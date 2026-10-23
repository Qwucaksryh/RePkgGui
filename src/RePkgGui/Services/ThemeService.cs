using System.Windows;
using Microsoft.Win32;
using RePkgGui.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace RePkgGui.Services;

/// <summary>主题：跟随系统 / 浅色 / 深色，使用 WPF-UI 的 ApplicationThemeManager。</summary>
public static class ThemeService
{
    private static bool _watching;

    public static AppThemeMode Mode { get; private set; } = AppThemeMode.System;

    public static void Apply(AppThemeMode mode)
    {
        Mode = mode;

        try
        {
            switch (mode)
            {
                case AppThemeMode.Dark:
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);
                    break;

                case AppThemeMode.Light:
                    ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
                    break;

                default:
                    ApplicationThemeManager.ApplySystemTheme(true);
                    break;
            }
        }
        catch
        {
            // 极早期调用（窗口尚未创建）时忽略异常，后续窗口初始化会再次套用
        }
    }

    /// <summary>监听系统主题变化，仅在「跟随系统」时才真正响应。</summary>
    public static void StartWatchingSystemTheme()
    {
        if (_watching)
            return;

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _watching = true;
    }

    public static void StopWatchingSystemTheme()
    {
        if (!_watching)
            return;

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _watching = false;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Mode != AppThemeMode.System)
            return;

        if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color)
            return;

        // SystemEvents 在独立线程触发，切回 UI 线程再套用主题
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        dispatcher.BeginInvoke(new Action(() =>
        {
            if (Mode == AppThemeMode.System)
                Apply(AppThemeMode.System);
        }));
    }
}
