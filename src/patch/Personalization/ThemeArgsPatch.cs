using System;

internal static class ThemeArgsPatch
{
    internal static bool Prefix(ref ValueTuple<int, double, double> __result)
    {
        try
        {
            var module = ModConfig.Current;
            if (!module.Features.Personalization) return true;
            var config = module.Personalization;
            if (!config.Enabled) return true;

            __result = (
                Math.Clamp(config.Hue, 0, 359),
                Math.Clamp(config.LightAdjust, -1.0, 1.0),
                Math.Clamp(config.ChromaAdjust, -1.0, 1.0));
            return false;
        }
        catch (Exception ex)
        {
            ModRuntime.Log("[personalization] theme args prefix failed: " + ex.Message);
            return true;
        }
    }
}
