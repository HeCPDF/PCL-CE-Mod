# PCL CE Mod

给 [PCL CE](https://github.com/PCL-Community/PCL-CE) 加两个功能的外挂式补丁，**不修改 CE 本体、不占用 CE 的可执行文件名、不改存档**。

| 功能 | 说明 |
| --- | --- |
| 个性化功能补全（`personalization`） | 把 PCL 快照版 2.99「设置 → 个性化 → 基础」里的主题 UI 移植进 CE：主题 / 隐藏主题单选、色相 / 明度 / 彩度滑杆（带悬浮数值提示），保留 CE 自己的「浅色 / 深色 / 跟随系统」模式下拉 |
| 绕过地区检测（`regionBypass`） | 让 CE 的地区判定恒为「其他地区」，从而不再因大陆/未知地区在启动游戏时跳过原版文件下载 |

## 快速开始

1. 确认 PCL CE 与 `PCL-CE-Mod` 放在同一层目录，例如：

   ```
   Games\
     PCL-CE\
       PCL2_CE_Release_x64.exe      <- 官方 CE，保持原名，不要动
       PCL\
     PCL-CE-Mod\                    <- 本程序
       PCL-CE-Mod.exe
       pcl-ce-mod.json
       patch\
         PCLCeMod.dll
         0Harmony.dll
       logs\
   ```

   不在同一层也没关系，改 `pcl-ce-mod.json` 的 `cePath` 或 `ceSearchDirectories` 即可。

2. 双击 `PCL-CE-Mod.exe`（不是 CE 本体）。启动器会：
   - 按 `PCL2_CE_Release_<arch>.exe` 的命名模式搜索 CE；
   - 读 PE 头里的 machine 字段，只认与当前平台架构一致的那一个（x64 / arm64 / x86）；
   - 设好 `DOTNET_STARTUP_HOOKS` 注入 `patch\PCLCeMod.dll`，然后拉起 CE。

3. 进「设置 → 个性化 → 基础」就能看到补全后的主题面板。

> 直接用官方 exe 启动 = 完全没有补丁，所以随时可以两边切换对比。

## 配置 `pcl-ce-mod.json`

```json
{
  "cePath": "",
  "ceSearchDirectories": [ ".", "..\\PCL-CE" ],
  "features": {
    "personalization": true,
    "regionBypass": true
  },
  "personalization": {
    "themeKey": "龙猫蓝",
    "hue": 255,
    "lightAdjust": 0,
    "chromaAdjust": -0.2
  },
  "launcher": { "extraArgs": [] }
}
```

| 字段 | 说明 |
| --- | --- |
| `cePath` | 直接指定 CE 可执行文件，留空则自动探测。支持相对路径（相对 mod 目录）与绝对路径 |
| `ceSearchDirectories` | 自动探测时依次搜索的目录，第一个是 mod 目录，第二个 `..\PCL-CE` 是常见布局 |
| `features.personalization` | 个性化功能补全开关 |
| `features.regionBypass` | 绕过地区检测开关 |
| `personalization.*` | 当前选中的主题与自定义参数，补丁在 UI 里改动时会自动写回本文件（保留其它字段，并留 `.bak`） |
| `launcher.extraArgs` | 透传给 CE 的额外命令行参数 |

任一项关掉后重启启动器即可生效。

## 工作原理

```
PCL-CE-Mod.exe  --DOTNET_STARTUP_HOOKS-->  patch\PCLCeMod.dll
                                              ├─ Harmony patch  ThemeService.GetCurrentThemeArgs()   -> 主题色
                                              │   + 监听 PageSetupUI.Loaded，就地改造「基础」卡片
                                              └─ Harmony patch  RegionPolicy.Evaluate()               -> 地区判定恒为 Other
```

- 主题色：CE 自己用 `(色相, 明度偏移, 彩度偏移)` 重算全部配色资源，补丁只负责给出这三个数。
- 个性化 UI：在 CE 原「基础」卡片里折叠「浅色配色 / 深色配色」两列，插入 CE 原生 `MyRadioBox` / `MySlider`，不新加卡片。
- 地区检测：CE 用 `RegionPolicy.Evaluate` 判定 `Mainland` / `Unknown` 时，`RestrictedFeaturePolicy.IsRestricted` 为 `true`，启动游戏会跳过原版文件下载。补丁把判定改成 `Other`。
- 加载阶段会写自检日志（例如 `classification=Other, restricted=False`），便于确认补丁真的生效。

## 日志

- `logs\launcher.log`：启动器探测/启动过程
- `logs\patch.log`：补丁安装与各模块日志

## 构建

需要 .NET 10 SDK；运行时需要 .NET 10 Desktop Runtime（CE 本身就需要）。

```powershell
.\build.ps1                                  # 产物在 dist\
.\build.ps1 -DeployTo 'D:\Games\PCL-CE-Mod'  # 构建并部署
```

脚本会自动挑出机器上带 10.x SDK 的 `dotnet`（例如同时装了 9.x 与 10.x 时不会选错）；
也可以用 `-Dotnet 'C:\path\dotnet.exe'` 显式指定。

首次部署会自动生成默认 `pcl-ce-mod.json`，已存在则保留。

## 已知限制

- 只适配 PCL CE 2.15.1 实测（类型名变化时补丁会跳过并写日志，不会影响 CE 启动）。
- 主题映射是快照版 HSL → CE LCH 的近似，个别主题观感略有差异，可用「自定义」微调。
- 「自定义」的色相/明度/彩度是同一个主题色的三个参数，与快照版 HSL 取色不完全等价。
- CE 自动更新只替换它自己进程路径上的 exe，本 mod 位于独立目录，不受影响；但 CE 升级后内部类型若变动，需要按新版本重新适配。

## 免责声明

本项目仅供学习与个人使用。绕过地区检测可能违反 PCL CE 或其依赖服务的使用条款，是否使用、产生何种后果由使用者自行判断与承担。本项目不包含、不修改任何 PCL CE 的可执行文件或游戏文件。
