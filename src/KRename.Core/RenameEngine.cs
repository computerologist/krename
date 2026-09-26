using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KRename.Core;

public sealed class RenameEngine
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private readonly string _journalPath;

    public RenameEngine(string? journalPath = null)
    {
        _journalPath = journalPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KRename", "last-operation.json");
    }

    public bool CanUndo => File.Exists(_journalPath);

    public IReadOnlyList<RenamePlanItem> BuildPreview(RenameOptions options)
    {
        ValidateOptions(options);
        var nameRules = PrepareRules(options.FileNameReplacements, options);
        var extensionRules = PrepareRules(options.ExtensionReplacements, options);
        var keptSplitItems = options.SplitEnabled ? ParseItemSelection(options.SplitItemsToKeep) : [];
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = options.IncludeSubdirectories,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var masks = options.FileMask.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (masks.Length == 0) masks = ["*.*"];

        var files = new SortedSet<string>(PathComparer);
        foreach (var mask in masks)
        {
            foreach (var file in Directory.EnumerateFiles(options.Folder, mask, enumerationOptions))
                files.Add(Path.GetFullPath(file));
        }

        var plans = new List<RenamePlanItem>(files.Count);
        var sequence = options.SequenceStart;
        var currentDateTime = DateTime.Now;
        foreach (var source in files)
        {
            var directory = Path.GetDirectoryName(source)!;
            var relativeDirectory = Path.GetRelativePath(options.Folder, directory);
            var targetDirectory = string.IsNullOrWhiteSpace(options.OutputFolder)
                ? directory
                : relativeDirectory == "."
                    ? Path.GetFullPath(options.OutputFolder)
                    : Path.Combine(Path.GetFullPath(options.OutputFolder), relativeDirectory);
            var fileName = Path.GetFileName(source);
            var name = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName).TrimStart('.');

            name = options.ReplaceEntireName ? options.EntireName : name;
            name = ApplyRules(name, nameRules);
            name = ApplyCaseAndCleanup(name, options);
            if (options.SplitEnabled)
                name = ApplySplit(name, options.SplitDelimiter, keptSplitItems, options.SplitJoiner, options.SplitMatchCase);
            name = ApplyLeftTrim(name, options.LeftTrimAction, options.LeftTrimCount, options.LeftTrimIgnore);
            name = ApplyRightTrim(name, options.RightTrimAction, options.RightTrimCount, options.RightTrimIgnore);
            name = $"{options.Prefix}{name}{options.Suffix}";
            name += BuildDateTimeText(options, options.UseCurrentDateTime ? currentDateTime : options.CustomDateTime);
            name = ApplySequence(name, options, sequence);
            extension = ApplyRules(extension, extensionRules);
            sequence++;

            var newName = string.IsNullOrEmpty(extension) ? name : $"{name}.{extension}";
            var target = Path.Combine(targetDirectory, newName);
            var changed = !string.Equals(source, target, StringComparison.Ordinal);
            var metadata = ReadMetadata(source);
            var item = new RenamePlanItem
            {
                SourcePath = source,
                TargetPath = target,
                RelativeDirectory = relativeDirectory,
                FileSize = metadata.Size,
                CreatedAt = metadata.CreatedAt,
                ModifiedAt = metadata.ModifiedAt,
                IsReadOnly = metadata.IsReadOnly,
                IsHidden = metadata.IsHidden,
                Status = changed ? RenameStatus.Ready : RenameStatus.Unchanged,
                Message = changed ? "Will rename" : "No change — ignored"
            };

            var nameError = ValidateFileName(newName);
            if (nameError is not null)
            {
                item.Status = RenameStatus.Error;
                item.Message = nameError;
            }
            plans.Add(item);
        }

        ValidateCollisions(plans);
        return plans;
    }

    public RenameResult Apply(IReadOnlyList<RenamePlanItem> plan)
    {
        var items = plan.Where(x => x.Status == RenameStatus.Ready).ToList();
        if (plan.Any(x => x.Status == RenameStatus.Error))
            return new RenameResult(false, 0, "Resolve every red preview row before renaming. Grey no-change rows are safely ignored.");
        if (items.Count == 0)
            return new RenameResult(false, 0, "There are no changes to apply.");
        if (items.GroupBy(x => x.TargetPath, PathComparer).Any(x => x.Count() > 1))
            return new RenameResult(false, 0, "Two or more files have the same destination name.");
        foreach (var item in items)
        {
            var nameError = ValidateFileName(item.NewName);
            if (nameError is not null)
                return new RenameResult(false, 0, $"Invalid destination '{item.NewName}': {nameError}");
        }

        var validation = ValidateBeforeMove(items);
        if (validation is not null) return new RenameResult(false, 0, validation);
        var operations = items.Select(x => new MoveOperation(x.SourcePath, x.TargetPath, CreateTemporaryPath(x.SourcePath))).ToList();
        var result = ExecuteTwoPhase(operations);
        if (!result.Success) return result;

        try
        {
            SaveJournal(items);
            return new RenameResult(true, items.Count, $"Renamed {items.Count} file{(items.Count == 1 ? "" : "s")}.");
        }
        catch (Exception ex)
        {
            return new RenameResult(true, items.Count,
                $"Renamed {items.Count} file{(items.Count == 1 ? "" : "s")}, but the undo journal could not be saved: {ex.Message}");
        }
    }

    public RenameResult UndoLast()
    {
        if (!File.Exists(_journalPath)) return new RenameResult(false, 0, "There is no recorded operation to undo.");
        RenameJournal? journal;
        try { journal = JsonSerializer.Deserialize<RenameJournal>(File.ReadAllText(_journalPath)); }
        catch (Exception ex) { return new RenameResult(false, 0, $"The undo journal could not be read: {ex.Message}"); }
        if (journal is null || journal.Items.Count == 0) return new RenameResult(false, 0, "The undo journal is empty.");

        var inverse = journal.Items.Select(x => new RenamePlanItem
        {
            SourcePath = x.TargetPath,
            TargetPath = x.SourcePath,
            Status = RenameStatus.Ready,
            Message = "Undo"
        }).ToList();
        var validation = ValidateBeforeMove(inverse);
        if (validation is not null) return new RenameResult(false, 0, $"Undo stopped: {validation}");
        var operations = inverse.Select(x => new MoveOperation(x.SourcePath, x.TargetPath, CreateTemporaryPath(x.SourcePath))).ToList();
        var result = ExecuteTwoPhase(operations);
        if (result.Success) File.Delete(_journalPath);
        return result.Success
            ? new RenameResult(true, inverse.Count, $"Restored {inverse.Count} file{(inverse.Count == 1 ? "" : "s")}.")
            : result;
    }

    private static void ValidateOptions(RenameOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Folder);
        if (!Directory.Exists(options.Folder)) throw new DirectoryNotFoundException($"Folder not found: {options.Folder}");
        if (!string.IsNullOrWhiteSpace(options.OutputFolder) && !Directory.Exists(options.OutputFolder))
            throw new DirectoryNotFoundException($"Output folder not found: {options.OutputFolder}");
        if (options.SequenceDigits is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(options.SequenceDigits), "Sequence digits must be between 1 and 12.");
        if (options.SequenceKind == SequenceKind.Letters && options.SequenceStart < 1) throw new ArgumentOutOfRangeException(nameof(options.SequenceStart), "Letter sequences must start at 1 or higher.");
        if (options.SequenceKind == SequenceKind.Numbers && options.SequenceStart < 0) throw new ArgumentOutOfRangeException(nameof(options.SequenceStart), "Number sequences cannot start below zero.");
        if (options.LeftTrimCount < 0 || options.LeftTrimIgnore < 0 || options.RightTrimCount < 0 || options.RightTrimIgnore < 0)
            throw new ArgumentOutOfRangeException(nameof(options.LeftTrimCount), "Trim and ignore counts cannot be negative.");
        if (options.SplitEnabled && string.IsNullOrEmpty(options.SplitDelimiter)) throw new ArgumentException("Enter a split character or delimiter.");
    }

    private static List<PreparedRule> PrepareRules(IReadOnlyList<TextReplacementRule> rules, RenameOptions options)
    {
        var result = new List<PreparedRule>(rules.Count);
        foreach (var rule in rules.Where(x => !string.IsNullOrEmpty(x.Find)))
        {
            Regex? regex = null;
            if (options.UseRegex)
            {
                var regexOptions = rule.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                regex = new Regex(rule.Find, regexOptions, TimeSpan.FromSeconds(2));
            }
            result.Add(new PreparedRule(rule.Find, rule.ReplaceWith, rule.MatchCase, regex));
        }
        return result;
    }

    private static string ApplyRules(string value, IReadOnlyList<PreparedRule> rules)
    {
        foreach (var rule in rules)
        {
            value = rule.Regex is not null
                ? rule.Regex.Replace(value, rule.ReplaceWith)
                : value.Replace(rule.Find, rule.ReplaceWith, rule.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }
        return value;
    }

    private static string ApplyCaseAndCleanup(string value, RenameOptions options)
    {
        value = options.CaseTransform switch
        {
            NameCaseTransform.Lowercase => value.ToLowerInvariant(),
            NameCaseTransform.Uppercase => value.ToUpperInvariant(),
            _ => value
        };
        if (!options.RemoveNumbers && !options.RemoveLetters && !options.RemoveSpaces && !options.RemoveSymbols) return value;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var remove = (options.RemoveNumbers && char.IsDigit(character))
                || (options.RemoveLetters && char.IsLetter(character))
                || (options.RemoveSpaces && char.IsWhiteSpace(character))
                || (options.RemoveSymbols && !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character));
            if (!remove) builder.Append(character);
        }
        return builder.ToString();
    }

    private static SortedSet<int> ParseItemSelection(string selection)
    {
        var result = new SortedSet<int>();
        foreach (var token in selection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Contains('-'))
            {
                var bounds = token.Split('-', 2, StringSplitOptions.TrimEntries);
                if (bounds.Length != 2 || !int.TryParse(bounds[0], out var start) || !int.TryParse(bounds[1], out var end) || start < 1 || end < start)
                    throw new ArgumentException($"Invalid split item range: {token}");
                if (end - start > 10_000) throw new ArgumentException($"Split item range is too large: {token}");
                for (var item = start; item <= end; item++) result.Add(item);
            }
            else if (int.TryParse(token, out var index) && index >= 1) result.Add(index);
            else throw new ArgumentException($"Invalid split item number: {token}");
        }
        if (result.Count == 0) throw new ArgumentException("Enter at least one split item to keep.");
        return result;
    }

    private static string ApplySplit(string value, string delimiter, SortedSet<int> keptItems, string joiner, bool matchCase)
    {
        var parts = matchCase
            ? value.Split([delimiter], StringSplitOptions.None)
            : Regex.Split(value, Regex.Escape(delimiter), RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        return string.Join(joiner, keptItems.Where(x => x <= parts.Length).Select(x => parts[x - 1]));
    }

    private static string ApplyLeftTrim(string value, TrimAction action, int count, int ignore)
    {
        if (action == TrimAction.None) return value;
        var protectedLength = Math.Min(ignore, value.Length);
        var protectedText = value[..protectedLength];
        var candidate = value[protectedLength..];
        return action == TrimAction.Remove
            ? protectedText + candidate[Math.Min(count, candidate.Length)..]
            : protectedText + candidate[..Math.Min(count, candidate.Length)];
    }

    private static string ApplyRightTrim(string value, TrimAction action, int count, int ignore)
    {
        if (action == TrimAction.None) return value;
        var protectedLength = Math.Min(ignore, value.Length);
        var protectedText = protectedLength == 0 ? "" : value[^protectedLength..];
        var candidate = protectedLength == 0 ? value : value[..^protectedLength];
        if (action == TrimAction.Remove) return candidate[..Math.Max(0, candidate.Length - count)] + protectedText;
        return candidate[Math.Max(0, candidate.Length - count)..] + protectedText;
    }

    private static string BuildDateTimeText(RenameOptions options, DateTime value)
    {
        var date = new List<string>();
        if (!options.YearAtEnd && options.AddYear) date.Add(value.ToString("yyyy"));
        if (options.AddMonth) date.Add(value.ToString("MM"));
        if (options.AddDay) date.Add(value.ToString("dd"));
        if (options.YearAtEnd && options.AddYear) date.Add(value.ToString("yyyy"));
        var time = new List<string>();
        if (options.AddHour) time.Add(value.ToString("HH"));
        if (options.AddMinute) time.Add(value.ToString("mm"));
        if (options.AddSecond) time.Add(value.ToString("ss"));
        var dateText = string.Join(options.DateSeparator, date);
        var timeText = string.Join(options.TimeSeparator, time);
        if (dateText.Length > 0 && timeText.Length > 0) return dateText + options.DateTimeSeparator + timeText;
        return dateText + timeText;
    }

    private static string ApplySequence(string name, RenameOptions options, int sequence)
    {
        if (options.SequenceKind == SequenceKind.None) return name;
        var token = options.SequenceKind == SequenceKind.Numbers
            ? sequence.ToString().PadLeft(options.SequenceDigits, '0')
            : ToLetters(sequence);
        return options.SequencePosition == SequencePosition.BeforeName
            ? token + options.SequenceSeparator + name
            : name + options.SequenceSeparator + token;
    }

    private static string ToLetters(int value)
    {
        var result = "";
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private static FileMetadata ReadMetadata(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return new FileMetadata(info.Length, info.CreationTime, info.LastWriteTime, info.IsReadOnly, info.Attributes.HasFlag(FileAttributes.Hidden));
        }
        catch { return new FileMetadata(0, DateTime.MinValue, DateTime.MinValue, false, false); }
    }

    private static void ValidateCollisions(List<RenamePlanItem> plans)
    {
        foreach (var group in plans.Where(x => x.Status == RenameStatus.Ready).GroupBy(x => x.TargetPath, PathComparer))
        {
            if (group.Count() <= 1) continue;
            foreach (var item in group)
            {
                item.Status = RenameStatus.Error;
                item.Message = "Multiple files would receive this name";
            }
        }
        var movableSources = plans.Where(x => x.Status == RenameStatus.Ready).Select(x => x.SourcePath).ToHashSet(PathComparer);
        foreach (var item in plans.Where(x => x.Status == RenameStatus.Ready))
        {
            if (File.Exists(item.TargetPath) && !movableSources.Contains(item.TargetPath))
            {
                item.Status = RenameStatus.Error;
                item.Message = "A file with this name already exists";
            }
        }
    }

    private static string? ValidateBeforeMove(IReadOnlyList<RenamePlanItem> items)
    {
        var sources = items.Select(x => x.SourcePath).ToHashSet(PathComparer);
        foreach (var item in items)
        {
            if (!File.Exists(item.SourcePath)) return $"Source file no longer exists: {item.SourcePath}";
            if (File.Exists(item.TargetPath) && !sources.Contains(item.TargetPath)) return $"Target file already exists: {item.TargetPath}";
        }
        return null;
    }

    private static RenameResult ExecuteTwoPhase(List<MoveOperation> operations)
    {
        try
        {
            foreach (var targetDirectory in operations
                         .Select(x => Path.GetDirectoryName(x.Target)!)
                         .Distinct(PathComparer))
                Directory.CreateDirectory(targetDirectory);
        }
        catch (Exception ex)
        {
            return new RenameResult(false, 0, $"Rename stopped while preparing output folders: {ex.Message}");
        }

        var staged = new List<MoveOperation>();
        try
        {
            foreach (var operation in operations)
            {
                File.Move(operation.Source, operation.Temp);
                staged.Add(operation);
            }
        }
        catch (Exception ex)
        {
            foreach (var operation in staged.AsEnumerable().Reverse()) TryMove(operation.Temp, operation.Source);
            return new RenameResult(false, 0, $"Rename stopped while staging files: {ex.Message}");
        }

        var finalized = new List<MoveOperation>();
        try
        {
            foreach (var operation in operations)
            {
                File.Move(operation.Temp, operation.Target);
                finalized.Add(operation);
            }
        }
        catch (Exception ex)
        {
            foreach (var operation in finalized.AsEnumerable().Reverse()) TryMove(operation.Target, operation.Temp);
            foreach (var operation in operations.AsEnumerable().Reverse()) TryMove(operation.Temp, operation.Source);
            return new RenameResult(false, 0, $"Rename failed and was rolled back where possible: {ex.Message}");
        }
        return new RenameResult(true, operations.Count, "Rename completed.");
    }

    private void SaveJournal(List<RenamePlanItem> items)
    {
        var directory = Path.GetDirectoryName(_journalPath)!;
        Directory.CreateDirectory(directory);
        var journal = new RenameJournal(DateTimeOffset.Now, items.Select(x => new RenameJournalItem(x.SourcePath, x.TargetPath)).ToList());
        File.WriteAllText(_journalPath, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string CreateTemporaryPath(string source)
    {
        var directory = Path.GetDirectoryName(source)!;
        string candidate;
        do { candidate = Path.Combine(directory, $".krename-{Guid.NewGuid():N}.tmp"); }
        while (File.Exists(candidate));
        return candidate;
    }

    private static string? ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "The resulting name is empty";
        if (name is "." or "..") return "This name is reserved";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "The resulting name contains invalid characters";
        if (name.EndsWith(' ') || name.EndsWith('.')) return "Windows names cannot end in a space or period";
        var stem = Path.GetFileNameWithoutExtension(name);
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        if (reserved.Contains(stem, StringComparer.OrdinalIgnoreCase)) return "This name is reserved by Windows";
        return null;
    }

    private static void TryMove(string source, string target)
    {
        try { if (File.Exists(source) && !File.Exists(target)) File.Move(source, target); }
        catch { }
    }

    private sealed record PreparedRule(string Find, string ReplaceWith, bool MatchCase, Regex? Regex);
    private sealed record FileMetadata(long Size, DateTime CreatedAt, DateTime ModifiedAt, bool IsReadOnly, bool IsHidden);
    private sealed record MoveOperation(string Source, string Target, string Temp);
}
