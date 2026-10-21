using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PclCeMod.Launcher;

internal static class Program
{
    private const string PatchFileName = "PCLCeMod.dll";
    private const string ConfigFileName = "pcl-ce-mod.json";

    [STAThread]
    private static int Main(string[] args)
    {
        var modDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var log = CreateLogger(Path.Combine(modDirectory, "logs", "launcher.log"));

        try
        {
            var configPath = Path.Combine(modDirectory, ConfigFileName);
            var config = LauncherConfig.Load(configPath, log);
            log($"启动器启动；mod={modDirectory}；personalization={config.Personalization}；" +
                $"regionBypass={config.RegionBypass}");

            var cePath = CeLocator.Locate(modDirectory, config, log);
            if (cePath is null)
            {
                ShowError("找不到 PCL CE。\r\n\r\n" +
                          "请在 pcl-ce-mod.json 里设置 cePath（PCL2_CE_Release_xxx.exe 的路径），" +
                          "或把本目录放到 CE 旁边。");
                log("未找到 CE，退出");
                return 2;
            }

            if (string.Equals(Path.GetFileName(cePath), "PCL-CE-Mod.exe", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("cePath 指向了启动器自身，请改成真正的 PCL CE 可执行文件。");
                log("cePath 指向自身，退出");
                return 3;
            }

            var startInfo = new ProcessStartInfo(cePath)
            {
                WorkingDirectory = Path.GetDirectoryName(cePath) ?? modDirectory,
                UseShellExecute = false
            };
            foreach (var argument in args) startInfo.ArgumentList.Add(argument);
            foreach (var argument in config.ExtraArgs) startInfo.ArgumentList.Add(argument);

            // 把配置路径与 mod 目录传给补丁，补丁据此读取 features / personalization
            startInfo.Environment["PCL_CE_MOD_CONFIG"] = configPath;
            startInfo.Environment["PCL_CE_MOD_DIR"] = modDirectory;

            var hookPath = Path.Combine(modDirectory, "patch", PatchFileName);
            if (File.Exists(hookPath))
            {
                startInfo.Environment["DOTNET_STARTUP_HOOKS"] = hookPath;
                log($"注入补丁: {hookPath}");
            }
            else
            {
                log($"补丁不存在（{hookPath}），按原版 CE 启动");
            }

            var process = Process.Start(startInfo);
            log($"已启动 CE: {cePath} (pid={process?.Id})");
            return 0;
        }
        catch (Exception ex)
        {
            log("启动失败: " + ex);
            ShowError("启动 PCL CE 失败：\r\n" + ex.Message);
            return 1;
        }
    }

    private static Action<string> CreateLogger(string logPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        }
        catch
        {
            // ignored
        }

        return message =>
        {
            try
            {
                File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch
            {
                // ignored
            }
        };
    }

    private static void ShowError(string message)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, message, "PCL CE Mod", 0x00000010); // MB_ICONERROR
        }
        catch
        {
            // ignored
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
