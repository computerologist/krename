using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using KRename.App;
using KRename.Core;

namespace KRename.UiTests;

internal static class Program
{
    private static readonly List<string> Failures = [];

    [STAThread]
    private static int Main()
    {
        var application = new KRename.App.App();
        application.InitializeComponent();

        Run("Startup loads the remembered folder into both panes", StartupLoadsRememberedFolder);
        Run("Recent-folder selection survives refresh and moves to the top", RecentFolderSelectionSurvivesRefresh);
        Run("Selecting a source-folder history item refreshes immediately", SourceHistorySelectionRefreshesImmediately);
        Run("Saved recurse state loads nested files", SavedRecurseLoadsNestedFiles);
        Run("Empty top-level scans explain how to enable recursion", EmptyTopLevelExplainsRecursion);
        Run("Editable rename fields retain dropdown history", EditableFieldsRetainHistory);
        Run("A literal-space filename rule appears in the preview", LiteralSpaceRuleAppearsInPreview);
        Run("A per-filter regex matches variable bracket contents", PerFilterRegexMatchesBracketContents);
        Run("Replacement filters can be loaded and reordered", ReplacementFiltersLoadAndReorder);
        Run("Gray no-change rows do not disable applicable changes", GrayRowsDoNotDisableChanges);
        Run("Enter in folder and wildcard fields refreshes the file list", EnterRefreshesFileList);
        Run("Output folder preview preserves path and remembers history", OutputFolderPreviewAndHistory);
        Run("Applying a rename refreshes the current lists", ApplyRenameRefreshesLists);
        Run("Direct filename edits rename and refresh", DirectFilenameEditRefreshes);
        Run("Folder tree nodes load child directories lazily", FolderTreeLoadsChildren);
        Run("Dark mode and the options splitter are enabled by default", DarkModeAndSplitterDefaults);

        if (Failures.Count > 0)
        {
            Console.Error.WriteLine($"{Failures.Count} UI test(s) failed:");
            Failures.ForEach(x => Console.Error.WriteLine($"- {x}"));
            return 1;
        }

        Console.WriteLine("All KRename WPF integration tests passed.");
        return 0;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS  {name}");
        }
        catch (Exception ex)
        {
            Failures.Add($"{name}: {ex}");
        }
    }

    private static void StartupLoadsRememberedFolder()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "one.txt"), "1");
            File.WriteAllText(Path.Combine(first, "two.txt"), "2");
            var settings = Settings(first, first, second);
            var window = new MainWindow(settings, persistSettings: false);

            RaiseLoaded(window);

            Equal(first, Control<ComboBox>(window, "FolderComboBox").Text);
            Equal(2, Plan(window, "SourceGrid").Count);
            Equal(2, Plan(window, "PreviewGrid").Count);
        });
    }

    private static void RecentFolderSelectionSurvivesRefresh()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "first.txt"), "1");
            File.WriteAllText(Path.Combine(second, "second.txt"), "2");
            var settings = Settings(first, first, second);
            var window = new MainWindow(settings, persistSettings: false);
            var folder = Control<ComboBox>(window, "FolderComboBox");
            var refresh = Control<Button>(window, "LoadRefreshButton");

            folder.SelectedItem = second;
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Equal(second, folder.Text);
            Equal("second.txt", Plan(window, "SourceGrid").Single().CurrentName);
            Equal(second, settings.LastFolder);
            Equal(second, settings.RecentFolders[0]);
        });
    }

    private static void SavedRecurseLoadsNestedFiles()
    {
        WithTempFolders((first, second) =>
        {
            var nested = Path.Combine(first, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "child.txt"), "child");
            var settings = Settings(first, first).withRecurse(true);
            var window = new MainWindow(settings, persistSettings: false);

            RaiseLoaded(window);

            var item = Plan(window, "SourceGrid").Single();
            Equal("child.txt", item.CurrentName);
            Equal("nested", item.RelativeDirectory);
        });
    }

    private static void EmptyTopLevelExplainsRecursion()
    {
        WithTempFolders((first, second) =>
        {
            var nested = Path.Combine(first, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "child.txt"), "child");
            var window = new MainWindow(Settings(first, first), persistSettings: false);

            RaiseLoaded(window);

            Equal(0, Plan(window, "SourceGrid").Count);
            var status = Control<TextBlock>(window, "StatusTextBlock").Text;
            True(status.Contains("enable Include subfolders", StringComparison.OrdinalIgnoreCase), status);
        });
    }

    private static void EditableFieldsRetainHistory()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "one.txt"), "1");
            var settings = Settings(first, first);
            var window = new MainWindow(settings, persistSettings: false);
            var mask = Control<ComboBox>(window, "MaskTextBox");
            mask.Text = "*.txt";

            Control<Button>(window, "LoadRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Equal("*.txt", settings.FieldHistory["FileMask"][0]);
            True(mask.Items.Cast<string>().Contains("*.txt"), "The wildcard history dropdown should contain the used value.");
        });
    }

    private static void SourceHistorySelectionRefreshesImmediately()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "first.txt"), "1");
            File.WriteAllText(Path.Combine(second, "second.txt"), "2");
            var window = new MainWindow(Settings(first, first, second), persistSettings: false);
            var folder = Control<ComboBox>(window, "FolderComboBox");
            folder.SelectedItem = second;

            Invoke(window, "SourceFolder_DropDownClosed", folder, EventArgs.Empty);

            Equal(second, folder.Text);
            Equal(second, Control<ComboBox>(window, "DestinationComboBox").Text);
            True(Control<CheckBox>(window, "UseSourceOutputCheckBox").IsChecked == true,
                "Automatic output should remain enabled when the source history changes.");
            Equal("second.txt", Plan(window, "SourceGrid").Single().CurrentName);
        });
    }

    private static void LiteralSpaceRuleAppearsInPreview()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "some file.txt"), "1");
            var window = new MainWindow(Settings(first, first), persistSettings: false);
            Control<ComboBox>(window, "NameFindTextBox").Text = " ";
            Control<ComboBox>(window, "NameReplaceTextBox").Text = "_";
            Control<Button>(window, "AddNameRuleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Control<Button>(window, "LoadRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Equal("some_file.txt", Plan(window, "PreviewGrid").Single().NewName);
        });
    }

    private static void PerFilterRegexMatchesBracketContents()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "google.com  - [YSDUI#sd].txt"), "1");
            var window = new MainWindow(Settings(first, first), persistSettings: false);
            Control<ComboBox>(window, "NameFindTextBox").Text = @"^google[.]com  - \[[^]]*\]$";
            Control<ComboBox>(window, "NameReplaceTextBox").Text = "matched";
            Control<CheckBox>(window, "NameRuleRegexCheckBox").IsChecked = true;
            Control<Button>(window, "AddNameRuleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Control<Button>(window, "LoadRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Equal("matched.txt", Plan(window, "PreviewGrid").Single().NewName);
        });
    }

    private static void ReplacementFiltersLoadAndReorder()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "a.txt"), "1");
            var window = new MainWindow(Settings(first, first), persistSettings: false);
            var find = Control<ComboBox>(window, "NameFindTextBox");
            var replace = Control<ComboBox>(window, "NameReplaceTextBox");
            var add = Control<Button>(window, "AddNameRuleButton");
            find.Text = "a";
            replace.Text = "b";
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            find.Text = "b";
            replace.Text = "c";
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Control<Button>(window, "LoadRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Equal("c.txt", Plan(window, "PreviewGrid").Single().NewName);

            var rules = Control<ListBox>(window, "NameRulesList").Items.Cast<TextReplacementRule>().ToList();
            var loadButton = new Button { Tag = rules[0] };
            Invoke(window, "LoadNameRule_Click", loadButton, new RoutedEventArgs());
            Equal("a", find.Text);
            Equal("b", replace.Text);

            var moveButton = new Button { Tag = rules[1] };
            Invoke(window, "MoveNameRuleUp_Click", moveButton, new RoutedEventArgs());
            Equal("b.txt", Plan(window, "PreviewGrid").Single().NewName);
        });
    }

    private static void GrayRowsDoNotDisableChanges()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "already.txt"), "1");
            File.WriteAllText(Path.Combine(first, "NeedsCase.txt"), "2");
            var window = new MainWindow(Settings(first, first), persistSettings: false);
            Control<RadioButton>(window, "CaseLowerRadio").IsChecked = true;

            Control<Button>(window, "LoadRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var plan = Plan(window, "PreviewGrid");
            Equal(1, plan.Count(x => x.Status == RenameStatus.Ready));
            Equal(1, plan.Count(x => x.Status == RenameStatus.Unchanged));
            True(Control<Button>(window, "ApplyButton").IsEnabled, "Gray no-change rows should not disable Apply.");
        });
    }

    private static void EnterRefreshesFileList()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "one.txt"), "1");
            File.WriteAllText(Path.Combine(second, "two.txt"), "2");
            File.WriteAllText(Path.Combine(second, "skip.jpg"), "3");
            var window = new MainWindow(Settings(first, first, second), persistSettings: false);
            var folder = Control<ComboBox>(window, "FolderComboBox");
            var mask = Control<ComboBox>(window, "MaskTextBox");

            folder.Text = second;
            PressEnter(folder);
            Equal(2, Plan(window, "SourceGrid").Count);

            mask.Text = "*.txt";
            PressEnter(mask);
            Equal("two.txt", Plan(window, "SourceGrid").Single().CurrentName);
        });
    }

    private static void OutputFolderPreviewAndHistory()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "one.txt"), "1");
            var settings = Settings(first, first);
            var window = new MainWindow(settings, persistSettings: false);
            var useSource = Control<CheckBox>(window, "UseSourceOutputCheckBox");
            useSource.IsChecked = false;
            useSource.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Control<ComboBox>(window, "DestinationComboBox").Text = second;
            Control<ComboBox>(window, "PrefixTextBox").Text = "x-";

            Control<Button>(window, "LoadRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Equal(Path.Combine(second, "x-one.txt"), Plan(window, "PreviewGrid").Single().TargetPath);
            Equal(second, settings.LastOutputFolder);
            Equal(second, settings.RecentOutputFolders[0]);
        });
    }

    private static void ApplyRenameRefreshesLists()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "NeedsCase.txt"), "1");
            var settings = Settings(first, first);
            settings.ConfirmBeforeRename = false;
            var journal = Path.Combine(Path.GetDirectoryName(first)!, "apply-journal.json");
            var window = new MainWindow(settings, persistSettings: false, showDialogs: false, engine: new RenameEngine(journal));
            Control<RadioButton>(window, "CaseLowerRadio").IsChecked = true;
            Control<Button>(window, "LoadRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Control<Button>(window, "ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            True(File.Exists(Path.Combine(first, "needscase.txt")), "The lowercase rename should be applied.");
            var refreshed = Plan(window, "SourceGrid").Single();
            Equal("needscase.txt", refreshed.CurrentName);
            Equal(RenameStatus.Unchanged, refreshed.Status);
        });
    }

    private static void DirectFilenameEditRefreshes()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "old.txt"), "1");
            var journal = Path.Combine(Path.GetDirectoryName(first)!, "edit-journal.json");
            var window = new MainWindow(Settings(first, first), persistSettings: false, showDialogs: false, engine: new RenameEngine(journal));
            Control<Button>(window, "LoadRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var item = Plan(window, "SourceGrid").Single();
            var method = typeof(MainWindow).GetMethod("RenameSingleSourceFile", BindingFlags.Instance | BindingFlags.NonPublic)
                         ?? throw new Exception("Direct rename handler was not found.");

            method.Invoke(window, [item, "new.txt"]);

            True(File.Exists(Path.Combine(first, "new.txt")), "The direct filename edit should rename the file.");
            Equal("new.txt", Plan(window, "SourceGrid").Single().CurrentName);
        });
    }

    private static void FolderTreeLoadsChildren()
    {
        WithTempFolders((first, second) =>
        {
            var child = Path.Combine(first, "child");
            Directory.CreateDirectory(child);
            var node = new FolderTreeNode(first);

            node.LoadChildren();

            True(node.Children.Any(x => string.Equals(x.FullPath, child, StringComparison.OrdinalIgnoreCase)),
                "The expanded tree node should contain its child directory.");
            True(node.Children.All(x => !x.IsPlaceholder), "The loading placeholder should be removed after expansion.");
        });
    }

    private static void DarkModeAndSplitterDefaults()
    {
        WithTempFolders((first, second) =>
        {
            var settings = Settings(first, first);
            var window = new MainWindow(settings, persistSettings: false);

            True(settings.UseDarkMode, "Dark mode should default to enabled.");
            True(Control<MenuItem>(window, "ViewDarkModeMenuItem").IsChecked, "The dark-mode menu item should be checked.");
            _ = Control<GridSplitter>(window, "OptionsPanelSplitter");
        });
    }

    private static AppSettings Settings(string lastFolder, params string[] recent) => new()
    {
        LastFolder = lastFolder,
        RecentFolders = [.. recent],
        RememberLastFolder = true,
        ConfirmBeforeRename = true,
        RecurseByDefault = false
    };

    private static AppSettings withRecurse(this AppSettings settings, bool value)
    {
        settings.RecurseByDefault = value;
        return settings;
    }

    private static void RaiseLoaded(MainWindow window) =>
        window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

    private static void PressEnter(Control control)
    {
        using var source = new HwndSource(new HwndSourceParameters("KRename UI test input")
        {
            Width = 1,
            Height = 1,
            WindowStyle = unchecked((int)0x80000000)
        });
        control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        });
    }

    private static object? Invoke(MainWindow window, string methodName, params object[] arguments)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new Exception($"Handler '{methodName}' was not found.");
        return method.Invoke(window, arguments);
    }

    private static T Control<T>(MainWindow window, string name) where T : class =>
        window.FindName(name) as T ?? throw new Exception($"Control '{name}' was not found.");

    private static IReadOnlyList<RenamePlanItem> Plan(MainWindow window, string gridName) =>
        Control<DataGrid>(window, gridName).ItemsSource as IReadOnlyList<RenamePlanItem>
        ?? throw new Exception($"Grid '{gridName}' does not contain a rename plan.");

    private static void WithTempFolders(Action<string, string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"krename-ui-tests-{Guid.NewGuid():N}");
        var first = Path.Combine(root, "first");
        var second = Path.Combine(root, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try { action(first, second); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected '{expected}', got '{actual}'.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
