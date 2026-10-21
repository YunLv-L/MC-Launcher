using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCLauncher;

public class LaunchService
{
    private readonly string _baseDir;
    private readonly IProgress<string> _log;
    private readonly IProgress<string> _status;

    public LaunchService(string baseDir, IProgress<string> log, IProgress<string> status)
    {
        _baseDir = baseDir;
        _log = log;
        _status = status;
    }

    public async Task LaunchAsync(string versionId, string username, string javaPath)
    {
        _status.Report("正在准备启动环境...");

        var versionChain = await LoadVersionChainAsync(versionId);
        if (versionChain.Count == 0) throw new Exception("无法加载版本信息");

        var mergedDetail = MergeVersionDetails(versionChain);
        var versionDir = Path.Combine(_baseDir, "versions", versionId);
        var nativesDir = Path.Combine(versionDir, "natives");
        Directory.CreateDirectory(nativesDir);

        _log.Report("正在解压 Natives...");
        foreach (var lib in mergedDetail.Libraries.Where(IsAllowed))
        {
            if (lib.Downloads?.Artifact == null) continue;
            var jarPath = Path.Combine(_baseDir, "libraries", lib.Downloads.Artifact.Path);
            if (File.Exists(jarPath)) ExtractNatives(jarPath, nativesDir);
        }

        _log.Report("正在拼装 ClassPath...");
        var classPath = new List<string>();
        foreach (var lib in mergedDetail.Libraries.Where(IsAllowed))
        {
            if (lib.Downloads?.Artifact != null)
            {
                var path = Path.Combine(_baseDir, "libraries", lib.Downloads.Artifact.Path);
                if (File.Exists(path)) classPath.Add(path);
            }
        }

        // 原版 Jar
        var clientJar = Path.Combine(_baseDir, "versions", versionChain.Last().Id, $"{versionChain.Last().Id}.jar");
        if (File.Exists(clientJar)) classPath.Add(clientJar);

        // Forge 等安装器生成的 Wrapper Jar
        if (versionId != versionChain.Last().Id)
        {
            var subJar = Path.Combine(_baseDir, "versions", versionId, $"{versionId}.jar");
            if (File.Exists(subJar)) classPath.Add(subJar);
        }

        var cpString = string.Join(";", classPath);

        _status.Report("正在拼装启动参数...");
        var args = new List<string>
        {
            "-Xmx2G", "-Xms1G",
            "-Djava.library.path=" + nativesDir,

            // 🌟 核心修复：添加允许 Forge 反射访问的参数 (兼容 Java 17+)
            "--add-opens", "java.base/java.lang.invoke=ALL-UNNAMED",
            "--add-opens", "java.base/java.lang=ALL-UNNAMED",
            "--add-opens", "java.base/java.lang.reflect=ALL-UNNAMED",

            "-cp", cpString,
            mergedDetail.MainClass ?? "net.minecraft.client.main.Main",
        };

        args.AddRange(new[] {
            "--username", username, "--version", versionId,
            "--gameDir", _baseDir, "--assetsDir", Path.Combine(_baseDir, "assets"),
            "--assetIndex", mergedDetail.AssetIndex?.Id ?? "legacy",
            "--uuid", Guid.NewGuid().ToString("N"), "--accessToken", "0",
            "--userType", "legacy", "--versionType", "release"
        });

        _status.Report("正在启动游戏...");
        _log.Report($"拉起 Java 进程，主类: {mergedDetail.MainClass}");

        var psi = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = string.Join(" ", args.Select(a => a.Contains(" ") ? $"\"{a}\"" : a)),
            UseShellExecute = false,
            WorkingDirectory = _baseDir
        };

        var process = Process.Start(psi);
        if (process == null) throw new Exception("启动进程失败");

        await process.WaitForExitAsync();
        _status.Report("游戏已退出");
    }

    private async Task<List<VersionDetail>> LoadVersionChainAsync(string versionId)
    {
        var chain = new List<VersionDetail>();
        string? currentId = versionId;

        while (!string.IsNullOrEmpty(currentId))
        {
            var jsonPath = Path.Combine(_baseDir, "versions", currentId, $"{currentId}.json");
            if (!File.Exists(jsonPath))
                throw new Exception($"找不到版本 JSON: {currentId}，请先下载该版本。");

            var json = await File.ReadAllTextAsync(jsonPath);
            var detail = JsonSerializer.Deserialize<VersionDetail>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (detail == null) throw new Exception($"解析版本 JSON 失败: {currentId}");

            chain.Add(detail);
            currentId = detail.InheritsFrom;
        }

        chain.Reverse();
        return chain;
    }

    private VersionDetail MergeVersionDetails(List<VersionDetail> chain)
    {
        var result = new VersionDetail
        {
            Id = chain.Last().Id,
            MainClass = chain.Last().MainClass ?? chain.First().MainClass,
            AssetIndex = chain.First().AssetIndex,
            Libraries = new List<Library>()
        };

        var seenLibs = new HashSet<string>();
        foreach (var detail in chain)
        {
            foreach (var lib in detail.Libraries)
            {
                if (seenLibs.Add(lib.Name))
                {
                    result.Libraries.Add(lib);
                }
            }
        }

        return result;
    }

    private void ExtractNatives(string jarPath, string nativesDir)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
                {
                    var destPath = Path.Combine(nativesDir, entry.Name);
                    if (!File.Exists(destPath))
                        entry.ExtractToFile(destPath, overwrite: true);
                }
            }
        }
        catch { }
    }

    private static bool IsAllowed(Library lib)
    {
        if (lib.Rules == null || lib.Rules.Count == 0) return true;
        bool allowed = false;
        foreach (var rule in lib.Rules)
        {
            bool match = rule.Os == null
                || (rule.Os.Name == "windows"
                    && (rule.Os.Arch == null || rule.Os.Arch == "x86_64"));
            if (match) allowed = rule.Action == "allow";
        }
        return allowed;
    }
}
