using System.Collections.Generic;

internal sealed class ThemeEntry
{
    public ThemeEntry(string name, int hue, double light, double chroma, bool isCustom = false, string toolTip = null)
    {
        Name = name;
        Hue = hue;
        Light = light;
        Chroma = chroma;
        IsCustom = isCustom;
        ToolTip = toolTip;
    }

    public string Name { get; }
    public int Hue { get; }
    public double Light { get; }
    public double Chroma { get; }
    public bool IsCustom { get; }
    public string ToolTip { get; }

    public override string ToString() => Name;
}

internal static class Presets
{
    // 主题（快照版 2.99 基础板块同款）
    internal static readonly IReadOnlyList<ThemeEntry> Normal = new List<ThemeEntry>
    {
        new("龙猫蓝", 255, 0.00, -0.20),
        new("甜柠青", 225, 0.00, -0.233),
        new("小草绿", 175, 0.00, -0.30),
        new("菠萝黄", 120, 0.00, -0.233),
        new("橡木棕", 80, 0.00, -0.367)
    };

    // 隐藏主题（快照版需解锁，这里直接开放）
    internal static readonly IReadOnlyList<ThemeEntry> Hidden = new List<ThemeEntry>
    {
        new("玄素黑", 270, -0.40, -0.50, false, "灰色是个谎言"),
        new("滑稽彩", 300, 0.10, 0.20, false, "滑稽树上滑稽果，滑稽树下你和我"),
        new("铁杆粉", 30, 0.30, -0.10, false, "再次重逢的喜悦"),
        new("神秘紫", 330, 0.00, -0.233, false, null),
        new("欧皇彩", 335, 0.05, -0.233, false, "这就是传说中的欧皇了吧"),
        new("秋仪金", 95, 0.20, -0.133, false, null),
        new("活跃橙", 70, 0.10, -0.067, false, null),
        new("跳票红", 50, -0.10, -0.10, false, "进行一次正版登录，支持正版游戏"),
        new("极客蓝", 270, -0.30, -0.167, false, "右键打开解密游戏入口"),
        new("自定义", 255, 0.00, -0.20, true, "调节下方色相 / 明度 / 彩度")
    };

    internal static IEnumerable<ThemeEntry> All
    {
        get
        {
            foreach (var entry in Normal) yield return entry;
            foreach (var entry in Hidden) yield return entry;
        }
    }

    internal static ThemeEntry Find(string name)
    {
        foreach (var entry in All)
        {
            if (entry.Name == name) return entry;
        }
        return null;
    }
}
