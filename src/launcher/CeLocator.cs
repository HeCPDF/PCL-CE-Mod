using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PclCeMod.Launcher;

/// <summary>
/// 找“正确的本平台 PCL CE”。
/// PCL CE 的 Release 资产命名形如 PCL2_CE_Release_x64.exe / PCL2_CE_Release_arm64.exe，
/// 所以先按名字模式搜，再用 PE 头里的 machine 字段核对是否为当前架构，避免选错。
/// </summary>
internal static class CeLocator
{
    private static readonly string[] NamePatterns =
    {
        "PCL2_CE_Release_*.exe",
        "PCL2_CE_*.exe",
        "PCL2_CE*.exe"
    };

    public static string Locate(string modDirectory, LauncherConfig config, Action<string> log)
    {
        var currentMachine = MachineOfCurrentProcess();

        // 1) 配置里显式指定的路径
        if (!string.IsNullOrWhiteSpace(config.CePath))
        {
            var explicitPath = ResolvePath(modDirectory, config.CePath);
            if (File.Exists(explicitPath))
            {
                log($"使用配置指定的 CE: {explicitPath}");
                return explicitPath;
            }

            log($"配置的 cePath 不存在: {explicitPath}");
        }

        // 2) 依次搜索候选目录
        var directories = new List<string>();
        foreach (var directory in config.CeSearchDirectories)
            directories.Add(ResolvePath(modDirectory, directory));
        if (directories.Count == 0)
        {
            directories.Add(modDirectory);
            directories.Add(Path.Combine(modDirectory, ".."));
        }

        var candidates = new List<string>();
        foreach (var directory in directories.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var pattern in NamePatterns)
            {
                try
                {
                    candidates.AddRange(Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly));
                }
                catch
                {
                    // 无权访问的目录跳过
                }
            }
        }

        candidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0)
        {
            log("没有在候选目录里找到 PCL2_CE*.exe");
            return null;
        }

        // 3) 只保留 PE 架构与当前进程一致的可执行文件
        var matched = new List<string>();
        foreach (var candidate in candidates)
        {
            var machine = ReadPeMachine(candidate);
            if (machine == currentMachine) matched.Add(candidate);
            else log($"跳过架构不符的 {Path.GetFileName(candidate)} (machine=0x{machine:X4})");
        }

        if (matched.Count > 0)
        {
            var chosen = PreferByName(matched, currentMachine);
            log($"自动探测到 CE: {chosen}");
            return chosen;
        }

        // 4) 没有架构匹配时，退一步用第一个候选并提示
        var fallback = PreferByName(candidates, currentMachine);
        log($"警告：没有找到与当前架构一致的可执行文件，回退使用 {fallback}");
        return fallback;
    }

    private static string PreferByName(IEnumerable<string> candidates, ushort machine)
    {
        var hint = machine switch
        {
            0x8664 => "x64",
            0xAA64 => "arm64",
            0x014C => "x86",
            _ => null
        };

        var list = candidates.ToList();
        if (hint is not null)
        {
            var preferred = list.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path).EndsWith(hint, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null) return preferred;
        }

        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list[0];
    }

    private static ushort MachineOfCurrentProcess() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => 0x8664,
            Architecture.Arm64 => 0xAA64,
            Architecture.X86 => 0x014C,
            Architecture.Arm => 0x01C4,
            _ => 0x8664
        };

    /// <summary>读 PE 头里的 IMAGE_FILE_HEADER.Machine；读不出来返回 0。</summary>
    private static ushort ReadPeMachine(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D) return 0;
            stream.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset + 6 > stream.Length) return 0;
            stream.Seek(peOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550) return 0;
            return reader.ReadUInt16();
        }
        catch
        {
            return 0;
        }
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        var combined = Path.Combine(baseDirectory, path);
        return Path.GetFullPath(combined);
    }
}
