using System.Windows;
using System.Windows.Controls;

namespace KRename.App;

public partial class ThemedTitleBar : UserControl
{
    public static readonly DependencyProperty MinimizeButtonVisibilityProperty = DependencyProperty.Register(
        nameof(MinimizeButtonVisibility), typeof(Visibility), typeof(ThemedTitleBar),
        new PropertyMetadata(Visibility.Visible));

    public static readonly DependencyProperty MaximizeButtonVisibilityProperty = DependencyProperty.Register(
        nameof(MaximizeButtonVisibility), typeof(Visibility), typeof(ThemedTitleBar),
        new PropertyMetadata(Visibility.Visible));

    public Visibility MinimizeButtonVisibility
    {
        get => (Visibility)GetValue(MinimizeButtonVisibilityProperty);
        set => SetValue(MinimizeButtonVisibilityProperty, value);
    }

    public Visibility MaximizeButtonVisibility
    {
        get => (Visibility)GetValue(MaximizeButtonVisibilityProperty);
        set => SetValue(MaximizeButtonVisibilityProperty, value);
    }

    public ThemedTitleBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var window = Window.GetWindow(this);
            if (window is null) return;
            window.StateChanged += Window_StateChanged;
            UpdateMaximizeButton(window);
        };
        Unloaded += (_, _) =>
        {
            var window = Window.GetWindow(this);
            if (window is not null) window.StateChanged -= Window_StateChanged;
        };
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is not null) window.WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is null) return;
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (sender is Window window) UpdateMaximizeButton(window);
    }

    private void UpdateMaximizeButton(Window window)
    {
        MaximizeButton.Content = window.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = window.WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }
}
