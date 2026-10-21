using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

// 补丁侧共享配置：pcl-ce-mod.json
// 启动器负责 cePath / ceSearchDirectories，补丁负责 features 与 personalization。
// 两边都按“读-改-写整个 JSON”处理，互不丢弃对方字段。

internal sealed class PersonalizationSettings
{
    // 运行期标记，是否真正启用由 ModFeatures.Personalization 决定
    internal bool Enabled = true;
    internal string ThemeKey = "龙猫蓝";
    internal string PresetName = "龙猫蓝";
    internal int Hue = 255;
    internal double LightAdjust;
    internal double ChromaAdjust = -0.2;
}

internal sealed class ModFeatures
{
    internal bool Personalization = true;
    internal bool RegionBypass = true;
}

internal sealed class ModConfig
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    internal static ModConfig Current { get; private set; } = new();

    internal string ConfigPath = string.Empty;
    internal string ModDirectory = string.Empty;
    internal ModFeatures Features = new();
    internal PersonalizationSettings Personalization = new();

    internal static void Load()
    {
        var config = new ModConfig();
        try
        {
            config.ResolvePaths();
            if (File.Exists(config.ConfigPath))
            {
                var text = File.ReadAllText(config.ConfigPath);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    using var document = JsonDocument.Parse(text);
                    var root = document.RootElement;
                    if (root.TryGetProperty("features", out var features))
                    {
                        config.Features.Personalization = ReadBool(features, "personalization", true);
                        config.Features.RegionBypass = ReadBool(features, "regionBypass", true);
                    }

                    if (root.TryGetProperty("personalization", out var personalization))
                    {
                        config.Personalization.ThemeKey =
                            ReadString(personalization, "themeKey", config.Personalization.ThemeKey);
                        config.Personalization.PresetName = config.Personalization.ThemeKey;
                        config.Personalization.Hue = ReadInt(personalization, "hue", config.Personalization.Hue);
                        config.Personalization.LightAdjust =
                            ReadDouble(personalization, "lightAdjust", config.Personalization.LightAdjust);
                        config.Personalization.ChromaAdjust =
                            ReadDouble(personalization, "chromaAdjust", config.Personalization.ChromaAdjust);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ModRuntime.Log("config load failed: " + ex.Message);
        }

        Current = config;
    }

    private void ResolvePaths()
    {
        // patch\PCLCeMod.dll → mod 根目录
        var hookDirectory = Path.GetDirectoryName(typeof(ModConfig).Assembly.Location);
        ModDirectory = string.IsNullOrEmpty(hookDirectory)
            ? AppContext.BaseDirectory
            : Directory.GetParent(hookDirectory)?.FullName ?? AppContext.BaseDirectory;

        var fromEnvironment = Environment.GetEnvironmentVariable("PCL_CE_MOD_CONFIG");
        ConfigPath = !string.IsNullOrWhiteSpace(fromEnvironment)
            ? Path.GetFullPath(fromEnvironment)
            : Path.Combine(ModDirectory, "pcl-ce-mod.json");
    }

    /// <summary>把个性化选择写回配置文件（保留 features / cePath 等其它字段）。</summary>
    internal static void SavePersonalization()
    {
        var config = Current;
        try
        {
            JsonObject root;
            if (File.Exists(config.ConfigPath))
            {
                root = JsonNode.Parse(File.ReadAllText(config.ConfigPath)) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            var personalization = root["personalization"] as JsonObject ?? new JsonObject();
            personalization["themeKey"] = config.Personalization.ThemeKey;
            personalization["hue"] = config.Personalization.Hue;
            personalization["lightAdjust"] = config.Personalization.LightAdjust;
            personalization["chromaAdjust"] = config.Personalization.ChromaAdjust;
            root["personalization"] = personalization;

            var directory = Path.GetDirectoryName(config.ConfigPath)!;
            Directory.CreateDirectory(directory);
            var temp = config.ConfigPath + ".tmp";
            File.WriteAllText(temp, root.ToJsonString(WriteOptions));
            if (File.Exists(config.ConfigPath))
            {
                try
                {
                    File.Copy(config.ConfigPath, config.ConfigPath + ".bak", true);
                }
                catch
                {
                    // 备份失败不影响主流程
                }
            }

            File.Move(temp, config.ConfigPath, true);
        }
        catch (Exception ex)
        {
            ModRuntime.Log("config save failed: " + ex.Message);
        }
    }

    // 旧调用名，保持兼容
    internal static void Save() => SavePersonalization();

    private static bool ReadBool(JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static string ReadString(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int ReadInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

    private static double ReadDouble(JsonElement element, string name, double fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : fallback;
}
