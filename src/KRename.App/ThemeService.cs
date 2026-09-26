using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace KRename.App;

public static class ThemeService
{
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
        ["ReadyForegroundBrush"] = "#8BE2A8",
        ["ErrorBackgroundBrush"] = "#4A2024",
        ["ErrorForegroundBrush"] = "#FF9DA4",
        ["UnchangedBackgroundBrush"] = "#30313A",
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
        ["ReadyForegroundBrush"] = "#166534",
        ["ErrorBackgroundBrush"] = "#FDE8E8",
        ["ErrorForegroundBrush"] = "#A11212",
        ["UnchangedBackgroundBrush"] = "#EEEEF2",
        ["UnchangedForegroundBrush"] = "#6A687A",
        ["SecondaryButtonBrush"] = "#ECEBF5"
    };

    public static void Apply(bool darkMode)
    {
        if (Application.Current is null) return;
        foreach (var (key, color) in darkMode ? Dark : Light)
            Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    public static void ApplyToTitleBar(Window window, bool darkMode)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var enabled = darkMode ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
