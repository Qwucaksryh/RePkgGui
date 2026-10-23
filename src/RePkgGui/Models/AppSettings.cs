namespace RePkgGui.Models;

/// <summary>主题模式：跟随系统 / 浅色 / 深色。</summary>
public enum AppThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>已存在文件的处理策略。</summary>
public enum OverwriteMode
{
    Overwrite = 0,
    Skip = 1
}

/// <summary>持久化到 %AppData%\RePkgGui\settings.json 的配置。</summary>
public sealed class AppSettings
{
    // ---------------- 外观 ----------------

    /// <summary>主题模式。</summary>
    public AppThemeMode Theme { get; set; } = AppThemeMode.System;

    // ---------------- 输出 ----------------

    /// <summary>默认输出目录，默认桌面。</summary>
    public string OutputDirectory { get; set; } = DefaultOutputDirectory;

    /// <summary>提取完成后是否自动打开输出目录。</summary>
    public bool OpenOutputWhenDone { get; set; }

    /// <summary>同名文件已存在时的处理方式。</summary>
    public OverwriteMode Overwrite { get; set; } = OverwriteMode.Overwrite;

    // ---------------- 提取内容 ----------------

    /// <summary>不转换 TEX，直接把 .tex 原样导出（只解包不转图）。</summary>
    public bool NoTexConvert { get; set; }

    /// <summary>转换图片后是否额外保留原始 .tex 文件。</summary>
    public bool KeepRawTex { get; set; }

    /// <summary>仅保留图片与视频（png/jpg/gif/mp4/webm…），其余不导出。</summary>
    public bool MediaOnly { get; set; }

    /// <summary>不导出的扩展名，逗号分隔，例如 ".json, bin"。</summary>
    public string SkipExtensions { get; set; } = string.Empty;

    // ---------------- 处理 ----------------

    /// <summary>忽略包内目录结构，所有文件直接放在包对应的输出目录根下（重名自动加序号）。</summary>
    public bool FlatOutput { get; set; }

    public static string DefaultOutputDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    public AppSettings Clone() => new()
    {
        Theme = Theme,
        OutputDirectory = OutputDirectory,
        OpenOutputWhenDone = OpenOutputWhenDone,
        Overwrite = Overwrite,
        NoTexConvert = NoTexConvert,
        KeepRawTex = KeepRawTex,
        MediaOnly = MediaOnly,
        SkipExtensions = SkipExtensions,
        FlatOutput = FlatOutput
    };
}
