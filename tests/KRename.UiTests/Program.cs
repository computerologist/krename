using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
        Run("Selecting a file-mask history item refreshes both panes", MaskHistorySelectionRefreshes);
        Run("A literal-space filename rule appears in the preview", LiteralSpaceRuleAppearsInPreview);
        Run("A per-filter regex replaces only the matched filename prefix", PerFilterRegexMatchesBracketContents);
        Run("Enter creates non-empty filename and extension filters", EnterCreatesReplacementFilters);
        Run("Replacement filters can be loaded and reordered", ReplacementFiltersLoadAndReorder);
        Run("Gray no-change rows do not disable applicable changes", GrayRowsDoNotDisableChanges);
        Run("A target row can be skipped for the current preview", TargetRowsCanBeSkipped);
        Run("Enter in folder and wildcard fields refreshes the file list", EnterRefreshesFileList);
        Run("Output folder preview preserves path and remembers history", OutputFolderPreviewAndHistory);
        Run("Applying a rename refreshes the current lists", ApplyRenameRefreshesLists);
        Run("Direct filename edits rename and refresh", DirectFilenameEditRefreshes);
        Run("Folder tree nodes load child directories lazily", FolderTreeLoadsChildren);
        Run("Folder tree expands and selects the active source path", FolderTreeTracksActiveSource);
        Run("Dark mode and the options splitter are enabled by default", DarkModeAndSplitterDefaults);
        Run("Application title bars follow dark and light themes", ApplicationTitleBarsFollowTheme);
        Run("Generated context menus follow the active theme", ContextMenusFollowActiveTheme);
        Run("Ready and error preview rows alternate in both themes", PreviewStatusRowsAlternateInBothThemes);

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

    private static void MaskHistorySelectionRefreshes()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "keep.txt"), "1");
            File.WriteAllText(Path.Combine(first, "skip.jpg"), "2");
            var settings = Settings(first, first);
            settings.FieldHistory["FileMask"] = ["*.txt", "*.*"];
            var window = new MainWindow(settings, persistSettings: false);
            var mask = Control<ComboBox>(window, "MaskTextBox");
            mask.SelectedItem = "*.txt";

            Invoke(window, "Mask_DropDownClosed", mask, EventArgs.Empty);

            Equal("keep.txt", Plan(window, "SourceGrid").Single().CurrentName);
            Equal("keep.txt", Plan(window, "PreviewGrid").Single().CurrentName);
        });
    }

    private static void PerFilterRegexMatchesBracketContents()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "prefix google.com - [sample123] trailing title_edited.mp4"), "1");
            var window = new MainWindow(Settings(first, first), persistSettings: false);
            Control<ComboBox>(window, "NameFindTextBox").Text = @"google[.]com\s+-\s+\[[^]]+\]";
            Control<ComboBox>(window, "NameReplaceTextBox").Text = "matched";
            Control<CheckBox>(window, "NameRuleRegexCheckBox").IsChecked = true;
            Control<Button>(window, "AddNameRuleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Equal("prefix matched trailing title_edited.mp4", Plan(window, "PreviewGrid").Single().NewName);
        });
    }

    private static void EnterCreatesReplacementFilters()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "sample.txt"), "1");
            var window = new MainWindow(Settings(first, first), persistSettings: false);
            var nameFind = Control<ComboBox>(window, "NameFindTextBox");
            PressEnter(nameFind);
            Equal(0, Control<ListBox>(window, "NameRulesList").Items.Count);

            nameFind.Text = "sample";
            Control<ComboBox>(window, "NameReplaceTextBox").Text = "renamed";
            PressEnter(Control<ComboBox>(window, "NameReplaceTextBox"));
            Equal(1, Control<ListBox>(window, "NameRulesList").Items.Count);
            Equal("renamed.txt", Plan(window, "PreviewGrid").Single().NewName);

            Control<ComboBox>(window, "ExtensionFindTextBox").Text = "txt";
            Control<ComboBox>(window, "ExtensionReplaceTextBox").Text = "md";
            PressEnter(Control<ComboBox>(window, "ExtensionReplaceTextBox"));
            Equal(1, Control<ListBox>(window, "ExtensionRulesList").Items.Count);
            Equal("renamed.md", Plan(window, "PreviewGrid").Single().NewName);
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

    private static void TargetRowsCanBeSkipped()
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "keep.txt"), "1");
            File.WriteAllText(Path.Combine(first, "skip.txt"), "2");
            var settings = Settings(first, first);
            settings.ConfirmBeforeRename = false;
            var journal = Path.Combine(Path.GetDirectoryName(first)!, "skip-journal.json");
            var window = new MainWindow(settings, persistSettings: false, showDialogs: false, engine: new RenameEngine(journal));
            Control<ComboBox>(window, "PrefixTextBox").Text = "x-";
            Control<Button>(window, "LoadRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var sourceGrid = Control<DataGrid>(window, "SourceGrid");
            var previewGrid = Control<DataGrid>(window, "PreviewGrid");
            var skipped = Plan(window, "PreviewGrid").Single(x => x.CurrentName == "skip.txt");
            previewGrid.SelectedItem = skipped;

            Invoke(window, "SkipTargetFile_Click", new MenuItem(), new RoutedEventArgs());

            Equal(2, (sourceGrid.ItemsSource as IReadOnlyList<RenamePlanItem>)?.Count ?? -1);
            Equal(1, Plan(window, "PreviewGrid").Count);
            Equal("keep.txt", Plan(window, "PreviewGrid").Single().CurrentName);
            Control<Button>(window, "ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            True(File.Exists(Path.Combine(first, "x-keep.txt")), "The remaining target should be renamed.");
            True(File.Exists(Path.Combine(first, "skip.txt")), "The skipped source should be left unchanged.");
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
            window.Show();
            try
            {
                var grid = Control<DataGrid>(window, "SourceGrid");
                var item = Plan(window, "SourceGrid").Single();
                grid.SelectedItem = item;
                grid.ScrollIntoView(item);
                grid.UpdateLayout();
                grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[0]);
                True(grid.BeginEdit(), "The filename cell should enter edit mode.");
                grid.UpdateLayout();
                var row = grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow
                          ?? throw new Exception("The filename row was not materialized.");
                var editor = FindVisualChild<TextBox>(row)
                             ?? throw new Exception("The filename editor was not created.");
                editor.Text = "new.txt";
                _ = grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
                DrainDispatcher(window.Dispatcher);

                True(File.Exists(Path.Combine(first, "new.txt")), "The committed cell edit should rename the file.");
                Equal("new.txt", Plan(window, "SourceGrid").Single().CurrentName);
            }
            finally
            {
                window.Close();
            }
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

    private static void FolderTreeTracksActiveSource()
    {
        WithTempFolders((first, second) =>
        {
            var window = new MainWindow(Settings(first, first), persistSettings: false);

            Invoke(window, "ExpandFolderTreeTo", first);

            var rootPath = Path.GetPathRoot(first) ?? throw new Exception("The test path has no drive root.");
            var current = window.FolderRoots.Single(x =>
                string.Equals(Path.GetFullPath(x.FullPath), Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase));
            var relative = Path.GetRelativePath(current.FullPath, first);
            foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                True(current.IsExpanded, $"Tree node '{current.FullPath}' should be expanded.");
                var expected = Path.Combine(current.FullPath, part);
                current = current.Children.Single(x => !x.IsPlaceholder &&
                    string.Equals(Path.GetFullPath(x.FullPath), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase));
            }
            True(current.IsSelected, "The active source folder should be selected in the tree.");
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

    private static void ContextMenusFollowActiveTheme()
    {
        try
        {
            foreach (var darkMode in new[] { true, false })
            {
                ThemeService.Apply(darkMode);
                var item = new MenuItem { Header = "Cut" };
                var menu = new ContextMenu();
                menu.Items.Add(item);

                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));

                SameBrush(Application.Current.Resources["SurfaceBrush"], menu.Background);
                SameBrush(Application.Current.Resources["TextPrimaryBrush"], menu.Foreground);
                SameBrush(Application.Current.Resources["SurfaceBrush"], item.Background);
                SameBrush(Application.Current.Resources["TextPrimaryBrush"], item.Foreground);
            }
        }
        finally
        {
            ThemeService.Apply(true);
        }
    }

    private static void ApplicationTitleBarsFollowTheme()
    {
        WithTempFolders((first, second) =>
        {
            foreach (var darkMode in new[] { true, false })
            {
                var settings = Settings(first, first);
                settings.UseDarkMode = darkMode;
                var window = new MainWindow(settings, persistSettings: false);
                window.Show();
                try
                {
                    DrainDispatcher(window.Dispatcher);
                    Equal(WindowStyle.None, window.WindowStyle);
                    var titleBar = FindVisualChild<ThemedTitleBar>(window)
                                   ?? throw new Exception("The themed title bar was not rendered.");
                    var titleBarBorder = titleBar.FindName("TitleBarBackground") as Border
                                         ?? throw new Exception("The title-bar background was not rendered.");
                    SameBrush(Application.Current.Resources["MenuBrush"], titleBarBorder.Background);

                    var maximize = titleBar.FindName("MaximizeButton") as Button
                                   ?? throw new Exception("The maximize button was not created.");
                    maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Equal(WindowState.Maximized, window.WindowState);
                    maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Equal(WindowState.Normal, window.WindowState);

                    var settingsWindow = new SettingsWindow(settings) { Owner = window };
                    settingsWindow.Show();
                    try
                    {
                        DrainDispatcher(settingsWindow.Dispatcher);
                        Equal(WindowStyle.None, settingsWindow.WindowStyle);
                        var settingsTitleBar = FindVisualChild<ThemedTitleBar>(settingsWindow)
                                               ?? throw new Exception("The settings title bar was not rendered.");
                        var settingsBackground = settingsTitleBar.FindName("TitleBarBackground") as Border
                                                 ?? throw new Exception("The settings title-bar background was not rendered.");
                        SameBrush(Application.Current.Resources["MenuBrush"], settingsBackground.Background);
                        Equal(Visibility.Collapsed, settingsTitleBar.MinimizeButtonVisibility);
                        Equal(Visibility.Collapsed, settingsTitleBar.MaximizeButtonVisibility);
                    }
                    finally
                    {
                        settingsWindow.Close();
                    }
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    private static void PreviewStatusRowsAlternateInBothThemes()
    {
        try
        {
            foreach (var darkMode in new[] { true, false })
            {
                AssertPreviewAlternation(darkMode, RenameStatus.Ready,
                    "ReadyBackgroundBrush", "ReadyAlternateBackgroundBrush");
                AssertPreviewAlternation(darkMode, RenameStatus.Error,
                    "ErrorBackgroundBrush", "ErrorAlternateBackgroundBrush");
            }
        }
        finally
        {
            ThemeService.Apply(true);
        }
    }

    private static void AssertPreviewAlternation(bool darkMode, RenameStatus status,
        string evenBrushKey, string oddBrushKey)
    {
        WithTempFolders((first, second) =>
        {
            File.WriteAllText(Path.Combine(first, "alpha.txt"), "1");
            File.WriteAllText(Path.Combine(first, "beta.txt"), "2");
            var settings = Settings(first, first);
            settings.UseDarkMode = darkMode;
            var window = new MainWindow(settings, persistSettings: false);
            if (status == RenameStatus.Ready)
            {
                Control<ComboBox>(window, "PrefixTextBox").Text = "x-";
            }
            else
            {
                Control<CheckBox>(window, "ReplaceEntireNameCheckBox").IsChecked = true;
                Control<ComboBox>(window, "EntireNameTextBox").Text = "same";
            }
            Control<Button>(window, "LoadRefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            window.Show();
            try
            {
                var grid = Control<DataGrid>(window, "PreviewGrid");
                grid.UpdateLayout();
                var items = Plan(window, "PreviewGrid");
                Equal(2, items.Count);
                True(items.All(x => x.Status == status), $"Both preview rows should be {status}.");
                var firstRow = MaterializeRow(grid, items[0]);
                var secondRow = MaterializeRow(grid, items[1]);
                SameBrush(Application.Current.Resources[evenBrushKey], firstRow.Background);
                SameBrush(Application.Current.Resources[oddBrushKey], secondRow.Background);
                True(((SolidColorBrush)firstRow.Background).Color != ((SolidColorBrush)secondRow.Background).Color,
                    $"{status} rows should have visibly different alternating backgrounds in {(darkMode ? "dark" : "light")} mode.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static DataGridRow MaterializeRow(DataGrid grid, object item)
    {
        grid.ScrollIntoView(item);
        grid.UpdateLayout();
        return grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow
               ?? throw new Exception("The preview row was not materialized.");
    }

    private static void SameBrush(object expected, Brush actual)
    {
        if (expected is not SolidColorBrush expectedBrush || actual is not SolidColorBrush actualBrush)
            throw new Exception("Expected solid-color theme brushes.");
        Equal(expectedBrush.Color, actualBrush.Color);
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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            var descendant = FindVisualChild<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
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
