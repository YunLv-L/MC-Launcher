using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MCLauncher;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;
    private AppSettings _settings;
    private VersionService _versionService;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        ThreadInput.Value = _settings.MaxThreads;

        var baseDir = Path.Combine(AppContext.BaseDirectory, "minecraft");
        _versionService = new VersionService(baseDir);

        _ = LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync(string? selectVersion = null)
    {
        try
        {
            var currentSelection = selectVersion ?? GetSelectedVersion();
            var versions = await _versionService.GetAvailableVersionsAsync();

            VersionComboBox.ItemsSource = versions;

            if (!string.IsNullOrEmpty(currentSelection))
            {
                VersionComboBox.Text = currentSelection;
            }
            else if (versions.Count > 0)
            {
                VersionComboBox.Text = versions[0];
            }

            UpdateHomeVersionText();
            AddLog("下载", $"✅ 成功加载 {versions.Count} 个版本");
        }
        catch (Exception ex)
        {
            AddLog("下载", $"❌ 获取在线版本列表失败，仅显示本地版本。错误: {ex.Message}");
        }
    }

    private async void OnRefreshVersionsClick(object? sender, RoutedEventArgs e)
    {
        RefreshVersionsButton.IsEnabled = false;
        AddLog("下载", "正在刷新版本列表...");
        await LoadVersionsAsync();
        AddLog("下载", "版本列表已刷新。");
        RefreshVersionsButton.IsEnabled = true;
    }

    private string GetSelectedVersion()
    {
        return VersionComboBox.Text?.Trim() ?? "";
    }

    private void OnVersionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateHomeVersionText();
    }

    private void UpdateHomeVersionText()
    {
        var version = GetSelectedVersion();
        if (string.IsNullOrEmpty(version))
        {
            HomeSelectedVersionText.Text = "尚未选择版本";
        }
        else
        {
            HomeSelectedVersionText.Text = version;
        }
    }

    private void OnSaveSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (ThreadInput.Value.HasValue)
        {
            _settings.MaxThreads = (int)ThreadInput.Value.Value;
            _settings.Save();
            AddLog("主页", $"⚙️ 设置已保存，线程数: {_settings.MaxThreads}");
        }
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts = null;
            DownloadButton.Content = "开始下载";
            VerifyButton.IsEnabled = true;
            return;
        }

        var version = GetSelectedVersion();
        if (string.IsNullOrEmpty(version))
        {
            AddLog("下载", "请选择或输入版本号");
            return;
        }

        _cts = new CancellationTokenSource();
        DownloadButton.Content = "取消";
        VerifyButton.IsEnabled = false;
        ProgressBar.Value = 0;
        FileCountText.Text = "";
        DownloadLogList.Items.Clear();

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
            FileCountText.Text = "";
            DownloadStatusText.Text = "下载完成";
        }
        catch (OperationCanceledException)
        {
            AddLog("下载", "⚠️ 下载已取消");
            DownloadStatusText.Text = "已取消";
            FileCountText.Text = "";
        }
        catch (Exception ex)
        {
            AddLog("下载", $"❌ 错误: {ex.Message}");
            DownloadStatusText.Text = "下载失败";
        }
        finally
        {
            _cts = null;
            DownloadButton.Content = "开始下载";
            VerifyButton.IsEnabled = true;
        }
    }

    private async void OnVerifyClick(object? sender, RoutedEventArgs e)
    {
        if (_cts != null) return;

        var version = GetSelectedVersion();
        if (string.IsNullOrEmpty(version)) { AddLog("下载", "请选择或输入版本号"); return; }

        _cts = new CancellationTokenSource();
        VerifyButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        ProgressBar.Value = 0;
        FileCountText.Text = "";
        DownloadLogList.Items.Clear();

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
        catch (OperationCanceledException) { AddLog("下载", "⚠️ 校验已取消"); }
        catch (Exception ex) { AddLog("下载", $"❌ 校验失败: {ex.Message}"); }
        finally
        {
            _cts = null;
            VerifyButton.IsEnabled = true;
            DownloadButton.IsEnabled = true;
            FileCountText.Text = "";
        }
    }

    private async void OnLaunchClick(object? sender, RoutedEventArgs e)
    {
        var version = GetSelectedVersion();
        var username = UsernameInput.Text?.Trim();
        var javaPath = JavaPathInput.Text?.Trim();

        if (string.IsNullOrEmpty(version)) { AddLog("主页", "请先在下载页选择版本"); return; }

        LaunchButton.IsEnabled = false;
        var baseDir = Path.Combine(AppContext.BaseDirectory, "minecraft");
        var log = new Progress<string>(msg => AddLog("主页", msg));
        var status = new Progress<string>(s => StatusText.Text = s);

        try
        {
            var service = new LaunchService(baseDir, log, status);
            await service.LaunchAsync(version, username ?? "Steve", javaPath ?? "java");
        }
        catch (Exception ex)
        {
            AddLog("主页", $"❌ 启动失败: {ex.Message}");
        }
        finally
        {
            LaunchButton.IsEnabled = true;
        }
    }

    private async void OnInstallFabricClick(object? sender, RoutedEventArgs e)
    {
        var mcVersion = GetSelectedVersion();
        if (string.IsNullOrEmpty(mcVersion) || mcVersion.Contains("-forge-") || mcVersion.Contains("fabric-loader"))
        {
            AddLog("主页", "请先在下载页选择一个【纯原版】的 Minecraft 版本（如 1.20.1）");
            return;
        }

        var loaderVersion = FabricLoaderVersionInput.Text?.Trim();
        InstallFabricButton.IsEnabled = false;
        AddLog("主页", $"开始安装 Fabric (基于 MC: {mcVersion})...");

        try
        {
            var installer = new ModLoaderInstaller(Path.Combine(AppContext.BaseDirectory, "minecraft"), new Progress<string>(msg => AddLog("主页", msg)));
            var fabricVersionId = await installer.InstallFabricAsync(mcVersion, loaderVersion);
            AddLog("主页", $"✅ Fabric 安装成功！版本 ID: {fabricVersionId}");

            await LoadVersionsAsync(fabricVersionId);
        }
        catch (Exception ex)
        {
            AddLog("主页", $"❌ Fabric 安装失败: {ex.Message}");
        }
        finally
        {
            InstallFabricButton.IsEnabled = true;
        }
    }

    private async void OnInstallForgeClick(object? sender, RoutedEventArgs e)
    {
        var mcVersion = GetSelectedVersion();
        var forgeVersion = ForgeVersionInput.Text?.Trim();

        if (string.IsNullOrEmpty(mcVersion) || mcVersion.Contains("-forge-") || mcVersion.Contains("fabric-loader"))
        {
            AddLog("主页", "请先在下载页选择一个【纯原版】的 Minecraft 版本（如 1.20.1）");
            return;
        }

        if (string.IsNullOrEmpty(forgeVersion))
        {
            AddLog("主页", "请输入 Forge 版本 (如 47.2.0)");
            return;
        }

        InstallForgeButton.IsEnabled = false;
        AddLog("主页", $"开始安装 Forge {mcVersion}-{forgeVersion}...");

        try
        {
            // 这里显式使用 Task.Run 将安装过程彻底推入后台线程
            var installer = new ModLoaderInstaller(Path.Combine(AppContext.BaseDirectory, "minecraft"), new Progress<string>(msg => AddLog("主页", msg)));
            var javaPath = JavaPathInput.Text?.Trim() ?? "java";

            var forgeVersionId = await Task.Run(() => installer.InstallForgeAsync(mcVersion, forgeVersion, javaPath));

            AddLog("主页", $"✅ Forge 安装成功！版本 ID: {forgeVersionId}");

            await LoadVersionsAsync(forgeVersionId);
        }
        catch (Exception ex)
        {
            AddLog("主页", $"❌ Forge 安装失败: {ex.Message}");
        }
        finally
        {
            InstallForgeButton.IsEnabled = true;
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
