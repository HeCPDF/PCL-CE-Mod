using System;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

internal static class ColorActions
{
    internal static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ApplyCore));
    }

    private static void ApplyCore()
    {
        try
        {
            ModRuntime.ApplyColorResourcesMethod?.Invoke(null, null);
        }
        catch (Exception ex)
        {
            ModRuntime.Log("ApplyColorResources failed: " + ex.Message);
        }
        try
        {
            ModRuntime.ThemeRefreshMethod?.Invoke(null, new object[] { -1 });
        }
        catch (Exception ex)
        {
            ModRuntime.Log("ThemeRefresh failed: " + ex.Message);
        }
    }

    internal static void AutoNavigateLater()
    {
        var thread = new System.Threading.Thread(() =>
        {
            for (var i = 0; i < 150; i++)
            {
                System.Threading.Thread.Sleep(200);
                var app = Application.Current;
                if (app is null) continue;
                System.Threading.Thread.Sleep(2500);
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        NavigateToUiPage();
                    }
                    catch (Exception ex)
                    {
                        ModRuntime.Log("auto-nav failed: " + ex);
                    }
                }));
                return;
            }
            ModRuntime.Log("auto-nav gave up: Application.Current never appeared");
        })
        { IsBackground = true, Name = "PCLCEThemePatchAutoNav" };
        thread.Start();
        ModRuntime.Log("auto-nav scheduled");
    }

    private static void NavigateToUiPage()
    {
        var main = ModRuntime.MainAssembly;
        if (main is null)
        {
            ModRuntime.Log("auto-nav: main assembly missing");
            return;
        }
        var modMainType = main.GetType("PCL.ModMain", false);
        var form = modMainType?.GetField("frmMain", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (form is null)
        {
            ModRuntime.Log("auto-nav: frmMain not ready");
            return;
        }
        var pageType = main.GetType("PCL.FormMain+PageType", false);
        var subType = main.GetType("PCL.FormMain+PageSubType", false);
        var stackType = main.GetType("PCL.FormMain+PageStackData", false);
        if (pageType is null || subType is null || stackType is null)
        {
            ModRuntime.Log("auto-nav: page enum types missing");
            return;
        }
        var stack = Activator.CreateInstance(stackType);
        stackType.GetField("page")!.SetValue(stack, Enum.ToObject(pageType, 2));
        var method = form.GetType().GetMethod("PageChange", new[] { stackType, subType });
        if (method is null)
        {
            ModRuntime.Log("auto-nav: PageChange not found");
            return;
        }
        method.Invoke(form, new[] { stack, Enum.ToObject(subType, 1) });
        ModRuntime.Log("auto-nav: navigated to Setup/SetupUI");
    }
}

