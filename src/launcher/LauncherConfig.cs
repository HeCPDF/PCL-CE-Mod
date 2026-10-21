using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PclCeMod.Launcher;

/// <summary>启动器侧读取的配置（与补丁共用一个 pcl-ce-mod.json）。</summary>
internal sealed class LauncherConfig
{
    public string CePath = string.Empty;
    public List<string> CeSearchDirectories = new();
    public List<string> ExtraArgs = new();
    public bool Personalization = true;
    public bool RegionBypass = true;

    public static LauncherConfig Load(string configPath, Action<string> log)
    {
        var config = new LauncherConfig();
        try
        {
            if (!File.Exists(configPath)) return config;
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;

            if (root.TryGetProperty("cePath", out var cePath) && cePath.ValueKind == JsonValueKind.String)
                config.CePath = cePath.GetString() ?? string.Empty;

            if (root.TryGetProperty("ceSearchDirectories", out var directories) &&
                directories.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in directories.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) config.CeSearchDirectories.Add(value);
                }
            }

            if (root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Object)
            {
                config.Personalization = ReadBool(features, "personalization", true);
                config.RegionBypass = ReadBool(features, "regionBypass", true);
            }

            if (root.TryGetProperty("launcher", out var launcher) && launcher.ValueKind == JsonValueKind.Object &&
                launcher.TryGetProperty("extraArgs", out var extraArgs) && extraArgs.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in extraArgs.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) config.ExtraArgs.Add(value);
                }
            }
        }
        catch (Exception ex)
        {
            log("读取配置失败，使用默认值: " + ex.Message);
        }

        return config;
    }

    private static bool ReadBool(JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
}
