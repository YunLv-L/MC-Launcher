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

    public DownloadService(string baseDir, IProgress<string> log,
                           IProgress<double> progress, IProgress<string> status,
                           IProgress<string> fileProgress)
    {
        _baseDir = baseDir;
        _log = log;
        _progress = progress;
        _status = status;
        _fileProgress = fileProgress;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "MC-Launcher/0.1");
        _http.Timeout = TimeSpan.FromMinutes(10);
    }

    public async Task DownloadVersionAsync(string versionId, CancellationToken ct)
    {
        Directory.CreateDirectory(_baseDir);

        _status.Report("正在获取版本清单...");
        _log.Report("获取版本清单...");
        var manifestJson = await _http.GetStringAsync(ManifestUrl, ct);
        var manifest = JsonSerializer.Deserialize<VersionManifest>(manifestJson, JsonOpts)
            ?? throw new Exception("版本清单解析失败");

        var versionEntry = manifest.Versions.FirstOrDefault(v => v.Id == versionId)
            ?? throw new Exception($"找不到版本: {versionId}");

        _status.Report($"正在获取版本 {versionId} 的详细信息...");
        _log.Report($"获取版本 {versionId} 详情...");
        var versionJson = await _http.GetStringAsync(versionEntry.Url, ct);
        var detail = JsonSerializer.Deserialize<VersionDetail>(versionJson, JsonOpts)
            ?? throw new Exception("版本详情解析失败");

        var versionDir = Path.Combine(_baseDir, "versions", versionId);
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, $"{versionId}.json"), versionJson, ct);

        // --- 收集基础文件任务 ---
        var baseTasks = new List<(string Url, string Path, string? Sha1)>();
        if (detail.Downloads?.Client != null)
        {
            baseTasks.Add((detail.Downloads.Client.Url,
                       Path.Combine(versionDir, $"{versionId}.jar"),
                       detail.Downloads.Client.Sha1));
        }
        foreach (var lib in detail.Libraries.Where(IsAllowed))
        {
            if (lib.Downloads?.Artifact == null) continue;
            var a = lib.Downloads.Artifact;
            baseTasks.Add((a.Url, Path.Combine(_baseDir, "libraries", a.Path), a.Sha1));
        }
        
        string? idxPath = null;
        if (detail.AssetIndex != null)
        {
            idxPath = Path.Combine(_baseDir, "assets", "indexes", $"{detail.AssetIndex.Id}.json");
            baseTasks.Add((detail.AssetIndex.Url, idxPath, detail.AssetIndex.Sha1));
        }

        // 1. 下载基础文件
        _status.Report("正在下载基础文件...");
        int baseTotal = baseTasks.Count;
        int baseDone = 0;
        
        foreach (var t in baseTasks)
        {
            ct.ThrowIfCancellationRequested();
            _fileProgress.Report($"{baseDone} / {baseTotal} (基础)");
            await DownloadFileAsync(t.Url, t.Path, t.Sha1, ct);
            baseDone++;
            // 基础文件占前 10% 的进度条
            _progress.Report(0.1 * baseDone / baseTotal);
        }
        _fileProgress.Report($"{baseTotal} / {baseTotal} (基础)");

        // 2. 解析资源并下载
        if (!string.IsNullOrEmpty(idxPath) && File.Exists(idxPath))
        {
            var idxJson = await File.ReadAllTextAsync(idxPath, ct);
            var idx = JsonSerializer.Deserialize<AssetIndex>(idxJson, JsonOpts);
            if (idx?.Objects != null && idx.Objects.Count > 0)
            {
                _status.Report("正在下载资源文件...");
                var objs = idx.Objects.Values.ToList();
                _log.Report($"资源对象 {objs.Count} 个");

                int assetTotal = objs.Count;
                int assetDone = 0;
                
                foreach (var obj in objs)
                {
                    ct.ThrowIfCancellationRequested();
                    var hash = obj.Hash;
                    var prefix = hash[..2];
                    var objPath = Path.Combine(_baseDir, "assets", "objects", prefix, hash);
                    
                    await DownloadFileAsync($"{ResourcesBaseUrl}/{prefix}/{hash}", objPath, hash, ct);
                    
                    assetDone++;
                    // 资源文件更新数量和进度条（占后 90%）
                    _fileProgress.Report($"{assetDone} / {assetTotal} (资源)");
                    _progress.Report(0.1 + 0.9 * assetDone / assetTotal);
                }
            }
        }

        _progress.Report(1.0);
        _status.Report("下载完成");
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