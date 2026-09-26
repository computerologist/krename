using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace KRename.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    public AppSettings SavedSettings => _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings.Copy();
        EnableLoggingCheckBox.IsChecked = _settings.EnableLogging;
        LogPathTextBox.Text = string.IsNullOrWhiteSpace(_settings.LogFilePath)
            ? SettingsService.DefaultLogFilePath
            : _settings.LogFilePath;
        ConfirmBeforeRenameCheckBox.IsChecked = _settings.ConfirmBeforeRename;
        RememberLastFolderCheckBox.IsChecked = _settings.RememberLastFolder;
        RecurseByDefaultCheckBox.IsChecked = _settings.RecurseByDefault;
        DarkModeCheckBox.IsChecked = _settings.UseDarkMode;
    }

    private void BrowseLogButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Choose operation log location",
            Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = Path.GetFileName(LogPathTextBox.Text),
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(LogPathTextBox.Text))
                ? Path.GetDirectoryName(LogPathTextBox.Text)
                : null,
            AddExtension = true,
            DefaultExt = ".log"
        };
        if (dialog.ShowDialog(this) == true) LogPathTextBox.Text = dialog.FileName;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (EnableLoggingCheckBox.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(LogPathTextBox.Text))
            {
                MessageBox.Show(this, "Choose a log file location.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try { _ = Path.GetFullPath(LogPathTextBox.Text); }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"The log location is invalid: {ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _settings.EnableLogging = EnableLoggingCheckBox.IsChecked == true;
        _settings.LogFilePath = Path.GetFullPath(LogPathTextBox.Text.Trim());
        _settings.ConfirmBeforeRename = ConfirmBeforeRenameCheckBox.IsChecked == true;
        _settings.RememberLastFolder = RememberLastFolderCheckBox.IsChecked == true;
        _settings.RecurseByDefault = RecurseByDefaultCheckBox.IsChecked == true;
        _settings.UseDarkMode = DarkModeCheckBox.IsChecked == true;
        DialogResult = true;
    }
}
