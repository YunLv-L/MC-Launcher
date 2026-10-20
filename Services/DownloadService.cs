using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCLauncher;

public class DownloadService
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string ResourcesBaseUrl = "https://resources.download.minecraft.net";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _baseDir;
    private readonly IProgress<string> _log;
    private readonly IProgress<double> _progress;
    private readonly IProgress<string> _status;
    private readonly IProgress<string> _fileProgress;
    private readonly int _maxThreads;

    public DownloadService(string baseDir, IProgress<string> log,
                           IProgress<double> progress, IProgress<string> status,
                           IProgress<string> fileProgress, int maxThreads)
    {
        _baseDir = baseDir;
        _log = log;
        _progress = progress;
        _status = status;
        _fileProgress = fileProgress;
        _maxThreads = Math.Clamp(maxThreads, 1, 128);

        var handler = new SocketsHttpHandler { MaxConnectionsPerServer = _maxThreads };
        _http = new HttpClient(handler);
        _http.DefaultRequestHeaders.Add("User-Agent", "MC-Launcher/0.1");
        _http.Timeout = TimeSpan.FromMinutes(10);
    }

    public async Task VerifyVersionAsync(string versionId, CancellationToken ct)
    {
        _status.Report("正在获取版本清单，准备校验...");
        var detail = await GetVersionDetailAsync(versionId, ct);
        var (baseTasks, assetTasks) = await GetAllTasksAsync(detail, versionId, ct);

        var allTasks = new List<(string Url, string Path, string? Sha1)>();
        allTasks.AddRange(baseTasks);
        allTasks.AddRange(assetTasks);

        int total = allTasks.Count;
        int done = 0;
        int corruptedCount = 0;
        var corruptedFiles = new List<string>();
        var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = _maxThreads };

        _status.Report("正在校验文件完整性...");
        _log.Report($"开始校验 {total} 个文件...");

        await Parallel.ForEachAsync(allTasks, options, async (t, token) =>
        {
            bool isOk = true;
            if (!File.Exists(t.Path)) isOk = false;
            else if (t.Sha1 != null)
            {
                var actual = await ComputeSha1Async(t.Path);
                if (!string.Equals(actual, t.Sha1, StringComparison.OrdinalIgnoreCase)) isOk = false;
            }

            if (!isOk)
            {
                Interlocked.Increment(ref corruptedCount);
                lock (corruptedFiles) { corruptedFiles.Add(Path.GetFileName(t.Path)); }
            }

            int current = Interlocked.Increment(ref done);
            _fileProgress.Report($"{current} / {total} (校验)");
            _progress.Report((double)current / total);
        });

        _progress.Report(1.0);
        _status.Report("校验完成");

        if (corruptedCount == 0) _log.Report("✅ 校验通过！所有文件完好无损。");
        else
        {
            _log.Report($"⚠️ 发现 {corruptedCount} 个文件缺失或损坏：");
            foreach (var f in corruptedFiles.Take(10)) _log.Report($"   - {f}");
            if (corruptedCount > 10) _log.Report("   - ... (仅显示前 10 个)");
            _log.Report("请重新点击【开始下载】进行修复。");
        }
    }

    public async Task DownloadVersionAsync(string versionId, CancellationToken ct)
    {
        _status.Report("正在获取版本详情...");
        var detail = await GetVersionDetailAsync(versionId, ct);
        var (baseTasks, assetTasks) = await GetAllTasksAsync(detail, versionId, ct);

        _status.Report("正在下载基础文件...");
        int baseTotal = baseTasks.Count;
        int baseDone = 0;
        var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = _maxThreads };

        await Parallel.ForEachAsync(baseTasks, options, async (t, token) =>
        {
            await DownloadFileAsync(t.Url, t.Path, t.Sha1, token);
            int current = Interlocked.Increment(ref baseDone);
            _fileProgress.Report($"{current} / {baseTotal} (基础)");
            _progress.Report(0.1 * current / baseTotal);
        });

        if (assetTasks.Count > 0)
        {
            _status.Report("正在多线程下载资源文件...");
            _log.Report($"资源对象 {assetTasks.Count} 个，并发线程: {_maxThreads}");
            int assetTotal = assetTasks.Count;
            int assetDone = 0;

            await Parallel.ForEachAsync(assetTasks, options, async (t, token) =>
            {
                await DownloadFileAsync(t.Url, t.Path, t.Sha1, token);
                int current = Interlocked.Increment(ref assetDone);
                _fileProgress.Report($"{current} / {assetTotal} (资源)");
                _progress.Report(0.1 + 0.9 * current / assetTotal);
            });
        }

        _progress.Report(1.0);
        _status.Report("下载完成");
    }

    private async Task<VersionDetail> GetVersionDetailAsync(string versionId, CancellationToken ct)
    {
        var manifestJson = await _http.GetStringAsync(ManifestUrl, ct);
        var manifest = JsonSerializer.Deserialize<VersionManifest>(manifestJson, JsonOpts)
            ?? throw new Exception("版本清单解析失败");

        var versionEntry = manifest.Versions.FirstOrDefault(v => v.Id == versionId);

        // 如果官方清单里没有（比如 Forge 或 Fabric 版本），直接读本地 JSON
        if (versionEntry == null)
        {
            var localJsonPath = Path.Combine(_baseDir, "versions", versionId, $"{versionId}.json");
            if (File.Exists(localJsonPath))
            {
                var localJson = await File.ReadAllTextAsync(localJsonPath, ct);
                return JsonSerializer.Deserialize<VersionDetail>(localJson, JsonOpts) ?? throw new Exception("本地版本详情解析失败");
            }
            throw new Exception($"找不到版本: {versionId}");
        }

        _log.Report($"获取版本 {versionId} 详情...");
        var versionJson = await _http.GetStringAsync(versionEntry.Url, ct);
        var detail = JsonSerializer.Deserialize<VersionDetail>(versionJson, JsonOpts)
            ?? throw new Exception("版本详情解析失败");

        var versionDir = Path.Combine(_baseDir, "versions", versionId);
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, $"{versionId}.json"), versionJson, ct);

        return detail;
    }

    private async Task<(List<(string Url, string Path, string? Sha1)> baseTasks, List<(string Url, string Path, string? Sha1)> assetTasks)>
        GetAllTasksAsync(VersionDetail detail, string versionId, CancellationToken ct)
    {
        var allBaseTasks = new List<(string Url, string Path, string? Sha1)>();
        var assetTasks = new List<(string Url, string Path, string? Sha1)>();

        var versionChain = new List<VersionDetail>();
        VersionDetail? current = detail;
        while (current != null)
        {
            versionChain.Add(current);
            if (string.IsNullOrEmpty(current.InheritsFrom)) break;

            var parentJsonPath = Path.Combine(_baseDir, "versions", current.InheritsFrom, $"{current.InheritsFrom}.json");
            if (File.Exists(parentJsonPath))
            {
                var parentJson = await File.ReadAllTextAsync(parentJsonPath, ct);
                current = JsonSerializer.Deserialize<VersionDetail>(parentJson, JsonOpts);
            }
            else
            {
                _log.Report($"本地未找到父版本 {current.InheritsFrom} 的 JSON，正在从网络获取...");
                current = await GetVersionDetailAsync(current.InheritsFrom, ct);
            }
        }

        versionChain.Reverse(); // 原版在前，子版本在后
        var addedLibs = new HashSet<string>();

        foreach (var ver in versionChain)
        {
            if (ver.Downloads?.Client != null)
            {
                var jarPath = Path.Combine(_baseDir, "versions", ver.Id, $"{ver.Id}.jar");
                allBaseTasks.Add((ver.Downloads.Client.Url, jarPath, ver.Downloads.Client.Sha1));
            }

            foreach (var lib in ver.Libraries.Where(IsAllowed))
            {
                if (lib.Downloads?.Artifact == null) continue;
                var a = lib.Downloads.Artifact;
                if (addedLibs.Add(lib.Name))
                {
                    allBaseTasks.Add((a.Url, Path.Combine(_baseDir, "libraries", a.Path), a.Sha1));
                }
            }

            if (ver.AssetIndex != null && assetTasks.Count == 0)
            {
                var idxPath = Path.Combine(_baseDir, "assets", "indexes", $"{ver.AssetIndex.Id}.json");
                allBaseTasks.Add((ver.AssetIndex.Url, idxPath, ver.AssetIndex.Sha1));

                if (File.Exists(idxPath))
                {
                    var idxJson = await File.ReadAllTextAsync(idxPath, ct);
                    var idx = JsonSerializer.Deserialize<AssetIndex>(idxJson, JsonOpts);
                    if (idx?.Objects != null)
                    {
                        foreach (var obj in idx.Objects.Values)
                        {
                            var hash = obj.Hash;
                            var prefix = hash[..2];
                            var objPath = Path.Combine(_baseDir, "assets", "objects", prefix, hash);
                            assetTasks.Add(($"{ResourcesBaseUrl}/{prefix}/{hash}", objPath, hash));
                        }
                    }
                }
            }
        }

        return (allBaseTasks, assetTasks);
    }

    private async Task DownloadFileAsync(string url, string path, string? expectedSha1, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (File.Exists(path) && expectedSha1 != null)
        {
            var existSha1 = await ComputeSha1Async(path);
            if (string.Equals(existSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
                return;
        }

        var tempPath = path + ".tmp";
        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using (var fs = File.Create(tempPath))
                await resp.Content.CopyToAsync(fs, ct);

            if (expectedSha1 != null)
            {
                var actual = await ComputeSha1Async(tempPath);
                if (!string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempPath);
                    throw new Exception($"SHA1 校验失败: {Path.GetFileName(path)}");
                }
            }
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private static async Task<string> ComputeSha1Async(string path)
    {
        using var sha1 = SHA1.Create();
        await using var fs = File.OpenRead(path);
        var hash = await sha1.ComputeHashAsync(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
