using System;
using System.IO;

public static class StartupHook
{
    public static void Initialize()
    {
        try
        {
            ModRuntime.HookDirectory = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location) ?? string.Empty;
            AppDomain.CurrentDomain.AssemblyResolve += ModRuntime.ResolveFromHookDirectory;
            ModRuntime.Initialize();
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "pclce-themepatch-crash.log"),
                    DateTime.Now.ToString("O") + " " + ex + Environment.NewLine);
            }
            catch
            {
                // ignored
            }
        }
    }
}
