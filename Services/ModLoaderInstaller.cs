using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCLauncher;

public class ModLoaderInstaller
{
    private readonly string _baseDir;
    private readonly IProgress<string> _log;
    private readonly HttpClient _http;

    public ModLoaderInstaller(string baseDir, IProgress<string> log)
    {
        _baseDir = baseDir;
        _log = log;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "MC-Launcher/0.1");
    }

    public async Task<string> InstallFabricAsync(string mcVersion, string? specificLoaderVersion = null)
    {
        _log.Report("正在获取 Fabric 元数据...");
        var url = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}";
        var json = await _http.GetStringAsync(url);
        var versions = JsonSerializer.Deserialize<List<FabricLoaderEntry>>(json);
        if (versions == null || versions.Count == 0) throw new Exception("未找到可用的 Fabric Loader 版本");

        var entry = string.IsNullOrEmpty(specificLoaderVersion)
            ? versions[0]
            : versions.FirstOrDefault(v => v.Loader.Version == specificLoaderVersion)
              ?? throw new Exception($"未找到指定的 Fabric Loader 版本: {specificLoaderVersion}");

        var loaderVersion = entry.Loader.Version;
        var versionId = $"fabric-loader-{loaderVersion}-{mcVersion}";
        _log.Report($"准备安装 Fabric: {versionId}");

        var fabricJson = new VersionDetail
        {
            Id = versionId,
            InheritsFrom = mcVersion,
            MainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
            Libraries = new List<Library>()
        };

        if (entry.LauncherMeta?.Libraries?.Client != null)
        {
            foreach (var lib in entry.LauncherMeta.Libraries.Client)
            {
                fabricJson.Libraries.Add(new Library
                {
                    Name = lib.Name,
                    Downloads = new LibraryDownloads
                    {
                        Artifact = new Artifact
                        {
                            Path = MavenToPath(lib.Name),
                            Url = lib.Url,
                            Sha1 = lib.Sha1 ?? ""
                        }
                    }
                });
            }
        }

        var versionDir = Path.Combine(_baseDir, "versions", versionId);
        Directory.CreateDirectory(versionDir);
        var jsonPath = Path.Combine(versionDir, $"{versionId}.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(fabricJson, new JsonSerializerOptions { WriteIndented = true }));

        _log.Report($"Fabric 版本 JSON 已写入: {jsonPath}");
        return versionId;
    }

    public async Task<string> InstallForgeAsync(string mcVersion, string forgeVersion, string javaPath)
    {
        var forgeFullVersion = $"{mcVersion}-{forgeVersion}";
        var installerJarName = $"forge-{forgeFullVersion}-installer.jar";
        var installerUrl = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{forgeFullVersion}/{installerJarName}";
        var installerPath = Path.Combine(_baseDir, installerJarName);

        if (!File.Exists(installerPath))
        {
            _log.Report($"正在下载 Forge Installer: {installerJarName}");
            var bytes = await _http.GetByteArrayAsync(installerUrl);
            await File.WriteAllBytesAsync(installerPath, bytes);
            _log.Report("Forge Installer 下载完成。");
        }

        _log.Report($"正在启动 Forge 安装程序 (静默模式)...");
        var psi = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = $"-jar \"{installerPath}\" --installClient \"{_baseDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _baseDir
        };

        using var process = Process.Start(psi);
        if (process == null) throw new Exception("无法启动 Java 进程");

        process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _log.Report($"[Forge] {e.Data}"); };
        process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _log.Report($"[Forge Error] {e.Data}"); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception($"Forge 安装程序退出码: {process.ExitCode}");

        _log.Report("Forge 安装程序执行完毕。");

        var expectedVersionId = $"{mcVersion}-forge-{forgeVersion}";
        var expectedJsonPath = Path.Combine(_baseDir, "versions", expectedVersionId, $"{expectedVersionId}.json");

        if (!File.Exists(expectedJsonPath))
            throw new Exception($"Forge 安装似乎未成功，未找到版本 JSON: {expectedJsonPath}");

        return expectedVersionId;
    }

    private static string MavenToPath(string maven)
    {
        var parts = maven.Split(':');
        if (parts.Length < 3) return maven;
        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        return $"{group}/{artifact}/{version}/{artifact}-{version}.jar";
    }
}
