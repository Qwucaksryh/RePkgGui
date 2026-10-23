using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RePkgGui.Models;

namespace RePkgGui.Services;

/// <summary>配置读写：%AppData%\RePkgGui\settings.json</summary>
public sealed class SettingsService
{
    private static readonly Lazy<SettingsService> LazyInstance = new(() => new SettingsService());

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Folder = Path.Combine(appData, "RePkgGui");
        FilePath = Path.Combine(Folder, "settings.json");
    }

    public static SettingsService Instance => LazyInstance.Value;

    public string Folder { get; }
    public string FilePath { get; }

    public AppSettings Current { get; private set; } = new();

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

                if (loaded != null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.OutputDirectory))
                        loaded.OutputDirectory = AppSettings.DefaultOutputDirectory;

                    Current = loaded;
                    return Current;
                }
            }
        }
        catch
        {
            // 配置损坏就回落到默认值，不打扰用户
        }

        Current = new AppSettings();
        return Current;
    }

    public void Save(AppSettings settings)
    {
        Current = settings;

        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // 写入失败不阻塞主流程
        }
    }
}
