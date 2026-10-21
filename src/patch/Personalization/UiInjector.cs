using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

internal static class UiInjector
{
    private static readonly ConditionalWeakTable<object, object> PatchedPages = new();
    private static readonly List<BasicCard> Cards = new();

    internal static void Register(Type pageType)
    {
        EventManager.RegisterClassHandler(pageType, FrameworkElement.LoadedEvent, new RoutedEventHandler(OnPageLoaded));
    }

    private static void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Build(sender);
        }
        catch (Exception ex)
        {
            ModRuntime.Log("basic card patch failed: " + ex);
        }
    }

    internal static void RefreshAll()
    {
        foreach (var card in Cards.ToArray())
        {
            try
            {
                card.Refresh();
            }
            catch (Exception ex)
            {
                ModRuntime.Log("card refresh failed: " + ex.Message);
            }
        }
    }

    private static void Build(object page)
    {
        if (page is null || PatchedPages.TryGetValue(page, out _)) return;
        var card = new BasicCard(page);
        if (!card.Ok)
        {
            ModRuntime.Log("BasicCard could not patch the page");
            return;
        }
        PatchedPages.Add(page, card);
        Cards.Add(card);
        card.Refresh();
        card.ArmAfterLoad();
        ModRuntime.Log("basic card modified: theme / hidden-theme radios in place");
    }
}

internal sealed class BasicCard
{
    private static Type RadioType;
    private static Type SliderType;
    private static bool TypesResolved;

    internal bool Ok { get; private set; }

    private readonly List<(object Radio, ThemeEntry Entry)> Radios = new();
    private object HueSlider;
    private object LightSlider;
    private object ChromaSlider;
    private Panel CustomPanel;
    private bool Applying;
    private bool Ready;
    private readonly DispatcherTimer Debounce;

    internal BasicCard(object page)
    {
        if (!ResolveTypes()) return;
        Debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        Debounce.Tick += (_, _) =>
        {
            Debounce.Stop();
            ModConfig.SavePersonalization();
            ColorActions.Apply();
        };

        // 找到 CE 原有的「主题 / 浅色配色 / 深色配色」那一行，并整行替换
        var themeRow = FindThemeRow(page);
        if (themeRow is null)
        {
            ModRuntime.Log("CE theme row not found");
            return;
        }
        var container = VisualTreeHelper.GetParent(themeRow) as Panel;
        if (container is null)
        {
            ModRuntime.Log("CE theme row container not found");
            return;
        }
        // 保留 CE 原有的「主题 + 浅色/深色模式」选择，只去掉「浅色配色 / 深色配色」两个下拉
        TrimColorCombos(themeRow);

        // 主题已解锁，移除「想要逃离蓝色困境？请使用官方快照版」广告条
        try
        {
            var bannerField = page.GetType().GetField("PanLauncherHide", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (bannerField?.GetValue(page) is UIElement banner && container.Children.Contains(banner))
            {
                container.Children.Remove(banner);
                ModRuntime.Log("removed CE blue-theme banner");
            }
        }
        catch (Exception ex)
        {
            ModRuntime.Log("remove banner failed: " + ex.Message);
        }

        var root = new Grid();
        // 第一列与 CE 其它行共用 SharedSizeGroup="Name1"，标签和滑条起点才能对齐
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Name1" });
        for (var i = 1; i < 6; i++)
        {
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 第一行标签沿用上面「主题」那一行的标签，这里只放选项
        var column = 1;
        foreach (var entry in Presets.Normal)
        {
            AddRadio(root, entry, 0, column++);
        }

        AddLabel(root, "隐藏主题", 1);
        column = 1;
        for (var i = 0; i < Presets.Hidden.Count; i++)
        {
            var row = i < 5 ? 1 : 2;
            if (i == 5) column = 1;
            AddRadio(root, Presets.Hidden[i], row, column++);
        }

        CustomPanel = CreateCustomPanel();
        Grid.SetRow(CustomPanel, 3);
        Grid.SetColumn(CustomPanel, 0);
        Grid.SetColumnSpan(CustomPanel, 6);
        root.Children.Add(CustomPanel);

        // 注意：上面删掉了广告条，themeRow 的索引会变，插入时必须重新取
        var insertAt = container.Children.IndexOf(themeRow);
        if (insertAt >= 0)
        {
            container.Children.Insert(insertAt + 1, root);
        }
        else
        {
            container.Children.Add(root);
        }
        Ok = true;
    }

    private static bool ResolveTypes()
    {
        if (TypesResolved) return RadioType is not null;
        TypesResolved = true;
        var main = ModRuntime.MainAssembly;
        if (main is null) return false;
        RadioType = main.GetType("PCL.MyRadioBox", false);
        SliderType = main.GetType("PCL.MySlider", false);
        ModRuntime.Log($"card types: radio={RadioType?.Name}, slider={SliderType?.Name}");
        return RadioType is not null && SliderType is not null;
    }

    private static Grid FindThemeRow(object page)
    {
        try
        {
            var field = page.GetType().GetField("ComboLightColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(page) is not DependencyObject lightColor) return null;
            DependencyObject current = lightColor;
            while (current is not null && current is not Grid)
            {
                current = VisualTreeHelper.GetParent(current);
            }
            return current as Grid;
        }
        catch (Exception ex)
        {
            ModRuntime.Log("FindThemeRow failed: " + ex.Message);
            return null;
        }
    }

    // 只保留「主题」标签与「浅色/深色模式」下拉；「浅色配色 / 深色配色」两个下拉与本补丁冲突，去掉
    private static void TrimColorCombos(Grid themeRow)
    {
        try
        {
            const int keepColumns = 2;
            for (var i = 0; i < themeRow.ColumnDefinitions.Count; i++)
            {
                if (i >= keepColumns) themeRow.ColumnDefinitions[i].Width = new GridLength(0);
            }
            foreach (UIElement child in themeRow.Children)
            {
                var column = Grid.GetColumn(child);
                if (column >= keepColumns)
                {
                    child.Visibility = Visibility.Collapsed;
                }
                else if (column == 1 && child is FrameworkElement modePicker)
                {
                    modePicker.HorizontalAlignment = HorizontalAlignment.Left;
                    modePicker.Width = 200;
                }
            }
            themeRow.Visibility = Visibility.Visible;
            ModRuntime.Log("kept CE light/dark mode picker, hid light/dark color combos");
        }
        catch (Exception ex)
        {
            ModRuntime.Log("trim color combos failed: " + ex.Message);
        }
    }

    private static void AddLabel(Grid root, string text, int row)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        };
        SetBrush(label, TextBlock.ForegroundProperty, "ColorBrushGray1");
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        root.Children.Add(label);
    }

    private void AddRadio(Grid root, ThemeEntry entry, int row, int column)
    {
        var radio = Activator.CreateInstance(RadioType)!;
        SetProperty(radio, "Text", entry.Name);
        if (!string.IsNullOrEmpty(entry.ToolTip)) SetProperty(radio, "ToolTip", entry.ToolTip);
        var element = (FrameworkElement)radio;
        element.VerticalAlignment = VerticalAlignment.Center;
        element.HorizontalAlignment = HorizontalAlignment.Left;
        element.Height = 22;
        element.Margin = new Thickness(0, 4, 12, 4);
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        root.Children.Add(element);
        Radios.Add((radio, entry));
        WireCheck(radio);
    }

    private Panel CreateCustomPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 2), Visibility = Visibility.Collapsed };
        HueSlider = CreateSliderRow(panel, "色相", 359, value => Convert.ToInt32(value) + "°");
        LightSlider = CreateSliderRow(panel, "明度", 100, value => (Convert.ToInt32(value) - 50).ToString());
        ChromaSlider = CreateSliderRow(panel, "彩度", 200, value => (Convert.ToInt32(value) - 100).ToString());
        return panel;
    }

    // 与「不透明度」那一行保持完全一致的布局：标签列共用 SharedSizeGroup，滑条占满剩余宽度
    private object CreateSliderRow(Panel parent, string label, int maxValue, Func<object, object> hint)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Name1" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 20, 0)
        };
        SetBrush(labelBlock, TextBlock.ForegroundProperty, "ColorBrushGray1");
        var slider = Activator.CreateInstance(SliderType)!;
        SetProperty(slider, "MaxValue", maxValue);
        SetProperty(slider, "ValueByKey", (uint)1);
        SetHintText(slider, hint);
        var wired = WireChange(slider);
        var sliderElement = (FrameworkElement)slider;
        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(sliderElement, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(sliderElement);
        parent.Children.Add(grid);
        ModRuntime.Log($"slider row '{label}' created, change wired={wired}");
        return slider;
    }

    private static void SetHintText(object slider, Func<object, object> hint)
    {
        try
        {
            var field = slider.GetType().GetField("getHintText", BindingFlags.Instance | BindingFlags.Public);
            if (field is null)
            {
                ModRuntime.Log("getHintText field not found; floating value hint disabled");
                return;
            }
            field.SetValue(slider, hint);
        }
        catch (Exception ex)
        {
            ModRuntime.Log("set getHintText failed: " + ex.Message);
        }
    }

    private void OnRadioCheck(object sender, object e)
    {
        var byMouse = false;
        try
        {
            var field = e?.GetType().GetField("raiseByMouse", BindingFlags.Instance | BindingFlags.Public);
            if (field?.GetValue(e) is bool value) byMouse = value;
        }
        catch
        {
            // ignore
        }
        if (Applying || !Ready || !byMouse) return;
        ThemeEntry entry = null;
        foreach (var (radio, theme) in Radios)
        {
            if (ReferenceEquals(radio, sender))
            {
                entry = theme;
                break;
            }
        }
        if (entry is null) return;
        var config = ModConfig.Current.Personalization;
        config.Enabled = true;
        config.ThemeKey = entry.Name;
        config.PresetName = entry.Name;
        if (entry.IsCustom)
        {
            CustomPanel.Visibility = Visibility.Visible;
            ApplyFromSliders(config);
        }
        else
        {
            CustomPanel.Visibility = Visibility.Collapsed;
            config.Hue = entry.Hue;
            config.LightAdjust = entry.Light;
            config.ChromaAdjust = entry.Chroma;
        }
        ModConfig.SavePersonalization();
        ColorActions.Apply();
        ModRuntime.Log("theme selected: " + entry.Name);
    }

    private void OnSliderChanged(object sender, bool user)
    {
        if (Applying) return;
        var config = ModConfig.Current.Personalization;
        ApplyFromSliders(config);
        config.Enabled = true;
        config.ThemeKey = "自定义";
        config.PresetName = "自定义";
        UpdateHints();
        SelectCustomRadio();
        // 立即落盘：切换页面 / 重启都不会丢；颜色重算合并到 debounce 里
        ModConfig.SavePersonalization();
        Debounce.Stop();
        Debounce.Start();
    }

    // 拖动滑条即视为「自定义」，把选中项同步过去（SetChecked 不会触发用户点击逻辑）
    private void SelectCustomRadio()
    {
        try
        {
            CustomPanel.Visibility = Visibility.Visible;
        }
        catch
        {
            // 忽略
        }
        foreach (var (radio, entry) in Radios)
        {
            try
            {
                radio.GetType().GetMethod("SetChecked", new[] { typeof(bool), typeof(bool) })
                    ?.Invoke(radio, new object[] { entry.IsCustom, false });
            }
            catch (Exception ex)
            {
                ModRuntime.Log("sync custom radio failed: " + ex.Message);
            }
        }
    }

    private void ApplyFromSliders(PersonalizationSettings config)
    {
        config.Hue = Math.Clamp(GetValue(HueSlider), 0, 359);
        config.LightAdjust = (Math.Clamp(GetValue(LightSlider), 0, 100) - 50) / 50.0;
        config.ChromaAdjust = (Math.Clamp(GetValue(ChromaSlider), 0, 200) - 100) / 100.0;
    }

    internal void ArmAfterLoad()
    {
        var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            Refresh();
        };
        settle.Start();
        var arm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        arm.Tick += (_, _) =>
        {
            arm.Stop();
            Refresh();
            Ready = true;
            ModRuntime.Log("theme radios armed");
        };
        arm.Start();
    }

    internal void Refresh()
    {
        if (!Ok) return;
        var config = ModConfig.Current.Personalization;
        var entry = Presets.Find(config.ThemeKey) ?? Presets.Normal[0];
        if (config.ThemeKey != entry.Name)
        {
            // 配置里的键名不在列表里（旧配置 / 手改），规范化回已知的一项
            config.ThemeKey = entry.Name;
            config.PresetName = entry.Name;
        }
        Applying = true;
        CustomPanel.Visibility = entry.IsCustom ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (radio, theme) in Radios)
        {
            var checkedValue = theme.Name == entry.Name;
            try
            {
                radio.GetType().GetMethod("SetChecked", new[] { typeof(bool), typeof(bool) })
                    ?.Invoke(radio, new object[] { checkedValue, false });
            }
            catch (Exception ex)
            {
                ModRuntime.Log("SetChecked failed: " + ex.Message);
            }
        }
        SetProperty(HueSlider, "Value", Math.Clamp(config.Hue, 0, 359));
        SetProperty(LightSlider, "Value", (int)Math.Round(Math.Clamp(config.LightAdjust, -1, 1) * 50 + 50));
        SetProperty(ChromaSlider, "Value", (int)Math.Round(Math.Clamp(config.ChromaAdjust, -1, 1) * 100 + 100));
        Applying = false;
        UpdateHints();
    }

    // 悬浮提示由 MySlider 自带的 Popup 负责（拖动时显示），这里同步一下悬停提示文字
    private void UpdateHints()
    {
        SetProperty(HueSlider, "ToolTip", Math.Clamp(GetValue(HueSlider), 0, 359) + "°");
        SetProperty(LightSlider, "ToolTip", (Math.Clamp(GetValue(LightSlider), 0, 100) - 50).ToString());
        SetProperty(ChromaSlider, "ToolTip", (Math.Clamp(GetValue(ChromaSlider), 0, 200) - 100).ToString());
    }

    private static int GetValue(object slider)
    {
        try
        {
            return slider.GetType().GetProperty("Value")?.GetValue(slider) is int value ? value : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void SetProperty(object target, string name, object value)
    {
        try
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property?.SetValue(target, value);
        }
        catch (Exception ex)
        {
            ModRuntime.Log($"set {name} failed: {ex.Message}");
        }
    }

    private void WireCheck(object radio)
    {
        try
        {
            var eventInfo = radio.GetType().GetEvent("Check", BindingFlags.Instance | BindingFlags.Public);
            var method = typeof(BasicCard).GetMethod("OnRadioCheck", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(object), typeof(object) }, null);
            if (eventInfo?.EventHandlerType is null || method is null) return;
            var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, method);
            eventInfo.AddEventHandler(radio, handler);
        }
        catch (Exception ex)
        {
            ModRuntime.Log("wire radio failed: " + ex.Message);
        }
    }

    private bool WireChange(object slider)
    {
        try
        {
            var eventInfo = slider.GetType().GetEvent("Change", BindingFlags.Instance | BindingFlags.Public);
            var method = typeof(BasicCard).GetMethod("OnSliderChanged", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(object), typeof(bool) }, null);
            if (eventInfo?.EventHandlerType is null || method is null)
            {
                ModRuntime.Log($"wire slider failed: event={eventInfo is not null}, method={method is not null}");
                return false;
            }
            var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, method);
            eventInfo.AddEventHandler(slider, handler);
            return true;
        }
        catch (Exception ex)
        {
            ModRuntime.Log("wire slider failed: " + ex.Message);
            return false;
        }
    }

    private static void SetBrush(FrameworkElement target, DependencyProperty property, string resourceKey)
    {
        try
        {
            if (Application.Current is null) return;
            target.SetResourceReference(property, resourceKey);
        }
        catch (Exception ex)
        {
            ModRuntime.Log("SetResourceReference failed: " + ex.Message);
        }
    }
}






