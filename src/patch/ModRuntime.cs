using System;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;

internal static class ModRuntime
{
    internal static string HookDirectory = string.Empty;
    internal static readonly string BaseDirectory = AppContext.BaseDirectory;

    private static readonly string FallbackDataDirectory = Path.Combine(BaseDirectory, "PCL");

    /// <summary>mod 目录/logs/patch.log；mod 目录未知时退回 CE 数据目录。</summary>
    internal static string LogPath
    {
        get
        {
            var modDirectory = ModConfig.Current.ModDirectory;
            return string.IsNullOrEmpty(modDirectory)
                ? Path.Combine(FallbackDataDirectory, "Log", "patch.log")
                : Path.Combine(modDirectory, "logs", "patch.log");
        }
    }

    private static readonly object Gate = new();
    private static volatile bool _installed;
    private static bool _themeDone;
    private static bool _regionDone;

    internal static Assembly CoreAssembly;
    internal static Assembly MainAssembly;
    internal static Type ThemeServiceType;
    internal static Type ThemeManagerType;
    internal static Type PageSetupUiType;
    internal static MethodInfo ApplyColorResourcesMethod;
    internal static MethodInfo ThemeRefreshMethod;

    internal static bool Installed => _installed;

    internal static void Log(string message)
    {
        try
        {
            var path = LogPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
        }
        catch
        {
            // ignored
        }
    }

    internal static Assembly ResolveFromHookDirectory(object sender, ResolveEventArgs args)
    {
        try
        {
            var simpleName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(simpleName)) return null;
            var candidate = Path.Combine(HookDirectory, simpleName + ".dll");
            if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
        }
        catch (Exception ex)
        {
            Log("resolve " + args.Name + " failed: " + ex.Message);
        }

        return null;
    }

    internal static void Initialize()
    {
        ModConfig.Load();
        var features = ModConfig.Current.Features;
        Log($"hook initialized; patchDir={HookDirectory}; ceDir={BaseDirectory}; " +
            $"personalization={features.Personalization}; regionBypass={features.RegionBypass}");
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) TryInstall(assembly);
        var retry = new Thread(RetryLoop) { IsBackground = true, Name = "PCLCeModInstaller" };
        retry.Start();
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e) => TryInstall(e.LoadedAssembly);

    private static void RetryLoop()
    {
        for (var i = 0; i < 80 && !_installed; i++)
        {
            Thread.Sleep(250);
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) TryInstall(assembly);
            }
            catch
            {
                // ignored
            }
        }

        if (!_installed) Log("install did not complete");
    }

    private static void TryInstall(Assembly assembly)
    {
        try
        {
            if (assembly is null) return;
            var name = assembly.GetName().Name;
            if (name == "PCL.Core") CoreAssembly = assembly;
            else if (name == "Plain Craft Launcher 2") MainAssembly = assembly;
            else return;
            if (CoreAssembly is null || MainAssembly is null) return;
            lock (Gate)
            {
                if (_installed) return;
                if (InstallLocked()) _installed = true;
            }
        }
        catch (Exception ex)
        {
            Log("TryInstall failed: " + ex);
        }
    }

    private static bool InstallLocked()
    {
        var features = ModConfig.Current.Features;
        var harmony = new Harmony("pcl.ce.mod");
        var settled = true;

        if (features.Personalization && !_themeDone)
            _themeDone = InstallPersonalization(harmony);
        else if (!features.Personalization)
            settled = false; // 未启用；不影响其它模块

        if (features.RegionBypass && !_regionDone)
            _regionDone = RegionPatch.Install(CoreAssembly!, harmony);

        if (features.Personalization && !_themeDone) settled = false;
        if (features.RegionBypass && !_regionDone) settled = false;

        // 两个功能都没开时也视为安装完成
        if (!features.Personalization && !features.RegionBypass) settled = true;
        return settled;
    }

    private static bool InstallPersonalization(Harmony harmony)
    {
        ThemeServiceType = CoreAssembly!.GetType("PCL.Core.UI.Theme.ThemeService", false);
        ThemeManagerType = MainAssembly!.GetType("PCL.ThemeManager", false);
        PageSetupUiType = MainAssembly!.GetType("PCL.PageSetupUI", false);
        if (ThemeServiceType is null)
        {
            Log("[personalization] ThemeService type not found");
            return false;
        }

        var argsMethod = ThemeServiceType.GetMethod("GetCurrentThemeArgs", BindingFlags.Public | BindingFlags.Static);
        if (argsMethod is null)
        {
            Log("[personalization] GetCurrentThemeArgs not found");
            return false;
        }

        if (argsMethod.ReturnType != typeof(ValueTuple<int, double, double>))
        {
            Log("[personalization] unexpected GetCurrentThemeArgs return type: " + argsMethod.ReturnType.FullName);
            return false;
        }

        try
        {
            var prefix = new HarmonyMethod(
                typeof(ThemeArgsPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!);
            harmony.Patch(argsMethod, prefix: prefix);
            Log("[personalization] patched ThemeService.GetCurrentThemeArgs");
        }
        catch (Exception ex)
        {
            Log("[personalization] harmony patch failed: " + ex);
            return false;
        }

        ApplyColorResourcesMethod = ThemeServiceType.GetMethod("ApplyColorResources", BindingFlags.Public | BindingFlags.Static);
        ThemeRefreshMethod = ThemeManagerType?.GetMethod("ThemeRefresh", BindingFlags.Public | BindingFlags.Static);
        if (ApplyColorResourcesMethod is null) Log("[personalization] ApplyColorResources not found");
        if (ThemeRefreshMethod is null) Log("[personalization] ThemeRefresh not found");

        if (PageSetupUiType is not null)
        {
            try
            {
                UiInjector.Register(PageSetupUiType);
                Log("[personalization] registered PageSetupUI handler");
            }
            catch (Exception ex)
            {
                Log("[personalization] register UI handler failed: " + ex);
            }
        }
        else
        {
            Log("[personalization] PageSetupUI type not found");
        }

        ColorActions.Apply();
        if (Environment.GetEnvironmentVariable("PCL_CE_MOD_AUTONAV") == "1") ColorActions.AutoNavigateLater();
        return true;
    }
}
