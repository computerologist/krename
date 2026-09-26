using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace KRename.App;

public sealed class FolderTreeNode : INotifyPropertyChanged
{
    private bool _loaded;
    private bool _isExpanded;
    private bool _isSelected;

    public string FullPath { get; }
    public string DisplayName { get; }
    public string IconGlyph { get; }
    public bool IsPlaceholder { get; }
    public ObservableCollection<FolderTreeNode> Children { get; } = [];
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FolderTreeNode(string path, string? displayName = null)
    {
        FullPath = path;
        DisplayName = displayName ?? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(DisplayName)) DisplayName = path;
        IconGlyph = string.Equals(Path.GetPathRoot(path), path, StringComparison.OrdinalIgnoreCase) ? "💽" : "📁";
        Children.Add(Placeholder());
    }

    private FolderTreeNode()
    {
        FullPath = "";
        DisplayName = "Loading…";
        IconGlyph = "";
        IsPlaceholder = true;
        _loaded = true;
    }

    public void LoadChildren()
    {
        if (_loaded || IsPlaceholder) return;
        _loaded = true;
        Children.Clear();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(FullPath)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                Children.Add(new FolderTreeNode(directory));
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private static FolderTreeNode Placeholder() => new();

    private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
