using System.Windows;
using RePkgGui.Services;

namespace RePkgGui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 先把配置里的主题套上，再显示窗口，避免亮/暗闪烁
        var settings = SettingsService.Instance.Load();
        ThemeService.Apply(settings.Theme);
        ThemeService.StartWatchingSystemTheme();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeService.StopWatchingSystemTheme();
        base.OnExit(e);
    }
}
