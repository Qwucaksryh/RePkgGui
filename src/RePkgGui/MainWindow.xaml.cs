using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RePkgGui.Services;
using RePkgGui.ViewModels;
using RePkgGui.Views;
using Wpf.Ui.Controls;

namespace RePkgGui;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel = new();
    private readonly Stopwatch _scrollWatch = Stopwatch.StartNew();

    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;

        _viewModel.ToastRequested += OnToastRequested;
        _viewModel.Logs.CollectionChanged += OnLogsChanged;

        FitSizeToWorkArea();

        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;
    }

    /// <summary>
    /// 高 DPI / 小屏幕下窗口默认尺寸会超出工作区，导致顶部栏被推到屏幕外。
    /// 这里在显示前把尺寸和最小尺寸一并收敛。
    /// </summary>
    private void FitSizeToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        var availableWidth = Math.Max(420, area.Width - 40);
        var availableHeight = Math.Max(360, area.Height - 40);

        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
    }

    /// <summary>CenterScreen 在小屏幕上会把窗口顶部推出可见区域，这里纠正位置。</summary>
    private void ClampPositionToWorkArea()
    {
        var area = SystemParameters.WorkArea;

        if (Left < area.Left)
            Left = area.Left + 12;

        if (Top < area.Top)
            Top = area.Top + 12;

        if (Left + ActualWidth > area.Right)
            Left = Math.Max(area.Left, area.Right - ActualWidth);

        if (Top + ActualHeight > area.Bottom)
            Top = Math.Max(area.Top, area.Bottom - ActualHeight);
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // 非 Win11 上关闭 Mica，避免透明背景导致“透视窗口”
        BackdropHelper.EnsureAvailable(this);
        ClampPositionToWorkArea();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _viewModel.ToastRequested -= OnToastRequested;
        _viewModel.Logs.CollectionChanged -= OnLogsChanged;
    }

    // ------------------------------------------------------------ 顶部栏

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow { Owner = this };
        _settingsWindow.SettingsSaved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void About_Click(object sender, RoutedEventArgs e) => ShowAboutWindow();

    private void ShowFormats_Click(object sender, RoutedEventArgs e) => ShowAboutWindow();

    private void ShowAboutWindow()
    {
        if (_aboutWindow is { IsLoaded: true })
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow { Owner = this };
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.ShowDialog();
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        var settings = SettingsService.Instance.Current;
        _viewModel.OutputDirectory = settings.OutputDirectory;
        _viewModel.ApplyTheme(settings.Theme, persist: false, notify: false);
    }

    // ------------------------------------------------------------ 选择输入

    private void DropZone_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (FindResource("PickerMenu") is System.Windows.Controls.ContextMenu menu)
        {
            menu.PlacementTarget = DropZone;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void PickFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要提取的文件",
            Filter = "支持的格式 (*.pkg;*.mpkg;*.tex)|*.pkg;*.mpkg;*.tex|" +
                     "Wallpaper Engine 包 (*.pkg;*.mpkg)|*.pkg;*.mpkg|" +
                     "TEX 贴图 (*.tex)|*.tex|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            _viewModel.SetInput(dialog.FileName);
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含 PKG / MPKG 的文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            _viewModel.SetInput(dialog.FolderName);
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择输出目录",
            Multiselect = false
        };

        var current = (_viewModel.OutputDirectory ?? string.Empty).Trim();
        if (Directory.Exists(current))
            dialog.InitialDirectory = current;

        if (dialog.ShowDialog(this) == true)
            _viewModel.OutputDirectory = dialog.FolderName;
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
        => _viewModel.OpenOutputCommand.Execute(null);

    // ------------------------------------------------------------ 拖拽

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var accepted = TryGetDroppedPath(e.Data, out _);

        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        SetDropHighlight(accepted);
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
        => SetDropHighlight(false);

    private void Window_Drop(object sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        e.Handled = true;

        if (TryGetDroppedPath(e.Data, out var path))
            _viewModel.SetInput(path);
    }

    private static bool TryGetDroppedPath(IDataObject? data, out string path)
    {
        path = string.Empty;

        if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
            return false;

        if (data.GetData(DataFormats.FileDrop) is not string[] items || items.Length == 0)
            return false;

        path = items[0];
        return !string.IsNullOrWhiteSpace(path);
    }

    private void SetDropHighlight(bool active)
    {
        DragOverlay.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

        if (active)
        {
            DashRect.Stroke = TryFindResource("AccentFillColorDefaultBrush") as Brush;
            DashRect.StrokeThickness = 2;
        }
        else
        {
            // 清掉本地值，让样式里的 hover 触发器重新生效
            DashRect.ClearValue(System.Windows.Shapes.Shape.StrokeProperty);
            DashRect.ClearValue(System.Windows.Shapes.Shape.StrokeThicknessProperty);
        }
    }

    // ------------------------------------------------------------ 日志滚动

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        // 高频日志下限制滚动频率，避免 UI 线程被 ScrollIntoView 拖慢
        if (_scrollWatch.ElapsedMilliseconds < 80)
            return;

        _scrollWatch.Restart();

        if (LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
    }

    // ------------------------------------------------------------ 提示

    private void OnToastRequested(object? sender, string message)
    {
        try
        {
            var snackbar = new Snackbar(SnackbarHost)
            {
                Title = message,
                Timeout = TimeSpan.FromSeconds(2.2),
                IsCloseButtonEnabled = false
            };

            snackbar.Show();
        }
        catch
        {
            // Snackbar 属于增强提示，失败可忽略
        }
    }
}
