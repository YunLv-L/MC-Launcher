using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCLauncher;

public class VersionService
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private readonly HttpClient _http;
    private readonly string _baseDir;

    public VersionService(string baseDir)
    {
        _baseDir = baseDir;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "MC-Launcher/0.1");
        _http.Timeout = TimeSpan.FromSeconds(15); // 加上超时，防止界面卡死
    }

    public async Task<List<string>> GetAvailableVersionsAsync()
    {
        var versions = new List<string>();
        var seen = new HashSet<string>();

        // 1. 获取本地已安装的版本（优先显示）
        var versionsDir = Path.Combine(_baseDir, "versions");
        if (Directory.Exists(versionsDir))
        {
            foreach (var dir in Directory.GetDirectories(versionsDir))
            {
                var versionId = Path.GetFileName(dir);
                if (File.Exists(Path.Combine(dir, $"{versionId}.json")) && seen.Add(versionId))
                {
                    versions.Add(versionId);
                }
            }
        }

        // 2. 获取原版在线版本
        // 注意：这里去掉了 try-catch，如果网络不通会直接抛异常，由 UI 层处理
        var manifestJson = await _http.GetStringAsync(ManifestUrl);
        var manifest = JsonSerializer.Deserialize<VersionManifest>(manifestJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (manifest?.Versions != null)
        {
            // 官方返回的顺序就是新到旧，不需要额外排序
            foreach (var v in manifest.Versions)
            {
                // 只保留正式版 (release)
                if (v.Type == "release" && seen.Add(v.Id))
                {
                    versions.Add(v.Id);
                }
            }
        }

        return versions;
    }
}
