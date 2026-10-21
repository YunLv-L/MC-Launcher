using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCLauncher;

public class ModrinthService
{
    private readonly HttpClient _http;

    public ModrinthService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "MC-Launcher/0.1");
    }

    public async Task<List<ModSearchResult>> SearchModsAsync(string query, string mcVersion, string loader)
    {
        var lowerQuery = (query ?? "").ToLowerInvariant();
        var lowerLoader = (loader ?? "").ToLowerInvariant();

        // 🌟 核心修复：将 categories 改为 loaders
        var facets = $"[[\"project_type:mod\"],[\"versions:{mcVersion}\"],[\"loaders:{lowerLoader}\"]]";

        var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(lowerQuery)}&facets={Uri.EscapeDataString(facets)}&limit=30";

        try
        {
            var json = await _http.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<ModrinthSearchResponse>(json);
            return result?.Hits ?? new List<ModSearchResult>();
        }
        catch (Exception ex)
        {
            throw new Exception($"Modrinth API 请求失败: {ex.Message}");
        }
    }

    public async Task DownloadModAsync(ModSearchResult mod, string mcVersion, string loader, string modsDir)
    {
        var lowerLoader = (loader ?? "").ToLowerInvariant();

        var url = $"https://api.modrinth.com/v2/project/{mod.ProjectId}/version?game_versions=[\"{mcVersion}\"]&loaders=[\"{lowerLoader}\"]";

        var json = await _http.GetStringAsync(url);
        var versions = JsonSerializer.Deserialize<List<ModrinthVersion>>(json);

        if (versions == null || versions.Count == 0)
            throw new Exception($"该模组暂不支持 Minecraft {mcVersion} 的 {loader} 版本");

        var latest = versions[0];
        var file = latest.Files[0];

        Directory.CreateDirectory(modsDir);
        var filePath = Path.Combine(modsDir, file.Filename);

        var bytes = await _http.GetByteArrayAsync(file.Url);
        await File.WriteAllBytesAsync(filePath, bytes);
    }
}

// 模型定义
public class ModrinthSearchResponse
{
    public List<ModSearchResult> Hits { get; set; } = new();
}

public class ModSearchResult
{
    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconUrl { get; set; } = "";
}

public class ModrinthVersion
{
    public List<ModrinthFile> Files { get; set; } = new();
}

public class ModrinthFile
{
    public string Url { get; set; } = "";
    public string Filename { get; set; } = "";
}
