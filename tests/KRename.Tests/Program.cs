using KRename.Core;

var failures = new List<string>();
Run("Preview composes replacement, prefix, suffix, and numbering", PreviewTest);
Run("Duplicate destinations are rejected", CollisionTest);
Run("No-change rows are ignored while real changes apply", NoChangeRowsAreIgnoredTest);
Run("Literal spaces can be replaced", LiteralSpaceReplacementTest);
Run("Bracket contents can be matched with a regular-expression wildcard", BracketWildcardRegexTest);
Run("Replacement filters execute from top to bottom", ReplacementOrderTest);
Run("Replacement rules support per-field case sensitivity", CaseSensitivityTest);
Run("Recursive preview includes subfolders", RecursiveTest);
Run("Output folders preserve recursive paths and support undo", OutputFolderTest);
Run("Advanced transforms compose in a predictable order", AdvancedTransformTest);
Run("Two-phase moves safely swap names", SwapTest);
Run("Undo restores the previous names", UndoTest);

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} test(s) failed:");
    failures.ForEach(x => Console.Error.WriteLine($"- {x}"));
    return 1;
}

Console.WriteLine("All KRename tests passed.");
return 0;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
    }
}

void PreviewTest()
{
    WithTempFolder((folder, journal) =>
    {
        File.WriteAllText(Path.Combine(folder, "photo old.jpg"), "x");
        var engine = new RenameEngine(journal);
        var plan = engine.BuildPreview(new RenameOptions
        {
            Folder = folder,
            FileNameReplacements = [new TextReplacementRule { Find = " old", ReplaceWith = "" }],
            Prefix = "trip-",
            Suffix = "-final",
            SequenceKind = SequenceKind.Numbers,
            SequenceStart = 7,
            SequenceDigits = 3
        });
        Equal("trip-photo-final007.jpg", plan.Single().NewName);
        Equal(RenameStatus.Ready, plan.Single().Status);
    });
}

void CollisionTest()
{
    WithTempFolder((folder, journal) =>
    {
        File.WriteAllText(Path.Combine(folder, "one.txt"), "1");
        File.WriteAllText(Path.Combine(folder, "two.txt"), "2");
        var engine = new RenameEngine(journal);
        var plan = engine.BuildPreview(new RenameOptions
        {
            Folder = folder,
            FileNameReplacements = [new TextReplacementRule { Find = ".+", ReplaceWith = "same" }],
            UseRegex = true
        });
        True(plan.All(x => x.Status == RenameStatus.Error), "Both colliding entries should be errors.");
        var blocked = engine.Apply(plan);
        True(!blocked.Success, "A plan containing red/error rows must never be applied.");
        True(File.Exists(Path.Combine(folder, "one.txt")) && File.Exists(Path.Combine(folder, "two.txt")),
            "Blocked plans must leave every source file untouched.");
    });
}

void RecursiveTest()
{
    WithTempFolder((folder, journal) =>
    {
        var nested = Path.Combine(folder, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(folder, "root.txt"), "root");
        File.WriteAllText(Path.Combine(nested, "child.txt"), "child");
        var engine = new RenameEngine(journal);
        var topOnly = engine.BuildPreview(new RenameOptions { Folder = folder, Prefix = "x-" });
        Equal(1, topOnly.Count);
        var recursive = engine.BuildPreview(new RenameOptions { Folder = folder, Prefix = "x-", IncludeSubdirectories = true });
        Equal(2, recursive.Count);
        Equal("nested", recursive.Single(x => x.CurrentName == "child.txt").RelativeDirectory);
    });
}

void OutputFolderTest()
{
    var root = Path.Combine(Path.GetTempPath(), $"krename-output-tests-{Guid.NewGuid():N}");
    var source = Path.Combine(root, "source");
    var output = Path.Combine(root, "output");
    var nested = Path.Combine(source, "nested");
    Directory.CreateDirectory(nested);
    Directory.CreateDirectory(output);
    var journal = Path.Combine(root, "journal.json");
    try
    {
        File.WriteAllText(Path.Combine(source, "root.txt"), "root");
        File.WriteAllText(Path.Combine(nested, "child.txt"), "child");
        var engine = new RenameEngine(journal);
        var plan = engine.BuildPreview(new RenameOptions
        {
            Folder = source,
            OutputFolder = output,
            IncludeSubdirectories = true,
            Prefix = "x-"
        });

        True(plan.All(x => x.Status == RenameStatus.Ready), "Output moves should be real actions.");
        Equal(Path.Combine(output, "x-root.txt"), plan.Single(x => x.CurrentName == "root.txt").TargetPath);
        Equal(Path.Combine(output, "nested", "x-child.txt"), plan.Single(x => x.CurrentName == "child.txt").TargetPath);

        var applied = engine.Apply(plan);
        True(applied.Success, applied.Message);
        True(File.Exists(Path.Combine(output, "x-root.txt")), "Root output file should exist.");
        True(File.Exists(Path.Combine(output, "nested", "x-child.txt")), "Nested output path should be created.");

        var undone = engine.UndoLast();
        True(undone.Success, undone.Message);
        True(File.Exists(Path.Combine(source, "root.txt")), "Undo should restore the root source file.");
        True(File.Exists(Path.Combine(nested, "child.txt")), "Undo should restore the nested source file.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void NoChangeRowsAreIgnoredTest()
{
    WithTempFolder((folder, journal) =>
    {
        var unchangedPath = Path.Combine(folder, "already.txt");
        var changingPath = Path.Combine(folder, "NeedsCase.txt");
        File.WriteAllText(unchangedPath, "unchanged");
        File.WriteAllText(changingPath, "changing");
        var engine = new RenameEngine(journal);
        var plan = engine.BuildPreview(new RenameOptions { Folder = folder, CaseTransform = NameCaseTransform.Lowercase });
        Equal(RenameStatus.Unchanged, plan.Single(x => x.CurrentName == "already.txt").Status);
        Equal(RenameStatus.Ready, plan.Single(x => x.CurrentName == "NeedsCase.txt").Status);
        var result = engine.Apply(plan);
        True(result.Success, result.Message);
        Equal(1, result.RenamedCount);
        True(File.Exists(unchangedPath), "The no-change file must be left alone.");
        True(File.Exists(Path.Combine(folder, "needscase.txt")), "The green row must be renamed.");
    });
}

void LiteralSpaceReplacementTest()
{
    WithTempFolder((folder, journal) =>
    {
        File.WriteAllText(Path.Combine(folder, "some file.txt"), "content");
        var plan = new RenameEngine(journal).BuildPreview(new RenameOptions
        {
            Folder = folder,
            FileNameReplacements = [new TextReplacementRule { Find = " ", ReplaceWith = "_", MatchCase = true }]
        });
        Equal("some_file.txt", plan.Single().NewName);
        Equal(RenameStatus.Ready, plan.Single().Status);
    });
}

void BracketWildcardRegexTest()
{
    WithTempFolder((folder, journal) =>
    {
        File.WriteAllText(Path.Combine(folder, "google.com - [sample123] trailing title_edited.mp4"), "content");
        var plan = new RenameEngine(journal).BuildPreview(new RenameOptions
        {
            Folder = folder,
            FileNameReplacements =
            [
                new TextReplacementRule
                {
                    Find = @"^google[.]com\s+-\s+\[[^]]+\].*$",
                    ReplaceWith = "matched",
                    MatchCase = true,
                    UseRegex = true
                }
            ]
        });
        Equal("matched.mp4", plan.Single().NewName);
        Equal(RenameStatus.Ready, plan.Single().Status);
    });
}

void ReplacementOrderTest()
{
    WithTempFolder((folder, journal) =>
    {
        File.WriteAllText(Path.Combine(folder, "a.txt"), "content");
        var plan = new RenameEngine(journal).BuildPreview(new RenameOptions
        {
            Folder = folder,
            FileNameReplacements =
            [
                new TextReplacementRule { Find = "a", ReplaceWith = "b", MatchCase = true },
                new TextReplacementRule { Find = "b", ReplaceWith = "c", MatchCase = true }
            ]
        });
        Equal("c.txt", plan.Single().NewName);
    });
}

void CaseSensitivityTest()
{
    WithTempFolder((folder, journal) =>
    {
        File.WriteAllText(Path.Combine(folder, "Photo.JPG"), "image");
        var engine = new RenameEngine(journal);
        var strict = engine.BuildPreview(new RenameOptions
        {
            Folder = folder,
            FileNameReplacements = [new TextReplacementRule { Find = "photo", ReplaceWith = "image", MatchCase = true }]
        });
        Equal(RenameStatus.Unchanged, strict.Single().Status);
        var relaxed = engine.BuildPreview(new RenameOptions
        {
            Folder = folder,
            FileNameReplacements = [new TextReplacementRule { Find = "photo", ReplaceWith = "image", MatchCase = false }]
        });
        Equal("image.JPG", relaxed.Single().NewName);
        Equal(RenameStatus.Ready, relaxed.Single().Status);
    });
}

void AdvancedTransformTest()
{
    WithTempFolder((folder, journal) =>
    {
        File.WriteAllText(Path.Combine(folder, "part_one_ABC123.TXT"), "data");
        var plan = new RenameEngine(journal).BuildPreview(new RenameOptions
        {
            Folder = folder,
            CaseTransform = NameCaseTransform.Lowercase,
            RemoveNumbers = true,
            SplitEnabled = true,
            SplitDelimiter = "_",
            SplitItemsToKeep = "1,3",
            SplitJoiner = "-",
            LeftTrimAction = TrimAction.Remove,
            LeftTrimCount = 1,
            RightTrimAction = TrimAction.Keep,
            RightTrimCount = 3,
            Prefix = "x-",
            Suffix = "-",
            ExtensionReplacements = [new TextReplacementRule { Find = "txt", ReplaceWith = "md" }],
            UseCurrentDateTime = false,
            CustomDateTime = new DateTime(2026, 9, 26, 14, 5, 6),
            AddYear = true,
            AddMonth = true,
            AddDay = true,
            AddHour = true,
            AddMinute = true,
            DateSeparator = "-",
            DateTimeSeparator = "_",
            TimeSeparator = "-",
            SequenceKind = SequenceKind.Letters,
            SequenceStart = 27,
            SequenceSeparator = "-"
        });
        Equal("x-abc-2026-09-26_14-05-AA.md", plan.Single().NewName);
        Equal(RenameStatus.Ready, plan.Single().Status);
    });
}

void SwapTest()
{
    WithTempFolder((folder, journal) =>
    {
        var a = Path.Combine(folder, "a.txt");
        var b = Path.Combine(folder, "b.txt");
        File.WriteAllText(a, "A");
        File.WriteAllText(b, "B");
        var plan = new[]
        {
            Ready(a, b),
            Ready(b, a)
        };
        var result = new RenameEngine(journal).Apply(plan);
        True(result.Success, result.Message);
        Equal("B", File.ReadAllText(a));
        Equal("A", File.ReadAllText(b));
    });
}

void UndoTest()
{
    WithTempFolder((folder, journal) =>
    {
        var oldPath = Path.Combine(folder, "old.txt");
        var newPath = Path.Combine(folder, "new.txt");
        File.WriteAllText(oldPath, "content");
        var engine = new RenameEngine(journal);
        var apply = engine.Apply([Ready(oldPath, newPath)]);
        True(apply.Success, apply.Message);
        True(File.Exists(newPath), "Renamed file should exist.");
        var undo = engine.UndoLast();
        True(undo.Success, undo.Message);
        True(File.Exists(oldPath), "Original filename should be restored.");
        True(!File.Exists(newPath), "Renamed filename should no longer exist.");
    });
}

RenamePlanItem Ready(string source, string target) => new()
{
    SourcePath = source,
    TargetPath = target,
    Status = RenameStatus.Ready,
    Message = "Ready"
};

void WithTempFolder(Action<string, string> action)
{
    var folder = Path.Combine(Path.GetTempPath(), $"krename-tests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(folder);
    try { action(folder, Path.Combine(folder, "journal.json")); }
    finally { Directory.Delete(folder, recursive: true); }
}

void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected '{expected}', got '{actual}'.");
}

void True(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
