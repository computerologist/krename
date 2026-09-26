using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace KRename.App;

public static class ThemeService
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    private static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["AccentBrush"] = "#8B7CF6",
        ["AccentHoverBrush"] = "#A89CFF",
        ["WindowBackgroundBrush"] = "#15161D",
        ["SurfaceBrush"] = "#20212B",
        ["SurfaceAltBrush"] = "#292B36",
        ["BorderBrush"] = "#3B3D4A",
        ["TextPrimaryBrush"] = "#F3F3F7",
        ["TextSecondaryBrush"] = "#AAA9B7",
        ["MenuBrush"] = "#1B1C24",
        ["HeaderBrush"] = "#292A36",
        ["BeforeHeaderBrush"] = "#252A38",
        ["AfterHeaderBrush"] = "#2B2842",
        ["AlternateRowBrush"] = "#252630",
        ["ReadyBackgroundBrush"] = "#173B2A",
        ["ReadyAlternateBackgroundBrush"] = "#1D4934",
        ["ReadyForegroundBrush"] = "#8BE2A8",
        ["ErrorBackgroundBrush"] = "#4A2024",
        ["ErrorAlternateBackgroundBrush"] = "#57262B",
        ["ErrorForegroundBrush"] = "#FF9DA4",
        ["UnchangedBackgroundBrush"] = "#30313A",
        ["UnchangedAlternateBackgroundBrush"] = "#383944",
        ["UnchangedForegroundBrush"] = "#C0BFCA",
        ["SecondaryButtonBrush"] = "#343643"
    };

    private static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
    {
        ["AccentBrush"] = "#6D5CE7",
        ["AccentHoverBrush"] = "#5848CE",
        ["WindowBackgroundBrush"] = "#F4F4F8",
        ["SurfaceBrush"] = "#FFFFFF",
        ["SurfaceAltBrush"] = "#ECEBF5",
        ["BorderBrush"] = "#DDDDE8",
        ["TextPrimaryBrush"] = "#29283A",
        ["TextSecondaryBrush"] = "#6A687A",
        ["MenuBrush"] = "#F8F8FA",
        ["HeaderBrush"] = "#ECEBF5",
        ["BeforeHeaderBrush"] = "#ECEBF5",
        ["AfterHeaderBrush"] = "#E8E5FF",
        ["AlternateRowBrush"] = "#F8F8FB",
        ["ReadyBackgroundBrush"] = "#E5F6E9",
        ["ReadyAlternateBackgroundBrush"] = "#D8EFDE",
        ["ReadyForegroundBrush"] = "#166534",
        ["ErrorBackgroundBrush"] = "#FDE8E8",
        ["ErrorAlternateBackgroundBrush"] = "#F8DCDC",
        ["ErrorForegroundBrush"] = "#A11212",
        ["UnchangedBackgroundBrush"] = "#EEEEF2",
        ["UnchangedAlternateBackgroundBrush"] = "#E5E5EA",
        ["UnchangedForegroundBrush"] = "#6A687A",
        ["SecondaryButtonBrush"] = "#ECEBF5"
    };

    public static void Apply(bool darkMode)
    {
        if (Application.Current is null) return;
        var palette = darkMode ? Dark : Light;
        foreach (var (key, color) in palette)
            Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        Application.Current.Resources[SystemColors.ControlBrushKey] = Brush(palette["SurfaceAltBrush"]);
        Application.Current.Resources[SystemColors.ControlTextBrushKey] = Brush(palette["TextPrimaryBrush"]);
        Application.Current.Resources[SystemColors.MenuBrushKey] = Brush(palette["SurfaceBrush"]);
        Application.Current.Resources[SystemColors.MenuTextBrushKey] = Brush(palette["TextPrimaryBrush"]);
        Application.Current.Resources[SystemColors.HighlightBrushKey] = Brush(palette["AccentBrush"]);
        Application.Current.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
        Application.Current.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Brush(palette["AccentBrush"]);
        Application.Current.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brushes.White;
    }

    private static SolidColorBrush Brush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    public static void ApplyToContextMenu(System.Windows.Controls.ContextMenu menu)
    {
        if (Application.Current is null) return;
        menu.Background = (Brush)Application.Current.Resources["SurfaceBrush"];
        menu.Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"];
        menu.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
        foreach (var item in menu.Items)
        {
            if (item is System.Windows.Controls.MenuItem menuItem)
            {
                menuItem.Background = menu.Background;
                menuItem.Foreground = menu.Foreground;
            }
            else if (item is System.Windows.Controls.Separator separator)
            {
                separator.Background = menu.BorderBrush;
            }
        }
    }

    public static void ApplyToTitleBar(Window window, bool darkMode)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var enabled = darkMode ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));

        var palette = darkMode ? Dark : Light;
        SetTitleBarColor(handle, DwmwaCaptionColor, palette["WindowBackgroundBrush"]);
        SetTitleBarColor(handle, DwmwaTextColor, palette["TextPrimaryBrush"]);
        SetTitleBarColor(handle, DwmwaBorderColor, palette["BorderBrush"]);
    }

    private static void SetTitleBarColor(IntPtr handle, int attribute, string colorValue)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorValue);
        var colorRef = color.R | color.G << 8 | color.B << 16;
        _ = DwmSetWindowAttribute(handle, attribute, ref colorRef, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
