namespace KRename.Core;

public enum RenameStatus { Ready, Unchanged, Error }
public enum NameCaseTransform { None, Lowercase, Uppercase }
public enum TrimAction { None, Remove, Keep }
public enum SequenceKind { None, Numbers, Letters }
public enum SequencePosition { BeforeName, AfterName }

public sealed class TextReplacementRule
{
    public string Find { get; set; } = "";
    public string ReplaceWith { get; set; } = "";
    public bool MatchCase { get; set; }
    public string Display => $"{Find}  →  {ReplaceWith}{(MatchCase ? "  (case-sensitive)" : "")}";
}

public sealed class RenameOptions
{
    public required string Folder { get; init; }
    public string? OutputFolder { get; init; }
    public string FileMask { get; init; } = "*.*";
    public bool IncludeSubdirectories { get; init; }
    public bool ReplaceEntireName { get; init; }
    public string EntireName { get; init; } = "";
    public IReadOnlyList<TextReplacementRule> FileNameReplacements { get; init; } = [];
    public IReadOnlyList<TextReplacementRule> ExtensionReplacements { get; init; } = [];
    public bool UseRegex { get; init; }
    public string Prefix { get; init; } = "";
    public string Suffix { get; init; } = "";
    public NameCaseTransform CaseTransform { get; init; }
    public bool RemoveNumbers { get; init; }
    public bool RemoveLetters { get; init; }
    public bool RemoveSpaces { get; init; }
    public bool RemoveSymbols { get; init; }
    public bool SplitEnabled { get; init; }
    public string SplitDelimiter { get; init; } = "";
    public string SplitItemsToKeep { get; init; } = "1";
    public string SplitJoiner { get; init; } = "";
    public bool SplitMatchCase { get; init; }
    public TrimAction LeftTrimAction { get; init; }
    public int LeftTrimCount { get; init; }
    public int LeftTrimIgnore { get; init; }
    public TrimAction RightTrimAction { get; init; }
    public int RightTrimCount { get; init; }
    public int RightTrimIgnore { get; init; }
    public bool AddYear { get; init; }
    public bool AddMonth { get; init; }
    public bool AddDay { get; init; }
    public bool AddHour { get; init; }
    public bool AddMinute { get; init; }
    public bool AddSecond { get; init; }
    public bool YearAtEnd { get; init; }
    public bool UseCurrentDateTime { get; init; } = true;
    public DateTime CustomDateTime { get; init; } = DateTime.Today;
    public string DateSeparator { get; init; } = "-";
    public string DateTimeSeparator { get; init; } = "_";
    public string TimeSeparator { get; init; } = "-";
    public SequenceKind SequenceKind { get; init; }
    public SequencePosition SequencePosition { get; init; } = SequencePosition.AfterName;
    public int SequenceStart { get; init; } = 1;
    public int SequenceDigits { get; init; } = 2;
    public string SequenceSeparator { get; init; } = "";
}

public sealed class RenamePlanItem
{
    public required string SourcePath { get; init; }
    public required string TargetPath { get; init; }
    public string CurrentName => Path.GetFileName(SourcePath);
    public string NewName => Path.GetFileName(TargetPath);
    public string DirectoryPath => Path.GetDirectoryName(SourcePath) ?? "";
    public string RelativeDirectory { get; init; } = ".";
    public long FileSize { get; init; }
    public string FileSizeDisplay => FormatFileSize(FileSize);
    public DateTime CreatedAt { get; init; }
    public DateTime ModifiedAt { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsHidden { get; init; }
    public RenameStatus Status { get; set; }
    public string Message { get; set; } = "";

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }
}

public sealed record RenameResult(bool Success, int RenamedCount, string Message);
internal sealed record RenameJournal(DateTimeOffset CreatedAt, List<RenameJournalItem> Items);
internal sealed record RenameJournalItem(string SourcePath, string TargetPath);
