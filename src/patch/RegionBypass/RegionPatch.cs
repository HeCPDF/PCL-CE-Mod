using System;
using System.Reflection;
using HarmonyLib;

// 「绕过地区检测」模块。
// PCL CE 用 PCL.Core.App.Region.RegionPolicy.Evaluate 判定 Mainland / Unknown 时，
// 会限制启动游戏时的原版文件下载（RestrictedFeaturePolicy.IsRestricted）。
// 这里让 Evaluate 恒定返回 Other（IsRestricted == false），并且不弹地区限制提示。
internal static class RegionPatch
{
    internal static bool Installed { get; private set; }

    private static ConstructorInfo _assessmentCtor;
    private static object _otherClassification;
    private static Type _classificationType;

    internal static bool Install(Assembly coreAssembly, Harmony harmony)
    {
        if (Installed) return true;
        try
        {
            var policyType = coreAssembly.GetType("PCL.Core.App.Region.RegionPolicy", false);
            var assessmentType = coreAssembly.GetType("PCL.Core.App.Region.RegionAssessment", false);
            _classificationType = coreAssembly.GetType("PCL.Core.App.Region.RegionClassification", false);
            if (policyType is null || assessmentType is null || _classificationType is null)
            {
                ModRuntime.Log("[region] RegionPolicy / RegionAssessment / RegionClassification not found");
                return false;
            }

            var evaluate = policyType.GetMethod("Evaluate", BindingFlags.Public | BindingFlags.Static);
            if (evaluate is null || evaluate.ReturnType != assessmentType)
            {
                ModRuntime.Log("[region] RegionPolicy.Evaluate not found or unexpected signature");
                return false;
            }

            _assessmentCtor = assessmentType.GetConstructor(new[] { _classificationType, typeof(string) });
            if (_assessmentCtor is null)
            {
                ModRuntime.Log("[region] RegionAssessment(RegionClassification, string) ctor not found");
                return false;
            }

            _otherClassification = Enum.ToObject(_classificationType, 1); // Other
            var prefix = new HarmonyMethod(
                typeof(RegionPatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)!);
            harmony.Patch(evaluate, prefix: prefix);
            Installed = true;
            ModRuntime.Log("[region] patched RegionPolicy.Evaluate -> Other (bypass enabled)");
            SelfCheck(policyType, evaluate);
            return true;
        }
        catch (Exception ex)
        {
            ModRuntime.Log("[region] install failed: " + ex);
            return false;
        }
    }

    // 打补丁后立刻用 CE 自己的真实信号调一次，把结果写进日志，方便确认 bypass 真的生效
    private static void SelfCheck(Type policyType, MethodInfo evaluate)
    {
        try
        {
            var readSignals = policyType.GetMethod("ReadWindowsSignals", BindingFlags.Public | BindingFlags.Static);
            if (readSignals is null || readSignals.GetParameters().Length != 0) return;
            var signals = readSignals.Invoke(null, null);
            var result = evaluate.Invoke(null, new[] { signals });
            if (result is null) return;
            var classification = result.GetType().GetProperty("Classification")?.GetValue(result);
            var reason = result.GetType().GetProperty("Reason")?.GetValue(result) as string;
            var restricted = result.GetType().GetProperty("IsRestricted")?.GetValue(result);
            ModRuntime.Log($"[region] self-check: classification={classification}, reason={reason}, restricted={restricted}");
        }
        catch (Exception ex)
        {
            ModRuntime.Log("[region] self-check failed: " + ex.Message);
        }
    }

    private static bool Prefix(ref object __result)
    {
        try
        {
            if (!ModConfig.Current.Features.RegionBypass) return true;
            if (_assessmentCtor is null || _otherClassification is null) return true;
            __result = _assessmentCtor.Invoke(new[] { _otherClassification, (object)"mod-bypass" });
            return false;
        }
        catch (Exception ex)
        {
            ModRuntime.Log("[region] prefix failed: " + ex.Message);
            return true;
        }
    }
}
