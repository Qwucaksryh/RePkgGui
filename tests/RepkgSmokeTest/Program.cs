using System.Text;
using RePkgGui.Models;
using RePkgGui.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RepkgSmokeTest;

/// <summary>
/// 端到端冒烟测试：现场构造 PKG / MPKG / TEX 样本（含视频贴图、DXT5、中文路径、路径穿越样本），
/// 交给真正的 RepkgService 解析，再逐项断言输出文件。
/// </summary>
internal static class Program
{
    private static int _failures;

    private static async Task<int> Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "repkg-smoke-" + Guid.NewGuid().ToString("N")[..8]);
        var input = Path.Combine(root, "input");
        var output = Path.Combine(root, "output");

        Directory.CreateDirectory(input);

        BuildFixtures(input);

        Console.WriteLine($"样本目录: {input}");
        Console.WriteLine($"输出目录: {output}");
        Console.WriteLine(new string('-', 70));

        var service = new RepkgService();
        var options = new RepkgOptions
        {
            OutputDirectory = output,
            KeepRawTex = false,
            CopyProjectFiles = true
        };

        var progressCount = 0;
        var progress = new Progress<ExtractionProgress>(_ => progressCount++);
        var log = new Progress<LogEntry>(entry =>
            Console.WriteLine($"[{entry.TimeText}] {entry.Level,-7} {entry.Text}"));

        var summary = await service.ExtractAsync(input, options, progress, log, CancellationToken.None);

        Console.WriteLine(new string('-', 70));

        // ---------- 断言 ----------
        Check("1 个 PKG + 1 个 MPKG 被识别", summary.Packages == 2, $"实际 {summary.Packages}");

        const string sceneFolder = "星空动态壁纸";
        var sceneDir = Path.Combine(output, sceneFolder);
        var dlcDir = Path.Combine(output, "dlc");

        Check("有 project.json 时按壁纸标题命名文件夹", Directory.Exists(sceneDir));
        Check("无 project.json 时回退到包文件名", Directory.Exists(dlcDir), dlcDir);

        var rgbaPng = Path.Combine(sceneDir, "materials", "tex_rgba.png");
        CheckPng(rgbaPng, 8, 8, "RGBA8888 → png（8x8）");

        var dxtPng = Path.Combine(sceneDir, "materials", "tex_dxt5.png");
        CheckPng(dxtPng, 8, 8, "DXT5 解压 → png（8x8）");

        var videoTex = Path.Combine(sceneDir, "video", "tex_video.mp4");
        Check("视频贴图导出为 .mp4", File.Exists(videoTex), videoTex);
        if (File.Exists(videoTex))
        {
            var head = File.ReadAllBytes(videoTex);
            var isMp4 = head.Length > 12 && Encoding.ASCII.GetString(head, 4, 8) == "ftypisom";
            Check("视频贴图内容为原始 mp4 字节", isMp4);
        }

        var rawMp4 = Path.Combine(sceneDir, "video", "scene.mp4");
        Check("包内 mp4 原样导出", File.Exists(rawMp4) && File.ReadAllBytes(rawMp4).Length == 64);

        var webm = Path.Combine(sceneDir, "webm", "clip.webm");
        Check("包内 webm 原样导出", File.Exists(webm) && File.ReadAllBytes(webm).Length == 48);

        var json = Path.Combine(sceneDir, "data", "config.json");
        Check("包内文本文件原样导出", File.Exists(json));

        var chinese = Path.Combine(sceneDir, "中文目录", "贴图.png");
        Check("中文条目路径正确转换", File.Exists(chinese), chinese);

        var brokenRaw = Path.Combine(sceneDir, "materials", "tex_broken.tex");
        Check("损坏 TEX 退化为原样保留 .tex", File.Exists(brokenRaw));

        Check("路径穿越条目被拒绝（未写出到上级目录）",
            !File.Exists(Path.Combine(output, "escape.txt")) &&
            !File.Exists(Path.Combine(root, "escape.txt")));

        var projectJson = Path.Combine(sceneDir, "project.json");
        Check("project.json 被复制到输出目录", File.Exists(projectJson), projectJson);

        var previewJpg = Path.Combine(sceneDir, "preview.jpg");
        Check("preview.jpg 被复制到输出目录", File.Exists(previewJpg), previewJpg);

        Check("进度回调有被触发", progressCount > 0, $"{progressCount} 次");
        Check("导出了 11 个文件", summary.Files == 11, $"实际 {summary.Files}");
        Check("图片数量为 6", summary.Textures == 6, $"实际 {summary.Textures}");
        Check("有 TEX 转换失败告警", summary.Warnings >= 1, $"实际 {summary.Warnings}");
        Check("有路径穿越被拒绝的错误", summary.Errors >= 1, $"实际 {summary.Errors}");

        // ---------- 单个 TEX ----------
        var standalone = Path.Combine(input, "standalone.tex");
        File.WriteAllBytes(standalone, BuildTex(TexKind.Rgba, 8, 8));

        var singleOutput = Path.Combine(root, "output-single");
        var single = await service.ExtractAsync(standalone,
            new RepkgOptions { OutputDirectory = singleOutput },
            null, log, CancellationToken.None);

        CheckPng(Path.Combine(singleOutput, "standalone.png"), 8, 8, "单个 TEX 直接转图片");
        Check("单文件统计正确", single.Textures == 1, $"实际 {single.Textures}");

        // ---------- 覆盖策略 ----------
        var skipExistingOutput = Path.Combine(root, "output-skipexisting");
        await service.ExtractAsync(input,
            new RepkgOptions { OutputDirectory = skipExistingOutput }, null, log, CancellationToken.None);

        var second = await service.ExtractAsync(input,
            new RepkgOptions { OutputDirectory = skipExistingOutput, Overwrite = false },
            null, log, CancellationToken.None);

        Check("不覆盖模式下第二次运行不写文件", second.Files == 0, $"files={second.Files}");
        Check("不覆盖模式下已存在跳过数正确", second.ExistingSkipped == 11, $"existing={second.ExistingSkipped}");
        Check("不覆盖模式下没有被筛选跳过", second.FilteredSkipped == 0, $"filtered={second.FilteredSkipped}");

        // 覆盖模式重跑：应当全部重写，一个不跳
        var third = await service.ExtractAsync(input,
            new RepkgOptions { OutputDirectory = skipExistingOutput, Overwrite = true },
            null, log, CancellationToken.None);

        Check("覆盖模式重跑不跳过任何文件",
            third.ExistingSkipped == 0 && third.Files == 11,
            $"files={third.Files} existing={third.ExistingSkipped}");

        // ---------- 忽略扩展名 ----------
        var skipExtOutput = Path.Combine(root, "output-skipext");
        await service.ExtractAsync(input,
            new RepkgOptions { OutputDirectory = skipExtOutput, SkipExtensions = new[] { ".json" } },
            null, log, CancellationToken.None);

        Check("忽略 .json 后不写出 config.json",
            !File.Exists(Path.Combine(skipExtOutput, sceneFolder, "data", "config.json")));
        Check("忽略扩展名时其它文件仍导出",
            File.Exists(Path.Combine(skipExtOutput, sceneFolder, "video", "scene.mp4")));

        // ---------- 不转换 TEX ----------
        var rawTexOutput = Path.Combine(root, "output-rawtex");
        var rawRun = await service.ExtractAsync(input,
            new RepkgOptions { OutputDirectory = rawTexOutput, NoTexConvert = true },
            null, log, CancellationToken.None);

        Check("不转换 TEX 时输出原始 .tex",
            File.Exists(Path.Combine(rawTexOutput, sceneFolder, "materials", "tex_rgba.tex")));
        Check("不转换 TEX 时不再生成 png",
            !File.Exists(Path.Combine(rawTexOutput, sceneFolder, "materials", "tex_rgba.png")));
        Check("不转换 TEX 时视频贴图保持 .tex",
            File.Exists(Path.Combine(rawTexOutput, sceneFolder, "video", "tex_video.tex")));
        Check("不转换 TEX 时图片计数为 0", rawRun.Textures == 0, $"实际 {rawRun.Textures}");

        // ---------- 忽略目录结构 ----------
        var flatOutput = Path.Combine(root, "output-flat");
        await service.ExtractAsync(input,
            new RepkgOptions { OutputDirectory = flatOutput, FlatOutput = true },
            null, log, CancellationToken.None);

        Check("扁平模式下文件在包目录根", File.Exists(Path.Combine(flatOutput, sceneFolder, "tex_rgba.png")));
        Check("扁平模式下不再创建子目录", !Directory.Exists(Path.Combine(flatOutput, sceneFolder, "materials")));
        Check("扁平模式下同名文件自动加序号", File.Exists(Path.Combine(flatOutput, sceneFolder, "tex_rgba_2.png")));

        // ---------- 仅保留图片和视频 ----------
        var mediaOutput = Path.Combine(root, "output-media");
        var mediaRun = await service.ExtractAsync(input,
            new RepkgOptions { OutputDirectory = mediaOutput, MediaOnly = true },
            null, log, CancellationToken.None);

        Check("仅媒体：保留 png", File.Exists(Path.Combine(mediaOutput, sceneFolder, "materials", "tex_rgba.png")));
        Check("仅媒体：保留包内 mp4", File.Exists(Path.Combine(mediaOutput, sceneFolder, "video", "scene.mp4")));
        Check("仅媒体：保留包内 webm", File.Exists(Path.Combine(mediaOutput, sceneFolder, "webm", "clip.webm")));
        Check("仅媒体：保留视频贴图 mp4", File.Exists(Path.Combine(mediaOutput, sceneFolder, "video", "tex_video.mp4")));
        Check("仅媒体：跳过 json", !File.Exists(Path.Combine(mediaOutput, sceneFolder, "data", "config.json")));
        Check("仅媒体：跳过转换失败的 tex", !File.Exists(Path.Combine(mediaOutput, sceneFolder, "materials", "tex_broken.tex")));
        Check("仅媒体：图片计数不变", mediaRun.Textures == 6, $"实际 {mediaRun.Textures}");

        // ---------- 命名：title 为空时回退 ----------
        var emptyTitleRoot = Path.Combine(root, "emptytitle");
        Directory.CreateDirectory(emptyTitleRoot);
        File.WriteAllText(Path.Combine(emptyTitleRoot, "project.json"), "{\"title\":\"\",\"type\":\"scene\"}");
        File.Copy(Path.Combine(input, "scene.pkg"), Path.Combine(emptyTitleRoot, "3132017530.pkg"));

        var emptyTitleOutput = Path.Combine(root, "output-emptytitle");
        await service.ExtractAsync(Path.Combine(emptyTitleRoot, "3132017530.pkg"),
            new RepkgOptions { OutputDirectory = emptyTitleOutput },
            null, log, CancellationToken.None);

        Check("title 为空时回退到包文件名",
            Directory.Exists(Path.Combine(emptyTitleOutput, "3132017530")));

        Console.WriteLine(new string('-', 70));

        if (_failures == 0)
        {
            Console.WriteLine("全部断言通过 ✔");
            return 0;
        }

        Console.WriteLine($"{_failures} 项断言失败 ✘");
        return 1;
    }

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            Console.WriteLine($"  PASS  {name}");
            return;
        }

        _failures++;
        Console.WriteLine($"  FAIL  {name}{(detail == null ? "" : "  → " + detail)}");
    }

    private static void CheckPng(string path, int width, int height, string name)
    {
        if (!File.Exists(path))
        {
            Check(name, false, "文件不存在: " + path);
            return;
        }

        try
        {
            using var image = Image.Load<Rgba32>(path);
            Check(name, image.Width == width && image.Height == height,
                $"实际 {image.Width}x{image.Height}");
        }
        catch (Exception ex)
        {
            Check(name, false, "无法解码 png: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- 样本

    private static void BuildFixtures(string input)
    {
        var scene = BuildPackage("PKGV0001", new[]
        {
            ("materials/tex_rgba.tex", BuildTex(TexKind.Rgba, 8, 8)),
            ("materials/tex_dxt5.tex", BuildTex(TexKind.Dxt5, 8, 8)),
            ("backup/tex_rgba.tex", BuildTex(TexKind.Rgba, 8, 8)),
            ("materials/tex_broken.tex", Encoding.ASCII.GetBytes("TEXV0005TEXI0001garbage")),
            ("video/tex_video.tex", BuildTex(TexKind.VideoMp4, 64, 64)),
            ("video/scene.mp4", BuildBlob(64, 0x11)),
            ("webm/clip.webm", BuildBlob(48, 0x22)),
            ("data/config.json", Encoding.UTF8.GetBytes("{\"camera\":{\"eye\":[0,0,1]}}")),
            ("中文目录/贴图.tex", BuildTex(TexKind.Rgba, 8, 8)),
            ("../escape.txt", Encoding.UTF8.GetBytes("should not escape"))
        });

        File.WriteAllBytes(Path.Combine(input, "scene.pkg"), scene);

        var dlc = BuildPackage("PKGV0001", new[]
        {
            ("materials/tex_a.tex", BuildTex(TexKind.Rgba, 8, 8)),
            ("materials/tex_b.tex", BuildTex(TexKind.Rgba, 8, 8))
        });

        var sub = Path.Combine(input, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, "dlc.mpkg"), dlc);

        // PKG 旁边的附属文件
        File.WriteAllText(Path.Combine(input, "project.json"),
            "{\"type\":\"scene\",\"title\":\"星空动态壁纸\",\"file\":\"scene.pkg\",\"tags\":[\"测试\"]}");
        File.WriteAllBytes(Path.Combine(input, "preview.jpg"), BuildBlob(32, 0x33));
    }

    private enum TexKind
    {
        Rgba,
        Dxt5,
        VideoMp4
    }

    private static byte[] BuildTex(TexKind kind, int width, int height)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8);

        WriteNString(writer, "TEXV0005");
        WriteNString(writer, "TEXI0001");

        var format = kind switch
        {
            TexKind.Rgba => 0,          // TexFormat.RGBA8888
            TexKind.Dxt5 => 4,          // TexFormat.DXT5
            _ => 0
        };

        var flags = kind == TexKind.VideoMp4 ? 32 : 0;   // TexFlags.IsVideoTexture

        writer.Write(format);
        writer.Write(flags);
        writer.Write(width);       // texture width
        writer.Write(height);      // texture height
        writer.Write(width);       // image width
        writer.Write(height);      // image height
        writer.Write(0u);          // unknown

        if (kind == TexKind.VideoMp4)
        {
            WriteNString(writer, "TEXB0004");
            writer.Write(1);        // image count
            writer.Write(-1);       // FreeImageFormat.FIF_UNKNOWN
            writer.Write(1);        // isVideoMp4

            writer.Write(1);        // mipmap count
            writer.Write(1);        // param1
            writer.Write(2);        // param2
            WriteNString(writer, "{}"); // condition json
            writer.Write(1);        // param3
            writer.Write(width);
            writer.Write(height);
            writer.Write(0);        // not lz4
            writer.Write(0);        // decompressed count

            var mp4 = BuildFakeMp4();
            writer.Write(mp4.Length);
            writer.Write(mp4);
        }
        else
        {
            WriteNString(writer, "TEXB0001");
            writer.Write(1);        // image count
            writer.Write(1);        // mipmap count
            writer.Write(width);
            writer.Write(height);

            var pixels = kind == TexKind.Dxt5
                ? BuildBlob(width / 4 * (height / 4) * 16, 0x7F)   // DXT5: 16 bytes per 4x4 block
                : BuildRgbaPixels(width, height);

            writer.Write(pixels.Length);
            writer.Write(pixels);
        }

        writer.Flush();
        return memory.ToArray();
    }

    private static byte[] BuildRgbaPixels(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                bytes[i] = (byte)(x * 32);          // R
                bytes[i + 1] = (byte)(y * 32);      // G
                bytes[i + 2] = 200;                 // B
                bytes[i + 3] = 255;                 // A
            }
        }

        return bytes;
    }

    private static byte[] BuildFakeMp4()
    {
        var bytes = new byte[64];
        // [size=32][ftypisom]...
        bytes[3] = 32;
        Encoding.ASCII.GetBytes("ftypisom").CopyTo(bytes, 4);
        return bytes;
    }

    private static byte[] BuildBlob(int length, byte value)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, value);
        return bytes;
    }

    private static byte[] BuildPackage(string magic, (string Path, byte[] Data)[] entries)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8);

        WriteString(writer, magic);
        writer.Write(entries.Length);

        var offset = 0;
        foreach (var (path, data) in entries)
        {
            WriteString(writer, path);
            writer.Write(offset);
            writer.Write(data.Length);
            offset += data.Length;
        }

        foreach (var (_, data) in entries)
            writer.Write(data);

        writer.Flush();
        return memory.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteNString(BinaryWriter writer, string value)
    {
        foreach (var ch in value)
            writer.Write((byte)ch);

        writer.Write((byte)0);
    }
}
