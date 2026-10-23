namespace RePkgGui.Models;

/// <summary>一次提取任务的可选项。</summary>
public sealed class RepkgOptions
{
    public string OutputDirectory { get; set; } = AppSettings.DefaultOutputDirectory;

    /// <summary>不转换 TEX，把 .tex 原样导出。</summary>
    public bool NoTexConvert { get; set; }

    /// <summary>转换后是否保留原始 .tex。</summary>
    public bool KeepRawTex { get; set; }

    /// <summary>是否复制 PKG 旁的 project.json / preview.*。</summary>
    public bool CopyProjectFiles { get; set; } = true;

    /// <summary>目标文件已存在时是否覆盖；false 则跳过。</summary>
    public bool Overwrite { get; set; } = true;

    /// <summary>输入为文件夹时是否递归子目录。</summary>
    public bool Recursive { get; set; } = true;

    /// <summary>要跳过的扩展名（已规范化，带前导点，小写）。</summary>
    public IReadOnlyList<string> SkipExtensions { get; set; } = Array.Empty<string>();

    /// <summary>同名包是否拆到独立子文件夹（false 则合并）。</summary>
    public bool UniquePackageFolder { get; set; } = true;

    /// <summary>仅保留图片与视频（忽略 json / bin / 字体等非媒体文件）。</summary>
    public bool MediaOnly { get; set; }

    /// <summary>忽略包内目录结构，平铺输出。</summary>
    public bool FlatOutput { get; set; }
}

/// <summary>执行进度快照。</summary>
public sealed class ExtractionProgress
{
    public ExtractionProgress(double percent, string message)
    {
        Percent = percent;
        Message = message;
    }

    /// <summary>0 - 100。</summary>
    public double Percent { get; }

    /// <summary>正在处理的内容描述。</summary>
    public string Message { get; }
}

/// <summary>一次提取任务的结果统计。</summary>
public sealed class ExtractionSummary
{
    public string OutputRoot { get; set; } = string.Empty;
    public int Packages { get; set; }
    public int Files { get; set; }
    public int Textures { get; set; }
    /// <summary>因文件已存在且选择了“跳过”而没写的数量。</summary>
    public int ExistingSkipped { get; set; }

    /// <summary>被筛选规则（忽略扩展名 / 仅媒体 / 不转换 TEX）过滤掉的数量。</summary>
    public int FilteredSkipped { get; set; }

    public int Skipped => ExistingSkipped + FilteredSkipped;
    public int Warnings { get; set; }
    public int Errors { get; set; }

    public bool IsEmpty => Packages == 0 && Files == 0 && Textures == 0;
}
