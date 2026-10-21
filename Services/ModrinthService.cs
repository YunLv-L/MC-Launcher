using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCLauncher;

public class ModrinthService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ModrinthService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "MC-Launcher/0.1");
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<List<ModSearchResult>> SearchModsAsync(string query, string mcVersion, string loader)
    {
        var lowerQuery = (query ?? "").ToLowerInvariant();
        var lowerLoader = (loader ?? "").ToLowerInvariant();

        // 🌟 核心修复：改用 POST 请求，将参数放入 JSON Body，彻底避开 URL 编码地狱
        var urlString = "https://api.modrinth.com/v2/search";

        var requestBody = new
        {
            query = lowerQuery,
            facets = new List<List<string>>
            {
                new() { "project_type:mod" },
                new() { $"versions:{mcVersion}" },
                new() { $"loaders:{lowerLoader}" }
            },
            limit = 30
        };

        var jsonBody = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http.PostAsync(urlString, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ModrinthSearchResponse>(json, JsonOpts);
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

        // 获取下载直链的 GET 请求
        var urlString = $"https://api.modrinth.com/v2/project/{mod.ProjectId}/version?game_versions=[\"{mcVersion}\"]&loaders=[\"{lowerLoader}\"]";

        // 依然使用 DangerousDisablePathAndQueryCanonicalization 防止底层乱改
        var uri = new Uri(urlString, new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true });

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var versions = JsonSerializer.Deserialize<List<ModrinthVersion>>(json, JsonOpts);

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
