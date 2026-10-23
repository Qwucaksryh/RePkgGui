using System.Diagnostics;
using System.IO;
using System.Text;
using RePKG.Application.Package;
using RePKG.Application.Texture;
using RePKG.Core.Package;
using RePKG.Core.Texture;
using RePkgGui.Models;

namespace RePkgGui.Services;

/// <summary>
/// 对 RePKG.Core / RePKG.Application 的封装。
/// 只做「编排」——PKG 头解析、条目定位、TEX 解码全部交给 RePKG 本身，
/// 额外提供逐条目的进度回调、路径穿越防护与视频原样导出。
/// </summary>
public sealed class RepkgService
{
    private static readonly string[] PackageExtensions = { ".pkg", ".mpkg" };

    /// <summary>“仅保留图片与视频”模式下保留的媒体扩展名。</summary>
    private static readonly string[] MediaExtensions =
    {
        // 图片
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico",
        // 视频
        ".mp4", ".webm", ".avi", ".mov", ".mkv", ".wmv", ".m4v"
    };

    private static readonly string[] VideoExtensions =
        { ".mp4", ".webm", ".avi", ".mov", ".mkv", ".wmv", ".m4v" };

    /// <summary>TEX 与图像以外的「原样导出」文件不逐条写日志，避免几百行刷屏。</summary>
    private readonly TexToImageConverter _texConverter = new();

    public Task<ExtractionSummary> ExtractAsync(
        string inputPath,
        RepkgOptions options,
        IProgress<ExtractionProgress>? progress,
        IProgress<LogEntry>? log,
        CancellationToken token)
    {
        return Task.Run(() => Extract(inputPath, options, progress, log, token), token);
    }

    private ExtractionSummary Extract(
        string inputPath,
        RepkgOptions options,
        IProgress<ExtractionProgress>? progress,
        IProgress<LogEntry>? log,
        CancellationToken token)
    {
        var ctx = new RunContext(options, progress, log, token);

        var input = (inputPath ?? string.Empty).Trim().Trim('"');
        if (input.Length == 0)
        {
            ctx.Error("请先选择要提取的文件或文件夹。");
            return ctx.Summary;
        }

        if (!File.Exists(input) && !Directory.Exists(input))
        {
            ctx.Error($"路径不存在：{input}");
            return ctx.Summary;
        }

        string outputRoot;
        try
        {
            outputRoot = string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? AppSettings.DefaultOutputDirectory
                : options.OutputDirectory.Trim().Trim('"');
            outputRoot = Path.GetFullPath(outputRoot);
            Directory.CreateDirectory(outputRoot);
        }
        catch (Exception ex)
        {
            ctx.Error($"无法创建输出目录：{ex.Message}");
            return ctx.Summary;
        }

        ctx.Summary.OutputRoot = outputRoot;
        ctx.Info($"输出目录：{outputRoot}");

        var plan = BuildPlan(input, outputRoot, ctx);
        if (plan.Count == 0)
        {
            if (ctx.Summary.Errors == 0 && ctx.Summary.Warnings == 0)
                ctx.Warn("没有找到可处理的 PKG / MPKG / TEX 文件。");
            return ctx.Summary;
        }

        ctx.Count = plan.Count;
        ctx.TotalWeight = plan.Sum(x => Math.Max(x.Size, 1));
        ctx.Report("准备中…", force: true);

        var usedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < plan.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var item = plan[i];
            ctx.Index = i + 1;

            if (item.Kind == WorkItemKind.Package)
            {
                var weight = ExtractPackage(item, outputRoot, usedFolderNames, ctx);
                ctx.Advance(weight);
            }
            else
            {
                ExtractTextureFile(item, outputRoot, ctx);
                ctx.Advance(Math.Max(item.Size, 1));
            }
        }

        ctx.Report("完成", force: true);
        return ctx.Summary;
    }

    // ---------------------------------------------------------------- 计划

    private enum WorkItemKind
    {
        Package,
        Texture
    }

    private readonly record struct WorkItem(string Path, long Size, WorkItemKind Kind);

    private static List<WorkItem> BuildPlan(string input, string outputRoot, RunContext ctx)
    {
        if (File.Exists(input))
        {
            var kind = GetKind(input);
            if (kind == null)
            {
                ctx.Error($"不支持的文件类型：{System.IO.Path.GetExtension(input)}（仅支持 .pkg / .mpkg / .tex）");
                return new List<WorkItem>();
            }

            return new List<WorkItem> { new(input, GetSize(input), kind.Value) };
        }

        var packages = Scan(input, outputRoot, PackageExtensions, ctx.Options.Recursive);
        if (packages.Count > 0)
            return packages;

        // 目录里没有包，退化为转换目录内的 .tex
        var textures = Scan(input, outputRoot, new[] { ".tex" }, ctx.Options.Recursive);
        if (textures.Count > 0)
            ctx.Warn("目录内未找到 PKG / MPKG，改为转换其中的 .tex 文件。");

        return textures;
    }

    private static WorkItemKind? GetKind(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        if (PackageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return WorkItemKind.Package;
        if (ext.Equals(".tex", StringComparison.OrdinalIgnoreCase))
            return WorkItemKind.Texture;
        return null;
    }

    private static long GetSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 递归扫描目标扩展名。带深度与数量上限，并跳过符号链接/联接点，
    /// 避免用户误选 C:\ 之类的大目录时卡死。
    /// </summary>
    /// <param name="skipDirectory">需要跳过的子树（通常是输出目录），可为 null。</param>
    private static List<WorkItem> Scan(string root, string? skipDirectory, string[] extensions,
        bool recursive, int maxFiles = 5000, int maxDepth = 12)
    {
        var separator = System.IO.Path.DirectorySeparatorChar;
        var result = new List<WorkItem>();
        var pending = new Stack<(string Dir, int Depth)>();
        pending.Push((root, 0));

        string? skipPrefix = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(skipDirectory))
                skipPrefix = System.IO.Path.GetFullPath(skipDirectory).TrimEnd(separator) + separator;
        }
        catch
        {
            skipPrefix = null;
        }

        while (pending.Count > 0 && result.Count < maxFiles)
        {
            var (dir, depth) = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var extension = System.IO.Path.GetExtension(file);
                if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    continue;

                var kind = extension.Equals(".tex", StringComparison.OrdinalIgnoreCase)
                    ? WorkItemKind.Texture
                    : WorkItemKind.Package;

                result.Add(new WorkItem(file, GetSize(file), kind));
                if (result.Count >= maxFiles) break;
            }

            if (!recursive || depth >= maxDepth) continue;

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                try
                {
                    var attr = File.GetAttributes(sub);
                    if (attr.HasFlag(FileAttributes.ReparsePoint)) continue; // 跳过 junction / symlink
                }
                catch
                {
                    continue;
                }

                // 避免把上一次的输出目录再扫进来
                if (skipPrefix != null &&
                    (sub + separator).StartsWith(skipPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                pending.Push((sub, depth + 1));
            }
        }

        result.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>
    /// 统计目录内的 PKG/MPKG 数量（找不到包时再统计 .tex），
    /// 用于主界面展示与执行前预检。有上限，不会因目录很大而卡死。
    /// </summary>
    public static (int Packages, int Textures) PreviewDirectory(string directory)
    {
        try
        {
            var packages = Scan(directory, null, PackageExtensions, true, maxFiles: 2000, maxDepth: 8).Count;
            if (packages > 0)
                return (packages, 0);

            var textures = Scan(directory, null, new[] { ".tex" }, true, maxFiles: 2000, maxDepth: 8).Count;
            return (0, textures);
        }
        catch
        {
            return (0, 0);
        }
    }

    // ---------------------------------------------------------------- PKG

    /// <summary>返回该包的「权重」（文件字节数），用于进度换算。</summary>
    private long ExtractPackage(WorkItem item, string outputRoot, HashSet<string> usedNames, RunContext ctx)
    {
        var weight = Math.Max(item.Size, 1);
        var fileName = System.IO.Path.GetFileName(item.Path);
        var baseFolderName = SanitizeName(System.IO.Path.GetFileNameWithoutExtension(item.Path));
        var folderName = ctx.Options.UniquePackageFolder
            ? MakeUniqueName(baseFolderName, usedNames)
            : baseFolderName;
        var destination = System.IO.Path.Combine(outputRoot, folderName);

        ctx.Info($"▶ 解析 {fileName}");
        ctx.Report($"{fileName}", force: true);

        try
        {
            Directory.CreateDirectory(destination);

            using var stream = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            // 只解析文件头，条目内容按需 seek 读取 —— 大包也不会把整个文件读进内存
            var package = new PackageReader { ReadEntryBytes = false }.ReadFrom(reader);
            var dataStart = package.HeaderSize;

            if (package.Entries.Count == 0)
            {
                ctx.Warn($"   {fileName} 中没有条目（可能不是有效的 PKG）。");
                return weight;
            }

            ctx.Info($"   共 {package.Entries.Count} 个条目，数据区起始 0x{dataStart:X}");

            var otherFiles = 0;
            var texOk = 0;
            var texFail = 0;
            var videos = 0;
            var existingSkipped = 0;
            var filteredSkipped = 0;

            // 扁平输出时用来避免同名文件互相覆盖
            var flatNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in package.Entries)
            {
                ctx.Token.ThrowIfCancellationRequested();

                try
                {
                    var offset = dataStart + (long)entry.Offset;
                    if (entry.Length <= 0 || offset < 0 || offset + entry.Length > stream.Length)
                    {
                        ctx.Error($"   × 条目越界，已跳过：{entry.FullPath}");
                        continue;
                    }

                    stream.Position = offset;
                    var bytes = reader.ReadBytes(entry.Length);
                    if (bytes.Length != entry.Length)
                    {
                        ctx.Error($"   × 条目数据不完整，已跳过：{entry.FullPath}");
                        continue;
                    }

                    ctx.Report($"{fileName} → {entry.FullPath}");

                    var outcome = WriteEntry(entry, bytes, destination, ctx.Options, flatNames,
                        out var note, out var written);
                    ctx.Summary.Files += written;

                    switch (outcome)
                    {
                        case EntryOutcome.Texture:
                            texOk++;
                            if (note.Length > 0) ctx.Info(note);
                            break;
                        case EntryOutcome.TextureFailed:
                            texFail++;
                            break;
                        case EntryOutcome.Video:
                            videos++;
                            if (note.Length > 0) ctx.Info(note);
                            break;
                        case EntryOutcome.SkippedExisting:
                            existingSkipped++;
                            break;
                        case EntryOutcome.SkippedFiltered:
                            filteredSkipped++;
                            break;
                        default:
                            otherFiles++;
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ctx.Error($"   × {entry.FullPath}：{ex.Message}");
                }
            }

            if (otherFiles > 0)
                ctx.Info($"   已原样导出 {otherFiles} 个其他文件");
            if (videos > 0)
                ctx.Info($"   已导出 {videos} 个视频文件");
            if (texOk > 0)
            {
                ctx.Summary.Textures += texOk;
                ctx.Ok($"   已转换 {texOk} 个 TEX 贴图");
            }

            if (texFail > 0)
                ctx.Warn($"   {texFail} 个 TEX 转换失败，已保留原始文件");

            if (existingSkipped > 0)
            {
                ctx.Summary.ExistingSkipped += existingSkipped;
                ctx.Info($"   已存在而跳过 {existingSkipped} 个（当前是“跳过”策略）");
            }

            if (filteredSkipped > 0)
            {
                ctx.Summary.FilteredSkipped += filteredSkipped;
                ctx.Info($"   被筛选规则忽略 {filteredSkipped} 个（忽略扩展名 / 仅保留图片和视频）");
            }

            ctx.Ok($"✔ {fileName} 完成 → {folderName}\\");
            ctx.Summary.Packages++;

            CopyProjectFiles(item.Path, destination, ctx);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ctx.Error($"解析 {fileName} 失败：{ex.Message}");
        }

        return weight;
    }

    private enum EntryOutcome
    {
        Other,
        Texture,
        TextureFailed,
        Video,
        SkippedExisting,
        SkippedFiltered
    }

    private EntryOutcome WriteEntry(PackageEntry entry, byte[] bytes, string destination,
        RepkgOptions options, HashSet<string> flatNames, out string note, out int filesWritten)
    {
        note = string.Empty;
        filesWritten = 0;

        var relative = (entry.FullPath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0)
            return EntryOutcome.Other;

        var native = relative.Replace('/', System.IO.Path.DirectorySeparatorChar);
        var directory = System.IO.Path.GetDirectoryName(native) ?? string.Empty;
        var name = System.IO.Path.GetFileNameWithoutExtension(native);
        var extension = System.IO.Path.GetExtension(native);

        if (name.Length == 0)
            return EntryOutcome.Other;

        var targetDir = options.FlatOutput ? destination : SafeCombine(destination, directory);

        // 用户在设置里指定的忽略扩展名
        if (options.SkipExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return EntryOutcome.SkippedFiltered;

        // “仅保留图片和视频”：非媒体条目直接丢弃。
        // .tex 例外——它是容器，要等转换出 png/gif/mp4 后按产物再判断。
        var isTex = extension.Equals(".tex", StringComparison.OrdinalIgnoreCase);
        if (options.MediaOnly && !isTex && !IsMediaExtension(extension))
            return EntryOutcome.SkippedFiltered;

        Directory.CreateDirectory(targetDir);

        if (extension.Equals(".tex", StringComparison.OrdinalIgnoreCase))
        {
            // 用户选了“不转换 TEX”：只解包，不做图片转换
            if (options.NoTexConvert)
            {
                if (options.MediaOnly)
                    return EntryOutcome.SkippedFiltered;

                var rawTexPath = SafeCombine(targetDir, ReserveName(options, flatNames, name + ".tex"));
                if (!options.Overwrite && File.Exists(rawTexPath))
                    return EntryOutcome.SkippedExisting;

                File.WriteAllBytes(rawTexPath, bytes);
                filesWritten = 1;
                note = $"   ↷ {relative}（原样导出，未转换）";
                return EntryOutcome.Other;
            }

            if (TryConvertTexture(bytes, out var imageExtension, out var imageBytes, out var reason))
            {
                if (options.MediaOnly && !IsMediaExtension("." + imageExtension))
                    return EntryOutcome.SkippedFiltered;

                var fileName = ReserveName(options, flatNames, name + "." + imageExtension);
                var imagePath = SafeCombine(targetDir, fileName);

                if (!options.Overwrite && File.Exists(imagePath))
                    return EntryOutcome.SkippedExisting;

                File.WriteAllBytes(imagePath, imageBytes);
                filesWritten = 1;

                if (options.KeepRawTex && !options.MediaOnly)
                {
                    var rawPath = SafeCombine(targetDir, ReserveName(options, flatNames, name + ".tex"));
                    if (options.Overwrite || !File.Exists(rawPath))
                    {
                        File.WriteAllBytes(rawPath, bytes);
                        filesWritten = 2;
                    }
                }

                note = imageExtension.Equals("mp4", StringComparison.OrdinalIgnoreCase)
                    ? $"   ▶ {relative}  →  {name}.mp4（视频贴图）"
                    : $"   ✓ {relative}  →  {name}.{imageExtension}";

                return imageExtension.Equals("mp4", StringComparison.OrdinalIgnoreCase)
                    ? EntryOutcome.Video
                    : EntryOutcome.Texture;
            }

            // 转换不出来就退化为原样导出，保证内容不丢
            if (options.MediaOnly)
                return EntryOutcome.SkippedFiltered;

            var fallbackPath = SafeCombine(targetDir, ReserveName(options, flatNames, name + ".tex"));
            if (!options.Overwrite && File.Exists(fallbackPath))
                return EntryOutcome.SkippedExisting;

            File.WriteAllBytes(fallbackPath, bytes);
            filesWritten = 1;
            note = $"   ! {relative} 转换失败，已保留原文件：{reason}";
            return EntryOutcome.TextureFailed;
        }

        var outputPath = SafeCombine(targetDir, ReserveName(options, flatNames, name + extension));
        if (!options.Overwrite && File.Exists(outputPath))
            return EntryOutcome.SkippedExisting;

        File.WriteAllBytes(outputPath, bytes);
        filesWritten = 1;

        if (VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            note = $"   ▶ {relative}（视频，原样导出）";
            return EntryOutcome.Video;
        }

        return EntryOutcome.Other;
    }

    // ---------------------------------------------------------------- TEX（单文件）
    private void ExtractTextureFile(WorkItem item, string outputRoot, RunContext ctx)
    {
        var fileName = System.IO.Path.GetFileName(item.Path);
        ctx.Report(fileName, force: true);

        try
        {
            var bytes = File.ReadAllBytes(item.Path);
            var name = System.IO.Path.GetFileNameWithoutExtension(item.Path);

            if (TryConvertTexture(bytes, out var extension, out var imageBytes, out var reason))
            {
                var outputPath = SafeCombine(outputRoot, name + "." + extension);

                if (!ctx.Options.Overwrite && File.Exists(outputPath))
                {
                    ctx.Summary.ExistingSkipped++;
                    ctx.Info($"↷ 已跳过（已存在）：{System.IO.Path.GetFileName(outputPath)}");
                    return;
                }

                File.WriteAllBytes(outputPath, imageBytes);
                ctx.Summary.Textures++;
                ctx.Summary.Files++;
                ctx.Ok($"✔ {fileName}  →  {System.IO.Path.GetFileName(outputPath)}");
            }
            else
            {
                ctx.Error($"✖ {fileName} 转换失败：{reason}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ctx.Error($"✖ {fileName}：{ex.Message}");
        }
    }

    /// <summary>调用 RePKG 解码 TEX；视频贴图会返回 mp4 字节。</summary>
    private bool TryConvertTexture(byte[] bytes, out string extension, out byte[] imageBytes, out string reason)
    {
        extension = "png";
        imageBytes = Array.Empty<byte>();
        reason = string.Empty;

        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(memory, Encoding.UTF8);

            var tex = TexReader.Default.ReadFrom(reader);
            var image = _texConverter.ConvertToImage(tex);

            extension = image.Format.GetFileExtension();
            imageBytes = image.Bytes;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    // ---------------------------------------------------------------- 附属文件

    private static void CopyProjectFiles(string packagePath, string destination, RunContext ctx)
    {
        if (!ctx.Options.CopyProjectFiles)
            return;

        try
        {
            var directory = System.IO.Path.GetDirectoryName(packagePath);
            if (string.IsNullOrEmpty(directory))
                return;

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var fileName = System.IO.Path.GetFileName(file);
                var isProject = fileName.Equals("project.json", StringComparison.OrdinalIgnoreCase);
                var isPreview = fileName.StartsWith("preview.", StringComparison.OrdinalIgnoreCase);

                if (!isProject && !isPreview)
                    continue;

                var target = System.IO.Path.Combine(destination, fileName);
                if (!ctx.Options.Overwrite && File.Exists(target))
                    continue;

                File.Copy(file, target, overwrite: true);
            }
        }
        catch
        {
            // 附属文件属于锦上添花，失败不影响解包结果
        }
    }

    private static bool IsMediaExtension(string extension)
    {
        return MediaExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>扁平输出时给重名文件让位：a.png → a_2.png；非扁平模式原样返回。</summary>
    private static string ReserveName(RepkgOptions options, HashSet<string> used, string candidate)
    {
        if (!options.FlatOutput)
            return candidate;

        if (used.Add(candidate))
            return candidate;

        var stem = System.IO.Path.GetFileNameWithoutExtension(candidate);
        var extension = System.IO.Path.GetExtension(candidate);

        for (var index = 2; index < 10_000; index++)
        {
            var next = $"{stem}_{index}{extension}";
            if (used.Add(next))
                return next;
        }

        return candidate;
    }

    // ---------------------------------------------------------------- 工具

    private static string SafeCombine(string root, string relative)
    {
        var rootFull = System.IO.Path.GetFullPath(root);

        if (System.IO.Path.IsPathRooted(relative))
            throw new IOException("条目使用了绝对路径，已拒绝写入");

        var combined = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootFull, relative));
        var prefix = rootFull.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + System.IO.Path.DirectorySeparatorChar;

        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, rootFull, StringComparison.OrdinalIgnoreCase))
            throw new IOException("条目路径越出输出目录，已拒绝写入");

        return combined;
    }

    private static string SanitizeName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
            builder.Append(invalid.Contains(ch) ? '_' : ch);

        var result = builder.ToString().Trim().TrimEnd('.');

        // 标题可能很长，留给完整路径一些余量
        if (result.Length > 80)
            result = result[..80].Trim().TrimEnd('.');

        return result.Length == 0 ? "output" : result;
    }

    private static string MakeUniqueName(string name, HashSet<string> used)
    {
        if (used.Add(name))
            return name;

        for (var i = 2; i < 10_000; i++)
        {
            var candidate = $"{name}_{i}";
            if (used.Add(candidate))
                return candidate;
        }

        return name;
    }

    // ---------------------------------------------------------------- 运行上下文

    private sealed class RunContext
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _lastReport;

        public RunContext(RepkgOptions options, IProgress<ExtractionProgress>? progress,
            IProgress<LogEntry>? log, CancellationToken token)
        {
            Options = options;
            Progress = progress;
            LogSink = log;
            Token = token;
        }

        public RepkgOptions Options { get; }
        public ExtractionSummary Summary { get; } = new();
        public IProgress<ExtractionProgress>? Progress { get; }
        public IProgress<LogEntry>? LogSink { get; }
        public CancellationToken Token { get; }

        public long TotalWeight { get; set; }
        public long DoneWeight { get; private set; }
        public int Index { get; set; }
        public int Count { get; set; }

        public void Advance(long weight)
        {
            DoneWeight += Math.Max(weight, 0);
            Report(string.Empty, force: true);
        }

        public void Report(string message, bool force = false)
        {
            if (Progress == null)
                return;

            var now = _watch.ElapsedMilliseconds;
            if (!force && now - _lastReport < 50)
                return;

            _lastReport = now;
            var percent = TotalWeight <= 0 ? 0 : Math.Clamp(DoneWeight * 100d / TotalWeight, 0, 100);
            Progress.Report(new ExtractionProgress(percent, message));
        }

        public void Info(string message) => LogSink?.Report(LogEntry.Info(message));

        public void Ok(string message) => LogSink?.Report(LogEntry.Success(message));

        public void Warn(string message)
        {
            Summary.Warnings++;
            LogSink?.Report(LogEntry.Warning(message));
        }

        public void Error(string message)
        {
            Summary.Errors++;
            LogSink?.Report(LogEntry.Error(message));
        }
    }
}
