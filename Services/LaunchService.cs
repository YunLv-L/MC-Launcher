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

        var jsonPath = Path.Combine(_baseDir, "versions", versionId, $"{versionId}.json");
        if (!File.Exists(jsonPath))
            throw new Exception("找不到版本 JSON，请先下载该版本。");

        var jsonText = await File.ReadAllTextAsync(jsonPath);
        var detail = JsonSerializer.Deserialize<VersionDetail>(jsonText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new Exception("解析版本 JSON 失败");

        var versionDir = Path.Combine(_baseDir, "versions", versionId);
        var nativesDir = Path.Combine(versionDir, "natives");
        Directory.CreateDirectory(nativesDir);

        // 1. 解压 Natives
        _log.Report("正在解压 Natives...");
        foreach (var lib in detail.Libraries.Where(IsAllowed))
        {
            if (lib.Downloads?.Artifact == null) continue;
            var jarPath = Path.Combine(_baseDir, "libraries", lib.Downloads.Artifact.Path);
            if (File.Exists(jarPath))
                ExtractNatives(jarPath, nativesDir);
        }

        // 2. 拼装 ClassPath
        _log.Report("正在拼装 ClassPath...");
        var classPath = new List<string>();
        foreach (var lib in detail.Libraries.Where(IsAllowed))
        {
            if (lib.Downloads?.Artifact != null)
            {
                var path = Path.Combine(_baseDir, "libraries", lib.Downloads.Artifact.Path);
                if (File.Exists(path)) classPath.Add(path);
            }
        }
        var clientJar = Path.Combine(versionDir, $"{versionId}.jar");
        if (File.Exists(clientJar)) classPath.Add(clientJar);

        var cpString = string.Join(";", classPath);

        // 3. 拼装启动参数 (离线模式)
        _status.Report("正在拼装启动参数...");
        var args = new List<string>
        {
            "-Xmx2G", "-Xms1G",
            "-Djava.library.path=" + nativesDir,
            "-cp", cpString,
            "net.minecraft.client.main.Main",
            "--username", username,
            "--version", versionId,
            "--gameDir", _baseDir,
            "--assetsDir", Path.Combine(_baseDir, "assets"),
            "--assetIndex", detail.AssetIndex?.Id ?? "legacy",
            "--uuid", Guid.NewGuid().ToString("N"), // 离线模式 UUID
            "--accessToken", "0", // 离线模式 token
            "--userType", "legacy",
            "--versionType", "release"
        };

        // 4. 启动进程
        _status.Report("正在启动游戏...");
        _log.Report("拉起 Java 进程...");

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
        _log.Report($"游戏进程退出，退出码: {process.ExitCode}");
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
        catch
        {
            // 忽略解析失败的非 native jar
        }
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
