using System.Collections.ObjectModel;
using System.IO;

namespace KRename.App;

public sealed class FolderTreeNode
{
    private bool _loaded;

    public string FullPath { get; }
    public string DisplayName { get; }
    public bool IsPlaceholder { get; }
    public ObservableCollection<FolderTreeNode> Children { get; } = [];

    public FolderTreeNode(string path, string? displayName = null)
    {
        FullPath = path;
        DisplayName = displayName ?? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(DisplayName)) DisplayName = path;
        Children.Add(Placeholder());
    }

    private FolderTreeNode()
    {
        FullPath = "";
        DisplayName = "Loading…";
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
}
