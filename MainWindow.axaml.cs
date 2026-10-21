using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MCLauncher;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;
    private AppSettings _settings;
    private VersionService _versionService;
    private JavaService _javaService;
    private ModrinthService _modrinthService;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();

        ThreadInput.Value = _settings.MaxThreads;
        MemoryInput.Value = _settings.MaxMemoryMB;
        UsernameInput.Text = _settings.LastUsername;

        var baseDir = Path.Combine(AppContext.BaseDirectory, "minecraft");
        _versionService = new VersionService(baseDir);
        _javaService = new JavaService();
        _modrinthService = new ModrinthService();

        _ = LoadVersionsAsync();
        _ = LoadJavaAsync();
    }

    private async Task LoadJavaAsync()
    {
        AddLog("主页", "正在扫描本地 Java 环境...");
        var javas = await _javaService.FindJavaInstallationsAsync();
        JavaPathComboBox.ItemsSource = javas;

        if (javas.Count > 0)
        {
            JavaPathComboBox.Text = javas[0];
        }
        else
        {
            JavaPathComboBox.Text = "java";
        }

        AddLog("主页", $"Java 扫描完成，找到 {javas.Count} 个。");
    }

    private async Task LoadVersionsAsync(string? selectVersion = null)
    {
        try
        {
            var currentSelection = selectVersion ?? GetSelectedVersion();
            var versions = await _versionService.GetAvailableVersionsAsync();
            VersionComboBox.ItemsSource = versions;

            if (!string.IsNullOrEmpty(currentSelection)) VersionComboBox.Text = currentSelection;
            else if (versions.Count > 0) VersionComboBox.Text = versions[0];

            UpdateHomeVersionText();
            AddLog("下载", $"✅ 成功加载 {versions.Count} 个版本");
        }
        catch (Exception ex)
        {
            AddLog("下载", $"❌ 获取版本失败: {ex.Message}");
        }
    }

    private async void OnRefreshVersionsClick(object? sender, RoutedEventArgs e)
    {
        RefreshVersionsButton.IsEnabled = false;
        await LoadVersionsAsync();
        RefreshVersionsButton.IsEnabled = true;
    }

    private string GetSelectedVersion() => VersionComboBox.Text?.Trim() ?? "";

    // 🌟 新增：把带加载器后缀的版本号还原为纯原版号
    private string ExtractMcVersion(string versionId)
    {
        if (string.IsNullOrEmpty(versionId)) return "";

        // 1. 处理 Forge (如 1.20.1-forge-47.2.0)
        if (versionId.Contains("-forge-"))
            return versionId.Split("-forge-")[0];

        // 2. 处理 Fabric (如 fabric-loader-0.15.11-1.20.1)
        if (versionId.Contains("fabric-loader-"))
            return versionId.Split('-').Last();

        // 3. 处理 NeoForge (如 1.20.1-neoforge-47.1.106)
        if (versionId.Contains("-neoforge-"))
            return versionId.Split("-neoforge-")[0];

        // 如果本来就是原版，直接返回
        return versionId;
    }

    private void OnVersionSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateHomeVersionText();

    private void UpdateHomeVersionText()
    {
        var version = GetSelectedVersion();
        HomeSelectedVersionText.Text = string.IsNullOrEmpty(version) ? "尚未选择版本" : version;
    }

    private void OnSaveSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (ThreadInput.Value.HasValue) _settings.MaxThreads = (int)ThreadInput.Value.Value;
        if (MemoryInput.Value.HasValue) _settings.MaxMemoryMB = (int)MemoryInput.Value.Value;
        _settings.LastUsername = UsernameInput.Text?.Trim() ?? "Steve";

        _settings.Save();
        AddLog("主页", $"⚙️ 设置已保存 (线程: {_settings.MaxThreads}, 内存: {_settings.MaxMemoryMB}MB)");
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); _cts = null; DownloadButton.Content = "开始下载"; VerifyButton.IsEnabled = true; return; }

        var version = GetSelectedVersion();
        if (string.IsNullOrEmpty(version)) { AddLog("下载", "请选择版本"); return; }

        _cts = new CancellationTokenSource();
        DownloadButton.Content = "取消";
        VerifyButton.IsEnabled = false;
        ProgressBar.Value = 0; FileCountText.Text = ""; DownloadLogList.Items.Clear();

        var baseDir = Path.Combine(AppContext.BaseDirectory, "minecraft");
        var log = new Progress<string>(msg => AddLog("下载", msg));
        var progress = new Progress<double>(p => ProgressBar.Value = p * 100);
        var status = new Progress<string>(s => DownloadStatusText.Text = s);
        var fileProgress = new Progress<string>(s => FileCountText.Text = s);

        try
        {
            var service = new DownloadService(baseDir, log, progress, status, fileProgress, _settings.MaxThreads);
            await service.DownloadVersionAsync(version, _cts.Token);
            AddLog("下载", "✅ 下载完成！");
            DownloadStatusText.Text = "下载完成";
        }
        catch (OperationCanceledException) { AddLog("下载", "⚠️ 已取消"); }
        catch (Exception ex) { AddLog("下载", $"❌ 错误: {ex.Message}"); }
        finally { _cts = null; DownloadButton.Content = "开始下载"; VerifyButton.IsEnabled = true; }
    }

    private async void OnVerifyClick(object? sender, RoutedEventArgs e)
    {
        if (_cts != null) return;
        var version = GetSelectedVersion();
        if (string.IsNullOrEmpty(version)) return;

        _cts = new CancellationTokenSource();
        VerifyButton.IsEnabled = false; DownloadButton.IsEnabled = false;

        var baseDir = Path.Combine(AppContext.BaseDirectory, "minecraft");
        var log = new Progress<string>(msg => AddLog("下载", msg));
        var progress = new Progress<double>(p => ProgressBar.Value = p * 100);
        var status = new Progress<string>(s => DownloadStatusText.Text = s);
        var fileProgress = new Progress<string>(s => FileCountText.Text = s);

        try
        {
            var service = new DownloadService(baseDir, log, progress, status, fileProgress, _settings.MaxThreads);
            await service.VerifyVersionAsync(version, _cts.Token);
        }
        catch (Exception ex) { AddLog("下载", $"❌ 校验失败: {ex.Message}"); }
        finally { _cts = null; VerifyButton.IsEnabled = true; DownloadButton.IsEnabled = true; }
    }

    private async void OnLaunchClick(object? sender, RoutedEventArgs e)
    {
        var version = GetSelectedVersion();
        var username = UsernameInput.Text?.Trim() ?? "Steve";

        // 🌟 双保险：剥离可能误带的首尾引号，防止路径报错
        var javaPath = (JavaPathComboBox.Text?.Trim() ?? "java").Trim('"');

        if (string.IsNullOrEmpty(version)) { AddLog("主页", "请选择版本"); return; }

        LaunchButton.IsEnabled = false;
        var baseDir = Path.Combine(AppContext.BaseDirectory, "minecraft");
        var log = new Progress<string>(msg => AddLog("主页", msg));
        var status = new Progress<string>(s => StatusText.Text = s);

        try
        {
            var service = new LaunchService(baseDir, log, status);
            await service.LaunchAsync(version, username, javaPath, _settings.MaxMemoryMB);
        }
        catch (Exception ex) { AddLog("主页", $"❌ 启动失败: {ex.Message}"); }
        finally { LaunchButton.IsEnabled = true; }
    }

    // ================== 安装加载器 ==================
    private async void OnInstallFabricClick(object? sender, RoutedEventArgs e)
    {
        var mcVersion = GetSelectedVersion();
        if (string.IsNullOrEmpty(mcVersion) || mcVersion.Contains("-forge-") || mcVersion.Contains("fabric-loader")) return;

        InstallFabricButton.IsEnabled = false;
        try
        {
            var installer = new ModLoaderInstaller(Path.Combine(AppContext.BaseDirectory, "minecraft"), new Progress<string>(msg => AddLog("主页", msg)));
            var fabricVersionId = await installer.InstallFabricAsync(mcVersion, FabricLoaderVersionInput.Text?.Trim());
            await LoadVersionsAsync(fabricVersionId);
        }
        catch (Exception ex) { AddLog("主页", $"❌ Fabric 安装失败: {ex.Message}"); }
        finally { InstallFabricButton.IsEnabled = true; }
    }

    private async void OnInstallForgeClick(object? sender, RoutedEventArgs e)
    {
        var mcVersion = GetSelectedVersion();
        var forgeVersion = ForgeVersionInput.Text?.Trim();
        if (string.IsNullOrEmpty(mcVersion) || string.IsNullOrEmpty(forgeVersion)) return;

        InstallForgeButton.IsEnabled = false;
        try
        {
            var installer = new ModLoaderInstaller(Path.Combine(AppContext.BaseDirectory, "minecraft"), new Progress<string>(msg => AddLog("主页", msg)));
            var javaPath = JavaPathComboBox.Text?.Trim() ?? "java";
            var forgeVersionId = await Task.Run(() => installer.InstallForgeAsync(mcVersion, forgeVersion, javaPath));
            await LoadVersionsAsync(forgeVersionId);
        }
        catch (Exception ex) { AddLog("主页", $"❌ Forge 安装失败: {ex.Message}"); }
        finally { InstallForgeButton.IsEnabled = true; }
    }

    // ================== Modrinth 模组 ==================
    private async void OnSearchModClick(object? sender, RoutedEventArgs e)
    {
        var query = ModSearchInput.Text?.Trim() ?? "";
        var rawVersion = GetSelectedVersion();

        if (string.IsNullOrEmpty(rawVersion)) { AddLog("主页", "请先在下载页选择游戏版本"); return; }

        // 🌟 核心修复：提取纯净的 MC 版本号 (例如从 1.20.1-forge-47.2.0 提取出 1.20.1)
        var pureMcVersion = ExtractMcVersion(rawVersion);
        var loader = (ModLoaderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLower() ?? "forge";

        SearchModButton.IsEnabled = false;
        AddLog("主页", $"正在搜索 Modrinth (查询: {query}, 游戏版本: {pureMcVersion}, 加载器: {loader})...");

        try
        {
            var results = await _modrinthService.SearchModsAsync(query, pureMcVersion, loader);
            ModResultsList.ItemsSource = results;
            AddLog("主页", $"搜索完成，找到 {results.Count} 个结果。");
        }
        catch (Exception ex) { AddLog("主页", $"❌ 搜索失败: {ex.Message}"); }
        finally { SearchModButton.IsEnabled = true; }
    }

    private async void OnInstallModClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ModSearchResult mod) return;

        var rawVersion = GetSelectedVersion();
        // 🌟 核心修复：下载时也要用纯净版本号
        var pureMcVersion = ExtractMcVersion(rawVersion);
        var loader = (ModLoaderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLower() ?? "forge";
        var modsDir = Path.Combine(AppContext.BaseDirectory, "minecraft", "mods");

        btn.Content = "下载中...";
        btn.IsEnabled = false;
        AddLog("主页", $"正在安装模组: {mod.Title} (匹配版本: {pureMcVersion})...");

        try
        {
            await _modrinthService.DownloadModAsync(mod, pureMcVersion, loader, modsDir);
            AddLog("主页", $"✅ 模组 {mod.Title} 安装成功！(已保存至 mods 文件夹)");
            btn.Content = "已安装";
        }
        catch (Exception ex)
        {
            AddLog("主页", $"❌ 模组安装失败: {ex.Message}");
            btn.Content = "重试";
            btn.IsEnabled = true;
        }
    }

    private void AddLog(string tab, string message)
    {
        var formatted = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogList.Items.Add(formatted);
        if (LogList.ItemCount > 300) LogList.Items.RemoveAt(0);
        DownloadLogList.Items.Add(formatted);
        if (DownloadLogList.ItemCount > 300) DownloadLogList.Items.RemoveAt(0);
    }
}
