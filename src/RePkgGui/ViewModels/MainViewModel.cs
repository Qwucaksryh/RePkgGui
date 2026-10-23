using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using RePkgGui.Models;
using RePkgGui.Services;
using Wpf.Ui.Controls;
using Clipboard = System.Windows.Clipboard;

namespace RePkgGui.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxLogEntries = 800;

    private readonly RepkgService _service = new();
    private readonly SettingsService _settings = SettingsService.Instance;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _inspectCts;
    private string? _lastOutputRoot;

    private string _inputPath = string.Empty;
    private string _outputDirectory;
    private string _selectionTitle = string.Empty;
    private string _selectionDetail = string.Empty;
    private bool _hasSelection;
    private SymbolRegular _selectionIcon = SymbolRegular.Folder24;
    private bool _isBusy;
    private double _progressValue;
    private string _statusText = "等待开始";
    private bool _isInfoBarOpen;
    private InfoBarSeverity _infoSeverity = InfoBarSeverity.Informational;
    private string _infoTitle = string.Empty;
    private string _infoMessage = string.Empty;
    private AppThemeMode _themeMode;
    private SymbolRegular _themeSymbol = SymbolRegular.Desktop24;
    private string _themeTooltip = string.Empty;

    public MainViewModel()
    {
        var settings = _settings.Current;
        _outputDirectory = settings.OutputDirectory;
        _themeMode = settings.Theme;
        RefreshThemeVisual();

        StartOrCancelCommand = new RelayCommand(StartOrCancel);
        PasteCommand = new RelayCommand(PasteFromClipboard);
        CycleThemeCommand = new RelayCommand(CycleTheme);
        ClearLogCommand = new RelayCommand(() => Logs.Clear());
        OpenOutputCommand = new RelayCommand(OpenOutputFolder);
        OpenOutputDirectoryCommand = new RelayCommand(OpenOutputDirectory);
    }

    /// <summary>请求弹出 Snackbar 提示（由 View 显示）。</summary>
    public event EventHandler<string>? ToastRequested;

    public RelayCommand StartOrCancelCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand CycleThemeCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand OpenOutputCommand { get; }

    /// <summary>打开输入框里写的输出目录（不管有没有跑过任务）。</summary>
    public RelayCommand OpenOutputDirectoryCommand { get; }

    public ObservableCollection<LogEntry> Logs { get; } = new();

    // ------------------------------------------------------------ 状态

    public string InputPath
    {
        get => _inputPath;
        set
        {
            if (!SetProperty(ref _inputPath, value))
                return;

            OnPropertyChanged(nameof(IsStartEnabled));
            ScheduleInspection();
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set => SetProperty(ref _outputDirectory, value);
    }

    public string SelectionTitle
    {
        get => _selectionTitle;
        private set => SetProperty(ref _selectionTitle, value);
    }

    public string SelectionDetail
    {
        get => _selectionDetail;
        private set => SetProperty(ref _selectionDetail, value);
    }

    public bool HasSelection
    {
        get => _hasSelection;
        private set => SetProperty(ref _hasSelection, value);
    }

    public SymbolRegular SelectionIcon
    {
        get => _selectionIcon;
        private set => SetProperty(ref _selectionIcon, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            OnPropertyChanged(nameof(IsStartEnabled));
            OnPropertyChanged(nameof(StartButtonText));
            OnPropertyChanged(nameof(StartIcon));
            OnPropertyChanged(nameof(StartAppearance));
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        set
        {
            if (SetProperty(ref _progressValue, value))
                OnPropertyChanged(nameof(ProgressText));
        }
    }

    public string ProgressText => $"{ProgressValue:F0}%";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsInfoBarOpen
    {
        get => _isInfoBarOpen;
        set => SetProperty(ref _isInfoBarOpen, value);
    }

    public InfoBarSeverity InfoSeverity
    {
        get => _infoSeverity;
        private set => SetProperty(ref _infoSeverity, value);
    }

    public string InfoTitle
    {
        get => _infoTitle;
        private set => SetProperty(ref _infoTitle, value);
    }

    public string InfoMessage
    {
        get => _infoMessage;
        private set => SetProperty(ref _infoMessage, value);
    }

    public AppThemeMode AppThemeMode
    {
        get => _themeMode;
        private set => SetProperty(ref _themeMode, value);
    }

    public SymbolRegular ThemeSymbol
    {
        get => _themeSymbol;
        private set => SetProperty(ref _themeSymbol, value);
    }

    public string ThemeTooltip
    {
        get => _themeTooltip;
        private set => SetProperty(ref _themeTooltip, value);
    }

    public bool IsStartEnabled => IsBusy || !string.IsNullOrWhiteSpace(InputPath);

    public string StartButtonText => IsBusy ? "取消提取" : "开始提取";

    public SymbolRegular StartIcon => IsBusy ? SymbolRegular.Dismiss20 : SymbolRegular.ArrowDownload24;

    public ControlAppearance StartAppearance =>
        IsBusy ? ControlAppearance.Danger : ControlAppearance.Primary;

    // ------------------------------------------------------------ 输入

    public void SetInput(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        InputPath = path.Trim().Trim('"');
    }

    private static SelectionInfo Describe(string path)
    {
        if (path.Length == 0)
            return SelectionInfo.None;

        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                var extension = info.Extension.ToLowerInvariant();

                var icon = extension == ".tex" ? SymbolRegular.Image24 : SymbolRegular.Box24;
                var type = extension switch
                {
                    ".pkg" => "PKG 包",
                    ".mpkg" => "MPKG 包",
                    ".tex" => "TEX 贴图",
                    _ => extension.TrimStart('.').ToUpperInvariant()
                };

                return new SelectionInfo(true, info.Name, $"{type} · {FormatSize(info.Length)}", icon);
            }

            if (Directory.Exists(path))
            {
                var name = new DirectoryInfo(path).Name;
                if (string.IsNullOrEmpty(name))
                    name = path;

                var (packages, textures) = RepkgService.PreviewDirectory(path);
                var detail = packages > 0
                    ? $"包含 {packages} 个 PKG / MPKG"
                    : textures > 0
                        ? $"包含 {textures} 个 TEX"
                        : "未找到 PKG / MPKG / TEX";

                return new SelectionInfo(true, name, detail, SymbolRegular.Folder24);
            }
        }
        catch
        {
            return SelectionInfo.None;
        }

        return SelectionInfo.None;
    }

    private async void ScheduleInspection()
    {
        _inspectCts?.Cancel();
        _inspectCts = new CancellationTokenSource();
        var token = _inspectCts.Token;

        try
        {
            await Task.Delay(180, token);

            var path = (InputPath ?? string.Empty).Trim().Trim('"');
            var info = await Task.Run(() => Describe(path), token);

            if (token.IsCancellationRequested)
                return;

            SelectionTitle = info.Title;
            SelectionDetail = info.Detail;
            SelectionIcon = info.Icon;
            HasSelection = info.HasSelection;
        }
        catch (OperationCanceledException)
        {
            // 用户还在输入，忽略
        }
        catch
        {
            HasSelection = false;
        }
    }

    private sealed record SelectionInfo(bool HasSelection, string Title, string Detail, SymbolRegular Icon)
    {
        public static readonly SelectionInfo None =
            new(false, string.Empty, string.Empty, SymbolRegular.Folder24);
    }

    /// <summary>拼出跳过说明，分开告知“已存在”与“被规则忽略”。</summary>
    private static string BuildSkippedSummary(ExtractionSummary summary, string separator)
    {
        var parts = new List<string>();
        if (summary.ExistingSkipped > 0)
            parts.Add($"{separator}已存在跳过 {summary.ExistingSkipped} 个");
        if (summary.FilteredSkipped > 0)
            parts.Add($"{separator}被规则忽略 {summary.FilteredSkipped} 个");

        return string.Concat(parts);
    }

    /// <summary>把 ".json, bin" 这类输入规范化成带点的扩展名列表。</summary>
    public static IReadOnlyList<string> ParseExtensions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw.Split(new[] { ',', ';', ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.StartsWith('.') ? x : "." + x)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024d:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / 1024d / 1024d:F1} MB";
        return $"{bytes / 1024d / 1024d / 1024d:F2} GB";
    }

    // ------------------------------------------------------------ 粘贴

    private void PasteFromClipboard()
    {
        try
        {
            string? raw = null;

            if (Clipboard.ContainsText())
                raw = Clipboard.GetText();
            else if (Clipboard.ContainsFileDropList())
            {
                var list = Clipboard.GetFileDropList();
                if (list.Count > 0)
                    raw = list[0];
            }

            var path = NormalizePath(raw);
            if (path.Length == 0)
            {
                ShowInfo(InfoBarSeverity.Warning, "剪贴板里没有路径",
                    "请先复制一个文件/文件夹路径，或在资源管理器中复制文件后再试。");
                return;
            }

            SetInput(path);
            ShowInfo(InfoBarSeverity.Success, "已粘贴路径", path);
            ToastRequested?.Invoke(this, "已从剪贴板粘贴路径");
        }
        catch (Exception ex)
        {
            ShowInfo(InfoBarSeverity.Error, "读取剪贴板失败", ex.Message);
        }
    }

    private static string NormalizePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var value = raw.Trim();

        // 资源管理器「复制为路径」会带双引号，多选时还会有换行
        var newline = value.IndexOfAny(new[] { '\r', '\n' });
        if (newline > 0)
            value = value[..newline];

        value = value.Trim().Trim('"').Trim();

        if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
            value = uri.LocalPath;

        return value;
    }

    // ------------------------------------------------------------ 主题

    private void CycleTheme()
    {
        var next = AppThemeMode switch
        {
            AppThemeMode.System => AppThemeMode.Light,
            AppThemeMode.Light => AppThemeMode.Dark,
            _ => AppThemeMode.System
        };

        ApplyTheme(next, persist: true, notify: true);
    }

    public void ApplyTheme(AppThemeMode mode, bool persist, bool notify)
    {
        AppThemeMode = mode;
        ThemeService.Apply(mode);
        RefreshThemeVisual();

        if (persist)
        {
            var settings = _settings.Current.Clone();
            settings.Theme = mode;
            _settings.Save(settings);
        }

        if (notify)
            ToastRequested?.Invoke(this, $"主题已切换为「{ThemeModeName(mode)}」");
    }

    public static string ThemeModeName(AppThemeMode mode) => mode switch
    {
        AppThemeMode.Light => "浅色",
        AppThemeMode.Dark => "深色",
        _ => "跟随系统"
    };

    private void RefreshThemeVisual()
    {
        ThemeSymbol = AppThemeMode switch
        {
            AppThemeMode.Light => SymbolRegular.WeatherSunny24,
            AppThemeMode.Dark => SymbolRegular.WeatherMoon24,
            _ => SymbolRegular.Desktop24
        };

        ThemeTooltip = AppThemeMode switch
        {
            AppThemeMode.System => "当前：跟随系统（点击 → 浅色）",
            AppThemeMode.Light => "当前：浅色（点击 → 深色）",
            _ => "当前：深色（点击 → 跟随系统）"
        };
    }

    // ------------------------------------------------------------ 提取

    private void StartOrCancel()
    {
        if (IsBusy)
        {
            _cts?.Cancel();
            StatusText = "正在取消…";
            return;
        }

        RunAsync();
    }

    private async void RunAsync()
    {
        var input = (InputPath ?? string.Empty).Trim().Trim('"');
        if (input.Length == 0)
        {
            ShowInfo(InfoBarSeverity.Warning, "请先选择输入", "拖入 PKG / MPKG / TEX 文件或文件夹，也可以直接粘贴路径。");
            return;
        }

        if (!File.Exists(input) && !Directory.Exists(input))
        {
            ShowInfo(InfoBarSeverity.Error, "路径不存在", input);
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsBusy = true;
        IsInfoBarOpen = false;
        Logs.Clear();
        ProgressValue = 0;
        StatusText = "正在准备…";

        AddLog(LogEntry.Info($"输入：{input}"));

        var progress = new Progress<ExtractionProgress>(p =>
        {
            ProgressValue = p.Percent;
            if (!string.IsNullOrWhiteSpace(p.Message))
                StatusText = p.Message;
        });

        var log = new Progress<LogEntry>(AddLog);

        try
        {
            var settings = _settings.Current;
            var options = new RepkgOptions
            {
                OutputDirectory = OutputDirectory,
                NoTexConvert = settings.NoTexConvert,
                KeepRawTex = settings.KeepRawTex,
                // project.json / preview 固定复制，不再提供开关
                CopyProjectFiles = true,
                MediaOnly = settings.MediaOnly,
                FlatOutput = settings.FlatOutput,
                Overwrite = settings.Overwrite == OverwriteMode.Overwrite,
                // 递归扫描是固定行为，不提供开关
                Recursive = true,
                // 同名包固定追加 _2、_3，不提供开关
                UniquePackageFolder = true,
                SkipExtensions = ParseExtensions(settings.SkipExtensions)
            };

            var summary = await _service.ExtractAsync(input, options, progress, log, token);

            _lastOutputRoot = summary.OutputRoot;
            ProgressValue = summary.IsEmpty ? 0 : 100;

            var skippedText = BuildSkippedSummary(summary, "，");
            AddLog(LogEntry.Success(
                $"—— 完成：{summary.Packages} 个包 / {summary.Textures} 张图片 / 共 {summary.Files} 个文件" +
                $"{skippedText}，失败 {summary.Errors} 个 ——"));

            if (summary.IsEmpty)
            {
                StatusText = "没有可提取的内容";
                ShowInfo(InfoBarSeverity.Warning, "没有可提取的内容",
                    "该路径下没有找到 PKG / MPKG / TEX 文件。");
            }
            else if (summary.Errors > 0)
            {
                StatusText = "已完成（含失败项）";
                ShowInfo(InfoBarSeverity.Warning, "部分条目未能导出",
                    $"已处理 {summary.Packages} 个包，导出 {summary.Files} 个文件，失败 {summary.Errors} 个。详见日志。");
            }
            else
            {
                StatusText = "已完成";
                ShowInfo(InfoBarSeverity.Success, "已完成",
                    $"已处理 {summary.Packages} 个包，导出 {summary.Files} 个文件（其中 {summary.Textures} 张图片）" +
                    $"{BuildSkippedSummary(summary, "，")}。" +
                    $"输出目录：{summary.OutputRoot}");
                ToastRequested?.Invoke(this, "提取完成");

                if (_settings.Current.OpenOutputWhenDone)
                    OpenOutputFolder();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
            AddLog(LogEntry.Warning("用户取消了本次提取"));
            ShowInfo(InfoBarSeverity.Warning, "已取消", "本次提取已被手动取消，已写出的文件会保留。");
        }
        catch (Exception ex)
        {
            StatusText = "出错";
            AddLog(LogEntry.Error(ex.ToString()));
            ShowInfo(InfoBarSeverity.Error, "提取失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void AddLog(LogEntry entry)
    {
        Logs.Add(entry);
        while (Logs.Count > MaxLogEntries)
            Logs.RemoveAt(0);
    }

    private void ShowInfo(InfoBarSeverity severity, string title, string message)
    {
        InfoSeverity = severity;
        InfoTitle = title;
        InfoMessage = message;
        IsInfoBarOpen = true;
    }

    private void OpenOutputFolder()
    {
        var target = _lastOutputRoot;

        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
            target = (OutputDirectory ?? string.Empty).Trim().Trim('"');

        OpenPathInExplorer(target, "输出目录");
    }

    private void OpenOutputDirectory()
        => OpenPathInExplorer((OutputDirectory ?? string.Empty).Trim().Trim('"'), "输出目录");

    private void OpenPathInExplorer(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowInfo(InfoBarSeverity.Warning, $"{label}还没有设置", "先在下方输入一个路径，或点「浏览」选一个。");
            return;
        }

        if (!Directory.Exists(path))
        {
            ShowInfo(InfoBarSeverity.Warning, $"{label}还不存在", path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowInfo(InfoBarSeverity.Error, $"无法打开{label}", ex.Message);
        }
    }
}
