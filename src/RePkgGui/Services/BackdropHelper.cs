using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace RePkgGui.Services;

/// <summary>
/// Mica 背景在 Windows 11 (build ≥ 22000) 才可用。
/// 旧系统上 WPF-UI 仍会把窗口背景置为透明，会出现“透视窗口”，
/// 这里显式关掉 Mica 并改用主题底色。
/// </summary>
public static class BackdropHelper
{
    private const int FirstMicaBuild = 22000;

    public static void EnsureAvailable(FluentWindow window)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, FirstMicaBuild))
            return;

        window.WindowBackdropType = WindowBackdropType.None;
        window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
    }
}
