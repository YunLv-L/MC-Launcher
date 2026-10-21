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

        var clientJar = Path.Combine(_baseDir, "versions", versionChain.Last().Id, $"{versionChain.Last().Id}.jar");
        if (File.Exists(clientJar)) classPath.Add(clientJar);

        if (versionId != versionChain.Last().Id)
        {
            var subJar = Path.Combine(_baseDir, "versions", versionId, $"{versionId}.jar");
            if (File.Exists(subJar)) classPath.Add(subJar);
        }

        var cpString = string.Join(";", classPath);

        _status.Report("正在拼装启动参数...");
        var args = new List<string>();
        var uuid = Guid.NewGuid().ToString("N");
        var assetsDir = Path.Combine(_baseDir, "assets");
        var libDir = Path.Combine(_baseDir, "libraries").Replace("\\", "/");

        // 1. 解析 JVM 参数
        if (mergedDetail.Arguments?.Jvm != null)
        {
            foreach (var item in mergedDetail.Arguments.Jvm)
            {
                if (item is JsonElement elem && elem.ValueKind == JsonValueKind.String)
                {
                    var arg = elem.GetString()!;
                    if (arg == "-cp" || arg == "${classpath}") continue;

                    arg = arg.Replace("${library_directory}", libDir)
                             .Replace("${version_name}", versionId)
                             .Replace("${classpath_separator}", ";")
                             .Replace("${launcher_name}", "MC-Launcher")
                             .Replace("${launcher_version}", "0.1");

                    args.Add(arg);
                }
            }
        }

        // 兜底内存参数
        if (!args.Any(a => a.StartsWith("-Xmx")))
        {
            args.Add("-Xmx2G");
            args.Add("-Xms1G");
        }

        args.Add("-Djava.library.path=" + nativesDir);
        args.Add("-cp");
        args.Add(cpString);
        args.Add(mergedDetail.MainClass ?? "net.minecraft.client.main.Main");

        // 2. 解析 Game 参数 (核心修复：读取 Forge 自带的 game 参数，不再手动硬编码)
        bool hasGameArgs = false;
        if (mergedDetail.Arguments?.Game != null && mergedDetail.Arguments.Game.Count > 0)
        {
            foreach (var item in mergedDetail.Arguments.Game)
            {
                if (item is JsonElement elem && elem.ValueKind == JsonValueKind.String)
                {
                    var arg = elem.GetString()!;

                    arg = arg.Replace("${auth_player_name}", username)
                             .Replace("${version_name}", versionId)
                             .Replace("${game_directory}", _baseDir)
                             .Replace("${assets_root}", assetsDir)
                             .Replace("${assets_index_name}", mergedDetail.AssetIndex?.Id ?? "legacy")
                             .Replace("${auth_uuid}", uuid)
                             .Replace("${auth_access_token}", "0") // 离线模式 Token
                             .Replace("${user_type}", "legacy")
                             .Replace("${version_type}", "release");

                    args.Add(arg);
                    hasGameArgs = true;
                }
            }
        }

        // 3. 如果没有 game 参数 (例如极老的版本)，使用基础兜底参数
        if (!hasGameArgs)
        {
            args.AddRange(new[] {
                "--username", username, "--version", versionId,
                "--gameDir", _baseDir, "--assetsDir", assetsDir,
                "--assetIndex", mergedDetail.AssetIndex?.Id ?? "legacy",
                "--uuid", uuid, "--accessToken", "0",
                "--userType", "legacy", "--versionType", "release"
            });
        }

        _status.Report("正在启动游戏...");
        _log.Report($"拉起 Java 进程，主类: {mergedDetail.MainClass}");

        var psi = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = string.Join(" ", args.Select(a => a.Contains(" ") ? $"\"{a}\"" : a)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _baseDir
        };

        using var process = new Process { StartInfo = psi };
        var crashLog = new List<string>();

        process.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _log.Report($"[Game] {e.Data}");
                crashLog.Add(e.Data);
            }
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _log.Report($"[Game Error] {e.Data}");
                crashLog.Add("[Error] " + e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _status.Report($"游戏异常退出 (代码: {process.ExitCode})");
            _log.Report($"❌ 游戏崩溃了！详细日志已保存到 crash-report.txt");

            var crashPath = Path.Combine(AppContext.BaseDirectory, "crash-report.txt");
            await File.WriteAllLinesAsync(crashPath, crashLog);
        }
        else
        {
            _status.Report("游戏已正常退出");
            _log.Report("游戏进程正常结束。");
        }
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
            Libraries = new List<Library>(),
            Arguments = new Arguments // 确保初始化
            {
                Jvm = new List<object>(),
                Game = new List<object>()
            }
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

            if (detail.Arguments?.Jvm != null)
            {
                foreach (var jvmArg in detail.Arguments.Jvm) result.Arguments.Jvm.Add(jvmArg);
            }

            // 🌟 核心修复：合并 Game 参数
            if (detail.Arguments?.Game != null)
            {
                foreach (var gameArg in detail.Arguments.Game) result.Arguments.Game.Add(gameArg);
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
