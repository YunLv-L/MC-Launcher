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
        // facets 格式: [["project_type:mod"],["versions:1.20.1"],["categories:forge"]]
        var facets = $"[[\"project_type:mod\"],[\"versions:{mcVersion}\"],[\"categories:{loader}\"]]";
        var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facets)}&limit=20";

        var json = await _http.GetStringAsync(url);
        var result = JsonSerializer.Deserialize<ModrinthSearchResponse>(json);
        return result?.Hits ?? new List<ModSearchResult>();
    }

    public async Task DownloadModAsync(ModSearchResult mod, string mcVersion, string loader, string modsDir)
    {
        // 获取下载直链
        var url = $"https://api.modrinth.com/v2/project/{mod.ProjectId}/version?game_versions=[\"{mcVersion}\"]&loaders=[\"{loader}\"]";
        var json = await _http.GetStringAsync(url);
        var versions = JsonSerializer.Deserialize<List<ModrinthVersion>>(json);

        if (versions == null || versions.Count == 0) throw new Exception("未找到适配该版本的下载");

        var latest = versions[0]; // 取最新版
        var file = latest.Files[0]; // 取主文件

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
