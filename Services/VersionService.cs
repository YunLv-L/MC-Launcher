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
    }

    public async Task<List<string>> GetAvailableVersionsAsync()
    {
        var versions = new HashSet<string>();

        // 1. 获取本地已安装的版本（包含 Forge/Fabric 等）
        var versionsDir = Path.Combine(_baseDir, "versions");
        if (Directory.Exists(versionsDir))
        {
            foreach (var dir in Directory.GetDirectories(versionsDir))
            {
                var versionId = Path.GetFileName(dir);
                if (File.Exists(Path.Combine(dir, $"{versionId}.json")))
                {
                    versions.Add(versionId);
                }
            }
        }

        // 2. 获取原版在线版本
        try
        {
            var manifestJson = await _http.GetStringAsync(ManifestUrl);
            var manifest = JsonSerializer.Deserialize<VersionManifest>(manifestJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest?.Versions != null)
            {
                foreach (var v in manifest.Versions)
                {
                    versions.Add(v.Id);
                }
            }
        }
        catch
        {
            // 如果断网，则只显示本地版本
        }

        // 按照版本号倒序排列（最新的在前）
        return versions.OrderByDescending(v => v).ToList();
    }
}
