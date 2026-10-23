using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace RePkgGui.Views;

public partial class AboutWindow
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version == null
            ? "版本 1.0.0"
            : $"版本 {version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

        RuntimeText.Text = $".NET {Environment.Version.Major} · WPF";

        Loaded += (_, _) => RePkgGui.Services.BackdropHelper.EnsureAvailable(this);
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } || string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 打不开浏览器时静默忽略
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
