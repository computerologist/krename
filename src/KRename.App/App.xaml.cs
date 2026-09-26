using System.Windows;
using System.Windows.Controls;

namespace KRename.App;

public partial class App : Application
{
    public App()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler(ContextMenu_Opened), handledEventsToo: true);
    }

    private static void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu) ThemeService.ApplyToContextMenu(menu);
    }
}
