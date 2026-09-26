using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KRename.Core;
using Microsoft.Win32;

namespace KRename.App;

public partial class MainWindow : Window
{
    private readonly RenameEngine _engine;
    private readonly ObservableCollection<TextReplacementRule> _nameRules = [];
    private readonly ObservableCollection<TextReplacementRule> _extensionRules = [];
    private readonly ObservableCollection<string> _recentFolders = [];
    private readonly ObservableCollection<string> _recentOutputFolders = [];
    private readonly ObservableCollection<FolderTreeNode> _folderRoots = [];
    private readonly Dictionary<ComboBox, string> _historyFields = [];
    private readonly Dictionary<string, ObservableCollection<string>> _fieldHistories = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _isSmokeTest = Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
    private readonly bool _persistSettings;
    private readonly bool _showDialogs;
    private IReadOnlyList<RenamePlanItem> _sourcePlan = [];
    private IReadOnlyList<RenamePlanItem> _currentPlan = [];
    private AppSettings _settings;
    private string _manualOutputFolder = "";
    private RenamePlanItem? _contextSourceItem;
    private RenamePlanItem? _contextTargetItem;

    public ObservableCollection<FolderTreeNode> FolderRoots => _folderRoots;

    public MainWindow() : this(SettingsService.Load(), persistSettings: true, showDialogs: true, engine: null)
    {
    }

    public MainWindow(AppSettings settings, bool persistSettings) : this(settings, persistSettings, showDialogs: true, engine: null)
    {
    }

    public MainWindow(AppSettings settings, bool persistSettings, bool showDialogs) : this(settings, persistSettings, showDialogs, engine: null)
    {
    }

    public MainWindow(AppSettings settings, bool persistSettings, bool showDialogs, RenameEngine? engine)
    {
        _engine = engine ?? new RenameEngine();
        _settings = settings;
        _persistSettings = persistSettings;
        _showDialogs = showDialogs;
        ThemeService.Apply(_settings.UseDarkMode);
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeService.ApplyToTitleBar(this, _settings.UseDarkMode);
        DataContext = this;
        RegisterHistoryFields();
        foreach (var folder in _settings.RecentFolders ?? [])
            if (Directory.Exists(folder) && !_recentFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) _recentFolders.Add(folder);
        FolderComboBox.ItemsSource = _recentFolders;
        FolderComboBox.Text = _settings.RememberLastFolder && Directory.Exists(_settings.LastFolder)
            ? _settings.LastFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var folder in _settings.RecentOutputFolders ?? [])
            if (Directory.Exists(folder) && !_recentOutputFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) _recentOutputFolders.Add(folder);
        DestinationComboBox.ItemsSource = _recentOutputFolders;
        _manualOutputFolder = _settings.RememberLastFolder && Directory.Exists(_settings.LastOutputFolder)
            ? _settings.LastOutputFolder
            : "";
        UseSourceOutputCheckBox.IsChecked = _settings.UseSourceAsOutput;
        UpdateOutputMode();
        RecursiveCheckBox.IsChecked = _settings.RecurseByDefault;
        ViewDarkModeMenuItem.IsChecked = _settings.UseDarkMode;
        CustomDatePicker.SelectedDate = DateTime.Today;
        NameRulesList.ItemsSource = _nameRules;
        ExtensionRulesList.ItemsSource = _extensionRules;
        ApplyColumnVisibility();
        InitializeFolderTree();
        RefreshUndoState();
        if (_isSmokeTest)
            Loaded += (_, _) => Dispatcher.BeginInvoke(Close);
        else
            Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(GetSelectedFolder())) BuildPreview();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder containing files to rename",
            InitialDirectory = Directory.Exists(GetSelectedFolder()) ? GetSelectedFolder() : null
        };
        if (dialog.ShowDialog(this) == true)
        {
            FolderComboBox.Text = dialog.FolderName;
            RememberCurrentFolder();
            BuildPreview();
        }
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var outputFolder = _manualOutputFolder;
        var dialog = new OpenFolderDialog
        {
            Title = "Choose an optional output folder",
            InitialDirectory = outputFolder is not null && Directory.Exists(outputFolder)
                ? outputFolder
                : Directory.Exists(GetSelectedFolder()) ? GetSelectedFolder() : null
        };
        if (dialog.ShowDialog(this) != true) return;
        UseSourceOutputCheckBox.IsChecked = false;
        _settings.UseSourceAsOutput = false;
        _manualOutputFolder = dialog.FolderName;
        DestinationComboBox.Text = dialog.FolderName;
        UpdateOutputMode();
        RememberCurrentFolder();
        BuildPreview();
    }

    private void UseSourceOutput_Click(object sender, RoutedEventArgs e)
    {
        if (UseSourceOutputCheckBox.IsChecked == true)
        {
            var current = DestinationComboBox.Text.Trim();
            if (!string.IsNullOrEmpty(current) && !string.Equals(current, GetSelectedFolder(), StringComparison.OrdinalIgnoreCase))
                _manualOutputFolder = current;
        }
        _settings.UseSourceAsOutput = UseSourceOutputCheckBox.IsChecked == true;
        UpdateOutputMode();
        RememberCurrentFolder();
        BuildPreview();
    }

    private void UpdateOutputMode()
    {
        var automatic = UseSourceOutputCheckBox.IsChecked == true;
        DestinationComboBox.IsEnabled = !automatic;
        OutputBrowseButton.IsEnabled = !automatic;
        DestinationComboBox.Text = automatic ? GetSelectedFolder() : _manualOutputFolder;
    }

    private void AddNameRule_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(NameFindTextBox.Text))
        {
            MessageBox.Show(this, "Enter text or a pattern to find.", "Filename replacement", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!ValidateRegexPattern(NameFindTextBox.Text, NameRuleRegexCheckBox.IsChecked == true, "filename filter")) return;
        _nameRules.Add(new TextReplacementRule
        {
            Find = NameFindTextBox.Text,
            ReplaceWith = NameReplaceTextBox.Text,
            MatchCase = NameRuleCaseSensitiveCheckBox.IsChecked == true,
            UseRegex = NameRuleRegexCheckBox.IsChecked == true
        });
        RememberHistoryValue(NameFindTextBox, "NameFind");
        RememberHistoryValue(NameReplaceTextBox, "NameReplace");
        TrySaveSettings();
        NameFindTextBox.Text = "";
        NameReplaceTextBox.Text = "";
        RefreshPreviewIfFolderValid();
    }

    private void NameRuleField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || string.IsNullOrEmpty(NameFindTextBox.Text)) return;
        AddNameRule_Click(sender, e);
        e.Handled = true;
    }

    private void RemoveNameRule_Click(object sender, RoutedEventArgs e)
    {
        if (NameRulesList.SelectedItem is not TextReplacementRule rule) return;
        _nameRules.Remove(rule);
        RefreshPreviewIfFolderValid();
    }

    private void DeleteNameRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TextReplacementRule rule }) return;
        _nameRules.Remove(rule);
        RefreshPreviewIfFolderValid();
    }

    private void ClearNameRules_Click(object sender, RoutedEventArgs e)
    {
        _nameRules.Clear();
        RefreshPreviewIfFolderValid();
    }

    private void LoadNameRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TextReplacementRule rule }) return;
        NameFindTextBox.Text = rule.Find;
        NameReplaceTextBox.Text = rule.ReplaceWith;
        NameRuleCaseSensitiveCheckBox.IsChecked = rule.MatchCase;
        NameRuleRegexCheckBox.IsChecked = rule.UseRegex;
    }

    private void MoveNameRuleUp_Click(object sender, RoutedEventArgs e) => MoveRule(_nameRules, sender, -1);
    private void MoveNameRuleDown_Click(object sender, RoutedEventArgs e) => MoveRule(_nameRules, sender, 1);

    private void AddExtensionRule_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ExtensionFindTextBox.Text))
        {
            MessageBox.Show(this, "Enter an extension to find, without the dot.", "Extension replacement", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var extensionPattern = ExtensionFindTextBox.Text.TrimStart('.');
        if (!ValidateRegexPattern(extensionPattern, ExtensionRuleRegexCheckBox.IsChecked == true, "extension filter")) return;
        _extensionRules.Add(new TextReplacementRule
        {
            Find = extensionPattern,
            ReplaceWith = ExtensionReplaceTextBox.Text.TrimStart('.'),
            MatchCase = ExtensionRuleCaseSensitiveCheckBox.IsChecked == true,
            UseRegex = ExtensionRuleRegexCheckBox.IsChecked == true
        });
        RememberHistoryValue(ExtensionFindTextBox, "ExtensionFind");
        RememberHistoryValue(ExtensionReplaceTextBox, "ExtensionReplace");
        TrySaveSettings();
        ExtensionFindTextBox.Text = "";
        ExtensionReplaceTextBox.Text = "";
        RefreshPreviewIfFolderValid();
    }

    private void ExtensionRuleField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || string.IsNullOrEmpty(ExtensionFindTextBox.Text)) return;
        AddExtensionRule_Click(sender, e);
        e.Handled = true;
    }

    private void RemoveExtensionRule_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionRulesList.SelectedItem is not TextReplacementRule rule) return;
        _extensionRules.Remove(rule);
        RefreshPreviewIfFolderValid();
    }

    private void DeleteExtensionRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TextReplacementRule rule }) return;
        _extensionRules.Remove(rule);
        RefreshPreviewIfFolderValid();
    }

    private void ClearExtensionRules_Click(object sender, RoutedEventArgs e)
    {
        _extensionRules.Clear();
        RefreshPreviewIfFolderValid();
    }

    private void LoadExtensionRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TextReplacementRule rule }) return;
        ExtensionFindTextBox.Text = rule.Find;
        ExtensionReplaceTextBox.Text = rule.ReplaceWith;
        ExtensionRuleCaseSensitiveCheckBox.IsChecked = rule.MatchCase;
        ExtensionRuleRegexCheckBox.IsChecked = rule.UseRegex;
    }

    private void MoveExtensionRuleUp_Click(object sender, RoutedEventArgs e) => MoveRule(_extensionRules, sender, -1);
    private void MoveExtensionRuleDown_Click(object sender, RoutedEventArgs e) => MoveRule(_extensionRules, sender, 1);

    private void MoveRule(ObservableCollection<TextReplacementRule> rules, object sender, int offset)
    {
        if (sender is not Button { Tag: TextReplacementRule rule }) return;
        var currentIndex = rules.IndexOf(rule);
        var newIndex = currentIndex + offset;
        if (currentIndex < 0 || newIndex < 0 || newIndex >= rules.Count) return;
        rules.Move(currentIndex, newIndex);
        RefreshPreviewIfFolderValid();
    }

    private void RuleOptionChanged_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RefreshPreviewIfFolderValid();
        });
    }

    private void RefreshPreviewIfFolderValid()
    {
        if (Directory.Exists(GetSelectedFolder())) BuildPreview();
    }

    private bool ValidateRegexPattern(string pattern, bool useRegex, string label)
    {
        if (!useRegex) return true;
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(2));
            return true;
        }
        catch (ArgumentException ex)
        {
            var message = $"The {label} regular expression is invalid: {ex.Message}";
            StatusTextBlock.Text = message;
            if (_showDialogs)
                MessageBox.Show(this, message, "Invalid regular expression", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e) => BuildPreview();

    private void BuildPreview()
    {
        SetApplyEnabled(false);
        try
        {
            var selectedFolder = GetSelectedFolder();
            FolderComboBox.Text = selectedFolder;
            if (UseSourceOutputCheckBox.IsChecked == true) DestinationComboBox.Text = selectedFolder;
            var sequenceStart = ParseInteger(SequenceStartTextBox.Text, "Sequence start", allowNegative: false);
            var sequenceDigits = ParseInteger(SequenceDigitsTextBox.Text, "Sequence digits", allowNegative: false);
            var customDate = CustomDatePicker.SelectedDate ?? DateTime.Today;
            if (!TimeSpan.TryParse(CustomTimeTextBox.Text, out var customTime))
                throw new ArgumentException("Custom time must use a valid time such as 14:30:00.");

            var options = new RenameOptions
            {
                Folder = selectedFolder,
                OutputFolder = GetSelectedOutputFolder(),
                FileMask = MaskTextBox.Text.Trim(),
                IncludeSubdirectories = RecursiveCheckBox.IsChecked == true,
                ReplaceEntireName = ReplaceEntireNameCheckBox.IsChecked == true,
                EntireName = EntireNameTextBox.Text,
                FileNameReplacements = _nameRules.ToList(),
                ExtensionReplacements = _extensionRules.ToList(),
                UseRegex = false,
                Prefix = PrefixTextBox.Text,
                Suffix = SuffixTextBox.Text,
                CaseTransform = CaseLowerRadio.IsChecked == true
                    ? NameCaseTransform.Lowercase
                    : CaseUpperRadio.IsChecked == true ? NameCaseTransform.Uppercase : NameCaseTransform.None,
                RemoveNumbers = RemoveNumbersCheckBox.IsChecked == true,
                RemoveLetters = RemoveLettersCheckBox.IsChecked == true,
                RemoveSpaces = RemoveSpacesCheckBox.IsChecked == true,
                RemoveSymbols = RemoveSymbolsCheckBox.IsChecked == true,
                SplitEnabled = SplitEnabledCheckBox.IsChecked == true,
                SplitDelimiter = SplitDelimiterTextBox.Text,
                SplitItemsToKeep = SplitItemsTextBox.Text,
                SplitJoiner = SplitJoinerTextBox.Text,
                SplitMatchCase = SplitCaseSensitiveCheckBox.IsChecked == true,
                LeftTrimAction = (TrimAction)LeftTrimActionComboBox.SelectedIndex,
                LeftTrimCount = ParseInteger(LeftTrimCountTextBox.Text, "Left trim characters", allowNegative: false),
                LeftTrimIgnore = ParseInteger(LeftTrimIgnoreTextBox.Text, "Left trim ignored characters", allowNegative: false),
                RightTrimAction = (TrimAction)RightTrimActionComboBox.SelectedIndex,
                RightTrimCount = ParseInteger(RightTrimCountTextBox.Text, "Right trim characters", allowNegative: false),
                RightTrimIgnore = ParseInteger(RightTrimIgnoreTextBox.Text, "Right trim ignored characters", allowNegative: false),
                AddYear = AddYearCheckBox.IsChecked == true,
                AddMonth = AddMonthCheckBox.IsChecked == true,
                AddDay = AddDayCheckBox.IsChecked == true,
                AddHour = AddHourCheckBox.IsChecked == true,
                AddMinute = AddMinuteCheckBox.IsChecked == true,
                AddSecond = AddSecondCheckBox.IsChecked == true,
                YearAtEnd = YearAtEndCheckBox.IsChecked == true,
                UseCurrentDateTime = UseCurrentDateTimeCheckBox.IsChecked == true,
                CustomDateTime = customDate.Date + customTime,
                DateSeparator = DateSeparatorTextBox.Text,
                DateTimeSeparator = DateTimeSeparatorTextBox.Text,
                TimeSeparator = TimeSeparatorTextBox.Text,
                SequenceKind = (SequenceKind)SequenceKindComboBox.SelectedIndex,
                SequencePosition = (KRename.Core.SequencePosition)SequencePositionComboBox.SelectedIndex,
                SequenceStart = sequenceStart,
                SequenceDigits = sequenceDigits,
                SequenceSeparator = SequenceSeparatorTextBox.Text
            };

            _sourcePlan = _engine.BuildPreview(options);
            _currentPlan = _sourcePlan;
            SourceGrid.ItemsSource = _sourcePlan;
            PreviewGrid.ItemsSource = _currentPlan;
            var ready = _currentPlan.Count(x => x.Status == RenameStatus.Ready);
            var errors = _currentPlan.Count(x => x.Status == RenameStatus.Error);
            var unchanged = _currentPlan.Count(x => x.Status == RenameStatus.Unchanged);
            SetApplyEnabled(ready > 0 && errors == 0);
            RememberCurrentFolder();
            CaptureFieldHistories();
            StatusTextBlock.Text = _currentPlan.Count == 0
                ? RecursiveCheckBox.IsChecked == true
                    ? "No files matched the selected folder and wildcard, including subfolders."
                    : "No files matched at the folder's top level. If its files are in subfolders, enable Include subfolders and refresh."
                : errors > 0
                    ? $"{_currentPlan.Count} files loaded; {errors} red error row{(errors == 1 ? "" : "s")} block the rename. {unchanged} gray no-change row{(unchanged == 1 ? " is" : "s are")} ignored."
                    : ready > 0
                        ? $"{_currentPlan.Count} files loaded; {ready} green row{(ready == 1 ? "" : "s")} will be renamed and {unchanged} gray no-change row{(unchanged == 1 ? " is" : "s are")} ignored."
                        : $"{_currentPlan.Count} files loaded; all rows are gray because no filenames would change.";
            if (IsVisible) ExpandFolderTreeTo(selectedFolder);
        }
        catch (Exception ex)
        {
            ClearPreview();
            StatusTextBlock.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Unable to build preview", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlan.Any(x => x.Status == RenameStatus.Error))
        {
            SetApplyEnabled(false);
            MessageBox.Show(this, "Nothing was renamed. Resolve every red preview row before applying changes. Gray no-change rows are ignored.",
                "Rename blocked", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        var count = _currentPlan.Count(x => x.Status == RenameStatus.Ready);
        if (count == 0) return;
        if (_settings.ConfirmBeforeRename)
        {
            var loggingText = _settings.EnableLogging
                ? $"\n\nThe operation will be written to:\n{_settings.LogFilePath}"
                : "\n\nYou can reverse the operation with Edit > Undo last rename.";
            var answer = MessageBox.Show(this,
                $"Rename {count} file{(count == 1 ? "" : "s")} now?{loggingText}",
                "Confirm rename", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
        }

        var loggedPlan = _currentPlan.Where(x => x.Status == RenameStatus.Ready).ToList();
        var result = _engine.Apply(_currentPlan);
        StatusTextBlock.Text = result.Message;
        if (result.Success)
        {
            var logWarning = TryWriteLog("RENAME", result, loggedPlan);
            RefreshUndoState();
            BuildPreview();
            StatusTextBlock.Text = result.Message + " The current source and output preview have been refreshed."
                + (logWarning is null ? "" : $" {logWarning}");
            if (_showDialogs)
                MessageBox.Show(this, result.Message + (logWarning is null ? "" : $"\n\n{logWarning}"),
                    "Rename complete", MessageBoxButton.OK, logWarning is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(this, result.Message, "Rename stopped", MessageBoxButton.OK, MessageBoxImage.Warning);
            BuildPreview();
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "Restore the filenames from the last successful operation?",
            "Undo last rename", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        var result = _engine.UndoLast();
        var logWarning = result.Success ? TryWriteLog("UNDO", result, []) : null;
        if (result.Success) BuildPreview();
        else ClearPreview();
        StatusTextBlock.Text = result.Message + (result.Success ? " The lists have been refreshed." : "")
            + (logWarning is null ? "" : $" {logWarning}");
        MessageBox.Show(this, result.Message + (logWarning is null ? "" : $"\n\n{logWarning}"),
            result.Success ? "Undo complete" : "Undo stopped", MessageBoxButton.OK,
            result.Success && logWarning is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
        RefreshUndoState();
    }

    private void ClearPreview()
    {
        _sourcePlan = [];
        _currentPlan = [];
        SourceGrid.ItemsSource = null;
        PreviewGrid.ItemsSource = null;
        SetApplyEnabled(false);
    }

    private static int ParseInteger(string text, string label, bool allowNegative)
    {
        if (!int.TryParse(text, out var result) || (!allowNegative && result < 0))
            throw new ArgumentException($"{label} must be a {(allowNegative ? "" : "non-negative ")}whole number.");
        return result;
    }

    private void SettingsMenu_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _settings = dialog.SavedSettings;
        ThemeService.Apply(_settings.UseDarkMode);
        ThemeService.ApplyToTitleBar(this, _settings.UseDarkMode);
        ViewDarkModeMenuItem.IsChecked = _settings.UseDarkMode;
        if (_settings.RememberLastFolder)
        {
            _settings.LastFolder = FolderComboBox.Text.Trim();
            AddRecentFolder(_settings.LastFolder);
            _settings.UseSourceAsOutput = UseSourceOutputCheckBox.IsChecked == true;
            if (UseSourceOutputCheckBox.IsChecked != true) _manualOutputFolder = DestinationComboBox.Text.Trim();
            _settings.LastOutputFolder = _manualOutputFolder;
            AddRecentOutputFolder(_settings.LastOutputFolder);
        }
        else
        {
            _settings.LastFolder = "";
            _settings.RecentFolders.Clear();
            _recentFolders.Clear();
            _settings.LastOutputFolder = "";
            _settings.RecentOutputFolders.Clear();
            _recentOutputFolders.Clear();
        }
        try
        {
            SettingsService.Save(_settings);
            StatusTextBlock.Text = "Settings saved.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Settings could not be saved: {ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ColumnVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string column) return;
        switch (column)
        {
            case "Subfolder": _settings.ShowSubfolderColumn = item.IsChecked; break;
            case "Size": _settings.ShowSizeColumn = item.IsChecked; break;
            case "Created": _settings.ShowCreatedColumn = item.IsChecked; break;
            case "Modified": _settings.ShowModifiedColumn = item.IsChecked; break;
            case "ReadOnly": _settings.ShowReadOnlyColumn = item.IsChecked; break;
            case "Hidden": _settings.ShowHiddenColumn = item.IsChecked; break;
        }
        ApplyColumnVisibility();
        TrySaveSettings();
    }

    private void ColumnChooserContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is string column) item.IsChecked = IsColumnVisible(column);
        }
    }

    private bool IsColumnVisible(string column) => column switch
    {
        "Subfolder" => _settings.ShowSubfolderColumn,
        "Size" => _settings.ShowSizeColumn,
        "Created" => _settings.ShowCreatedColumn,
        "Modified" => _settings.ShowModifiedColumn,
        "ReadOnly" => _settings.ShowReadOnlyColumn,
        "Hidden" => _settings.ShowHiddenColumn,
        _ => false
    };

    private void ApplyColumnVisibility()
    {
        SetColumnVisibility(_settings.ShowSubfolderColumn, SourceSubfolderColumn, PreviewSubfolderColumn);
        SetColumnVisibility(_settings.ShowSizeColumn, SourceSizeColumn, PreviewSizeColumn);
        SetColumnVisibility(_settings.ShowCreatedColumn, SourceCreatedColumn, PreviewCreatedColumn);
        SetColumnVisibility(_settings.ShowModifiedColumn, SourceModifiedColumn, PreviewModifiedColumn);
        SetColumnVisibility(_settings.ShowReadOnlyColumn, SourceReadOnlyColumn, PreviewReadOnlyColumn);
        SetColumnVisibility(_settings.ShowHiddenColumn, SourceHiddenColumn, PreviewHiddenColumn);
        ViewSubfolderMenuItem.IsChecked = _settings.ShowSubfolderColumn;
        ViewSizeMenuItem.IsChecked = _settings.ShowSizeColumn;
        ViewCreatedMenuItem.IsChecked = _settings.ShowCreatedColumn;
        ViewModifiedMenuItem.IsChecked = _settings.ShowModifiedColumn;
        ViewReadOnlyMenuItem.IsChecked = _settings.ShowReadOnlyColumn;
        ViewHiddenMenuItem.IsChecked = _settings.ShowHiddenColumn;
    }

    private static void SetColumnVisibility(bool visible, params DataGridColumn[] columns)
    {
        foreach (var column in columns) column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RememberCurrentFolder()
    {
        _settings.RecurseByDefault = RecursiveCheckBox.IsChecked == true;
        _settings.UseSourceAsOutput = UseSourceOutputCheckBox.IsChecked == true;
        var selectedFolder = GetSelectedFolder();
        if (!_settings.RememberLastFolder || !Directory.Exists(selectedFolder))
        {
            TrySaveSettings();
            return;
        }
        _settings.LastFolder = selectedFolder;
        AddRecentFolder(_settings.LastFolder);
        if (UseSourceOutputCheckBox.IsChecked != true) _manualOutputFolder = DestinationComboBox.Text.Trim();
        _settings.LastOutputFolder = _manualOutputFolder;
        if (Directory.Exists(_manualOutputFolder)) AddRecentOutputFolder(_manualOutputFolder);
        TrySaveSettings();
    }

    private void AddRecentFolder(string folder)
    {
        if (!_settings.RememberLastFolder || !Directory.Exists(folder)) return;
        var existingIndex = -1;
        for (var index = 0; index < _recentFolders.Count; index++)
        {
            if (string.Equals(_recentFolders[index], folder, StringComparison.OrdinalIgnoreCase))
            {
                existingIndex = index;
                break;
            }
        }
        if (existingIndex > 0) _recentFolders.Move(existingIndex, 0);
        else if (existingIndex < 0) _recentFolders.Insert(0, folder);
        while (_recentFolders.Count > 12) _recentFolders.RemoveAt(_recentFolders.Count - 1);
        _settings.RecentFolders = [.. _recentFolders];
        FolderComboBox.Text = folder;
    }

    private string GetSelectedFolder()
    {
        var typedFolder = FolderComboBox.Text.Trim();
        if (!string.IsNullOrEmpty(typedFolder)) return typedFolder;
        return (FolderComboBox.SelectedItem as string)?.Trim() ?? "";
    }

    private string? GetSelectedOutputFolder()
    {
        if (UseSourceOutputCheckBox.IsChecked == true) return null;
        var typedFolder = DestinationComboBox.Text.Trim();
        if (!string.IsNullOrEmpty(typedFolder)) return typedFolder;
        var selected = (DestinationComboBox.SelectedItem as string)?.Trim();
        return string.IsNullOrEmpty(selected) ? null : selected;
    }

    private void AddRecentOutputFolder(string folder)
    {
        if (!_settings.RememberLastFolder || !Directory.Exists(folder)) return;
        var existingIndex = -1;
        for (var index = 0; index < _recentOutputFolders.Count; index++)
        {
            if (!string.Equals(_recentOutputFolders[index], folder, StringComparison.OrdinalIgnoreCase)) continue;
            existingIndex = index;
            break;
        }
        if (existingIndex > 0) _recentOutputFolders.Move(existingIndex, 0);
        else if (existingIndex < 0) _recentOutputFolders.Insert(0, folder);
        while (_recentOutputFolders.Count > 12) _recentOutputFolders.RemoveAt(_recentOutputFolders.Count - 1);
        _settings.RecentOutputFolders = [.. _recentOutputFolders];
        if (UseSourceOutputCheckBox.IsChecked != true) DestinationComboBox.Text = folder;
    }

    private void InitializeFolderTree()
    {
        _folderRoots.Clear();
        foreach (var drive in DriveInfo.GetDrives().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var label = drive.Name;
            try
            {
                if (drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel))
                    label = $"{drive.Name}  {drive.VolumeLabel}";
            }
            catch (IOException) { }
            _folderRoots.Add(new FolderTreeNode(drive.RootDirectory.FullName, label));
        }
    }

    private void ExpandFolderTreeTo(string folder)
    {
        try
        {
            var fullPath = Path.GetFullPath(folder);
            var rootPath = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(rootPath)) return;
            var current = _folderRoots.FirstOrDefault(x =>
                string.Equals(Path.GetFullPath(x.FullPath), Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase));
            if (current is null) return;

            foreach (var root in _folderRoots) ClearTreeSelection(root);
            var relative = Path.GetRelativePath(current.FullPath, fullPath);
            if (relative != ".")
            {
                var currentPath = current.FullPath;
                foreach (var part in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    current.IsExpanded = true;
                    current.LoadChildren();
                    currentPath = Path.Combine(currentPath, part);
                    var next = current.Children.FirstOrDefault(x => !x.IsPlaceholder &&
                        string.Equals(Path.GetFullPath(x.FullPath), Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase));
                    if (next is null) return;
                    current = next;
                }
            }

            current.IsSelected = true;
            var selectedNode = current;
            Dispatcher.BeginInvoke(() =>
            {
                FolderTree.UpdateLayout();
                FindTreeViewItem(FolderTree, selectedNode)?.BringIntoView();
            }, DispatcherPriority.Loaded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The file list remains usable even if part of the shell tree cannot be enumerated.
        }
    }

    private static void ClearTreeSelection(FolderTreeNode node)
    {
        node.IsSelected = false;
        foreach (var child in node.Children.Where(x => !x.IsPlaceholder)) ClearTreeSelection(child);
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct) return direct;
        foreach (var childItem in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(childItem) is not TreeViewItem child) continue;
            var match = FindTreeViewItem(child, item);
            if (match is not null) return match;
        }
        return null;
    }

    private void FolderTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: FolderTreeNode node }) node.LoadChildren();
    }

    private void FolderTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item is null) return;
        item.IsSelected = true;
        item.Focus();
    }

    private void UseTreeFolderAsSource_Click(object sender, RoutedEventArgs e)
    {
        if (FolderTree.SelectedItem is not FolderTreeNode { IsPlaceholder: false } node) return;
        FolderComboBox.Text = node.FullPath;
        RememberCurrentFolder();
        BuildPreview();
    }

    private void UseTreeFolderAsOutput_Click(object sender, RoutedEventArgs e)
    {
        if (FolderTree.SelectedItem is not FolderTreeNode { IsPlaceholder: false } node) return;
        UseSourceOutputCheckBox.IsChecked = false;
        _settings.UseSourceAsOutput = false;
        _manualOutputFolder = node.FullPath;
        DestinationComboBox.Text = node.FullPath;
        UpdateOutputMode();
        RememberCurrentFolder();
        BuildPreview();
    }

    private void OpenTreeFolderInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (FolderTree.SelectedItem is not FolderTreeNode { IsPlaceholder: false } node || !Directory.Exists(node.FullPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (_showDialogs)
                MessageBox.Show(this, $"Explorer could not be opened: {ex.Message}", "Open in Explorer",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Mask_DropDownClosed(object sender, EventArgs e)
    {
        if (MaskTextBox.SelectedItem is string selected) MaskTextBox.Text = selected;
        RefreshPreviewIfFolderValid();
    }

    private void SourceFolder_DropDownClosed(object sender, EventArgs e)
    {
        if (FolderComboBox.SelectedItem is string selected) FolderComboBox.Text = selected;
        if (Directory.Exists(GetSelectedFolder())) BuildPreview();
    }

    private void OutputFolder_DropDownClosed(object sender, EventArgs e)
    {
        if (DestinationComboBox.SelectedItem is string selected)
        {
            UseSourceOutputCheckBox.IsChecked = false;
            _settings.UseSourceAsOutput = false;
            _manualOutputFolder = selected;
            DestinationComboBox.Text = selected;
            UpdateOutputMode();
        }
        if (Directory.Exists(GetSelectedFolder())) BuildPreview();
    }

    private void SourceGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        _contextSourceItem = row?.Item as RenamePlanItem;
        if (_contextSourceItem is not null) SourceGrid.SelectedItem = _contextSourceItem;
    }

    private void PreviewGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        _contextTargetItem = row?.Item as RenamePlanItem;
        if (_contextTargetItem is not null) PreviewGrid.SelectedItem = _contextTargetItem;
    }

    private void SkipTargetFile_Click(object sender, RoutedEventArgs e)
    {
        var item = _contextTargetItem ?? PreviewGrid.SelectedItem as RenamePlanItem;
        _contextTargetItem = null;
        if (item is null) return;
        _currentPlan = _currentPlan.Where(x =>
            !string.Equals(x.SourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase)).ToList();
        PreviewGrid.ItemsSource = _currentPlan;
        var ready = _currentPlan.Count(x => x.Status == RenameStatus.Ready);
        var errors = _currentPlan.Count(x => x.Status == RenameStatus.Error);
        SetApplyEnabled(ready > 0 && errors == 0);
        StatusTextBlock.Text = $"Skipped '{item.CurrentName}' for this preview. {_currentPlan.Count} target row{(_currentPlan.Count == 1 ? " remains" : "s remain")}.";
    }

    private void OpenSelectedFileInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var item = _contextSourceItem ?? SourceGrid.SelectedItem as RenamePlanItem;
        _contextSourceItem = null;
        if (item is null || !File.Exists(item.SourcePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.SourcePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (_showDialogs)
                MessageBox.Show(this, $"Explorer could not be opened: {ex.Message}", "Open in Explorer",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SourceGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell?.Column != SourceFileNameColumn || cell.DataContext is not RenamePlanItem item) return;
        SourceGrid.CurrentCell = new DataGridCellInfo(item, SourceFileNameColumn);
        if (!SourceGrid.BeginEdit()) return;
        Dispatcher.BeginInvoke(() =>
        {
            var editor = FindVisualChild<TextBox>(cell);
            if (editor is null) return;
            editor.Focus();
            editor.SelectAll();
        }, DispatcherPriority.Input);
        e.Handled = true;
    }

    private void SourceGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Column != SourceFileNameColumn ||
            e.Row.Item is not RenamePlanItem item) return;
        var editor = e.EditingElement as TextBox ?? FindVisualChild<TextBox>(e.EditingElement);
        if (editor is null) return;
        var newName = editor.Text;
        e.Cancel = true;
        Dispatcher.BeginInvoke(() =>
        {
            SourceGrid.CancelEdit();
            RenameSingleSourceFile(item, newName);
        });
    }

    private void RenameSingleSourceFile(RenamePlanItem item, string newName)
    {
        if (string.Equals(item.CurrentName, newName, StringComparison.Ordinal)) return;
        var targetPath = Path.Combine(item.DirectoryPath, newName);
        var manualPlan = new RenamePlanItem
        {
            SourcePath = item.SourcePath,
            TargetPath = targetPath,
            RelativeDirectory = item.RelativeDirectory,
            Status = RenameStatus.Ready,
            Message = "Manual filename edit"
        };
        var result = _engine.Apply([manualPlan]);
        if (!result.Success)
        {
            if (_showDialogs)
                MessageBox.Show(this, result.Message, "Filename was not changed", MessageBoxButton.OK, MessageBoxImage.Warning);
            BuildPreview();
            return;
        }
        var logWarning = TryWriteLog("MANUAL RENAME", result, [manualPlan]);
        RefreshUndoState();
        BuildPreview();
        StatusTextBlock.Text = $"Renamed '{item.CurrentName}' to '{newName}'. Lists refreshed."
            + (logWarning is null ? "" : $" {logWarning}");
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
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

    private void RegisterHistoryFields()
    {
        RegisterHistory(MaskTextBox, "FileMask");
        RegisterHistory(EntireNameTextBox, "EntireName");
        RegisterHistory(PrefixTextBox, "Prefix");
        RegisterHistory(SuffixTextBox, "Suffix");
        RegisterHistory(NameFindTextBox, "NameFind");
        RegisterHistory(NameReplaceTextBox, "NameReplace");
        RegisterHistory(SplitDelimiterTextBox, "SplitDelimiter");
        RegisterHistory(SplitItemsTextBox, "SplitItems");
        RegisterHistory(SplitJoinerTextBox, "SplitJoiner");
        RegisterHistory(LeftTrimCountTextBox, "LeftTrimCount");
        RegisterHistory(LeftTrimIgnoreTextBox, "LeftTrimIgnore");
        RegisterHistory(RightTrimCountTextBox, "RightTrimCount");
        RegisterHistory(RightTrimIgnoreTextBox, "RightTrimIgnore");
        RegisterHistory(ExtensionFindTextBox, "ExtensionFind");
        RegisterHistory(ExtensionReplaceTextBox, "ExtensionReplace");
        RegisterHistory(CustomTimeTextBox, "CustomTime");
        RegisterHistory(DateSeparatorTextBox, "DateSeparator");
        RegisterHistory(DateTimeSeparatorTextBox, "DateTimeSeparator");
        RegisterHistory(TimeSeparatorTextBox, "TimeSeparator");
        RegisterHistory(SequenceStartTextBox, "SequenceStart");
        RegisterHistory(SequenceDigitsTextBox, "SequenceDigits");
        RegisterHistory(SequenceSeparatorTextBox, "SequenceSeparator");
    }

    private void RegisterHistory(ComboBox field, string key)
    {
        var currentText = field.Text;
        var saved = _settings.FieldHistory.TryGetValue(key, out var history) ? history : [];
        var values = new ObservableCollection<string>(saved
            .Where(x => x is not null)
            .Distinct(StringComparer.Ordinal)
            .Take(12));
        _historyFields[field] = key;
        _fieldHistories[key] = values;
        field.ItemsSource = values;
        field.Text = currentText;
    }

    private void CaptureFieldHistories()
    {
        foreach (var (field, key) in _historyFields) RememberHistoryValue(field, key);
        TrySaveSettings();
    }

    private void RememberHistoryValue(ComboBox field, string key)
    {
        var value = field.Text;
        if (value.Length == 0 || !_fieldHistories.TryGetValue(key, out var history)) return;
        var existingIndex = -1;
        for (var index = 0; index < history.Count; index++)
        {
            if (string.Equals(history[index], value, StringComparison.Ordinal))
            {
                existingIndex = index;
                break;
            }
        }
        if (existingIndex > 0) history.Move(existingIndex, 0);
        else if (existingIndex < 0) history.Insert(0, value);
        while (history.Count > 12) history.RemoveAt(history.Count - 1);
        _settings.FieldHistory[key] = [.. history];
        field.Text = value;
    }

    private void RefreshField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        BuildPreview();
        e.Handled = true;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isSmokeTest) return;
        CaptureFieldHistories();
        RememberCurrentFolder();
    }

    private void TrySaveSettings()
    {
        if (!_persistSettings) return;
        try { SettingsService.Save(_settings); }
        catch { }
    }

    private string? TryWriteLog(string action, RenameResult result, IReadOnlyList<RenamePlanItem> plan)
    {
        if (!_settings.EnableLogging) return null;
        try
        {
            var logPath = Path.GetFullPath(_settings.LogFilePath);
            var directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var lines = new List<string>
            {
                new('-', 72),
                $"{DateTimeOffset.Now:O}  {action}  {result.Message}"
            };
            lines.AddRange(plan.Select(x => $"{x.SourcePath}  ->  {x.TargetPath}"));
            File.AppendAllLines(logPath, lines, Encoding.UTF8);
            return null;
        }
        catch (Exception ex)
        {
            return $"The operation succeeded, but logging failed: {ex.Message}";
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            BuildPreview();
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            BrowseButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _engine.CanUndo)
        {
            UndoButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e) => Close();

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.UseDarkMode = ViewDarkModeMenuItem.IsChecked;
        ThemeService.Apply(_settings.UseDarkMode);
        ThemeService.ApplyToTitleBar(this, _settings.UseDarkMode);
        TrySaveSettings();
        StatusTextBlock.Text = _settings.UseDarkMode ? "Dark mode enabled." : "Light mode enabled.";
    }

    private void AboutMenu_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "KRename\n\nPreview-first batch file renaming for Windows.\nRed rows block the operation, green rows will be renamed, and gray no-change rows are ignored.\n\nDouble-click a source filename to edit it directly, or right-click it to reveal it in Explorer.",
            "About KRename", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SetApplyEnabled(bool enabled)
    {
        ApplyButton.IsEnabled = enabled;
        ApplyMenuItem.IsEnabled = enabled;
    }

    private void RefreshUndoState() => UndoMenuItem.IsEnabled = _engine.CanUndo;
}
