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

        // 异步加载版本列表
        _ = LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync(string? selectVersion = null)
    {
        try
        {
            var currentSelection = selectVersion ?? (VersionComboBox.SelectedItem as string);
            var versions = await _versionService.GetAvailableVersionsAsync();

            VersionComboBox.ItemsSource = versions;

            if (!string.IsNullOrEmpty(currentSelection) && versions.Contains(currentSelection))
            {
                VersionComboBox.SelectedItem = currentSelection;
            }
            else if (versions.Count > 0)
            {
                VersionComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"❌ 获取版本列表失败: {ex.Message}");
        }
    }

    private async void OnRefreshVersionsClick(object? sender, RoutedEventArgs e)
    {
        RefreshVersionsButton.IsEnabled = false;
        AppendLog("正在刷新版本列表...");
        await LoadVersionsAsync();
        AppendLog("版本列表已刷新。");
        RefreshVersionsButton.IsEnabled = true;
    }

    private string GetSelectedVersion()
    {
        return VersionComboBox.SelectedItem as string ?? VersionComboBox.Text ?? "";
    }

    private void OnSaveSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (ThreadInput.Value.HasValue)
        {
            _settings.MaxThreads = (int)ThreadInput.Value.Value;
            _settings.Save();
            AppendLog($"⚙️ 设置已保存，线程数: {_settings.MaxThreads}");
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
            AppendLog("请选择或输入版本号");
            return;
        }

        _cts = new CancellationTokenSource();
        DownloadButton.Content = "取消";
        VerifyButton.IsEnabled = false;
        ProgressBar.Value = 0;
        FileCountText.Text = "";
        LogList.Items.Clear();

        var baseDir = Path.Combine(AppContext.BaseDirectory, "minecraft");
        var log = new Progress<string>(AppendLog);
        var progress = new Progress<double>(p => ProgressBar.Value = p * 100);
        var status = new Progress<string>(s => StatusText.Text = s);
        var fileProgress = new Progress<string>(s => FileCountText.Text = s);

        try
        {
            var service = new DownloadService(baseDir, log, progress, status, fileProgress, _settings.MaxThreads);
            await service.DownloadVersionAsync(version, _cts.Token);
            AppendLog("✅ 下载完成！");
            FileCountText.Text = "";
            StatusText.Text = "下载完成";
        }
        catch (OperationCanceledException)
        {
            AppendLog("⚠️ 下载已取消");
            StatusText.Text = "已取消";
            FileCountText.Text = "";
        }
        catch (Exception ex)
        {
            AppendLog($"❌ 错误: {ex.Message}");
            StatusText.Text = "下载失败";
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
        if (string.IsNullOrEmpty(version)) { AppendLog("请选择或输入版本号"); return; }

        _cts = new CancellationTokenSource();
        VerifyButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        ProgressBar.Value = 0;
        FileCountText.Text = "";
        LogList.Items.Clear();

        var baseDir = Path.Combine(AppContext.BaseDirectory, "minecraft");
        var log = new Progress<string>(AppendLog);
        var progress = new Progress<double>(p => ProgressBar.Value = p * 100);
        var status = new Progress<string>(s => StatusText.Text = s);
        var fileProgress = new Progress<string>(s => FileCountText.Text = s);

        try
        {
            var service = new DownloadService(baseDir, log, progress, status, fileProgress, _settings.MaxThreads);
            await service.VerifyVersionAsync(version, _cts.Token);
        }
        catch (OperationCanceledException) { AppendLog("⚠️ 校验已取消"); }
        catch (Exception ex) { AppendLog($"❌ 校验失败: {ex.Message}"); }
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

        if (string.IsNullOrEmpty(version)) { AppendLog("请选择或输入版本号"); return; }

        LaunchButton.IsEnabled = false;
        var baseDir = Path.Combine(AppContext.BaseDirectory, "minecraft");
        var log = new Progress<string>(AppendLog);
        var status = new Progress<string>(s => StatusText.Text = s);

        try
        {
            var service = new LaunchService(baseDir, log, status);
            await service.LaunchAsync(version, username ?? "Steve", javaPath ?? "java");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ 启动失败: {ex.Message}");
        }
        finally
        {
            LaunchButton.IsEnabled = true;
        }
    }

    // ================== 安装按钮事件 ==================

    private async void OnInstallFabricClick(object? sender, RoutedEventArgs e)
    {
        var mcVersion = GetSelectedVersion();
        // 防止用户在下拉框里选了 Forge 版本去装 Fabric
        if (string.IsNullOrEmpty(mcVersion) || mcVersion.Contains("-forge-") || mcVersion.Contains("fabric-loader"))
        {
            AppendLog("请先在主页选择一个【纯原版】的 Minecraft 版本（如 1.20.1）");
            return;
        }

        var loaderVersion = FabricLoaderVersionInput.Text?.Trim();
        InstallFabricButton.IsEnabled = false;
        AppendLog($"开始安装 Fabric (基于 MC: {mcVersion})...");

        try
        {
            var installer = new ModLoaderInstaller(Path.Combine(AppContext.BaseDirectory, "minecraft"), new Progress<string>(AppendLog));
            var fabricVersionId = await installer.InstallFabricAsync(mcVersion, loaderVersion);
            AppendLog($"✅ Fabric 安装成功！版本 ID: {fabricVersionId}");

            // 安装成功后刷新列表，并自动选中新版本
            await LoadVersionsAsync(fabricVersionId);
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Fabric 安装失败: {ex.Message}");
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
            AppendLog("请先在主页选择一个【纯原版】的 Minecraft 版本（如 1.20.1）");
            return;
        }

        if (string.IsNullOrEmpty(forgeVersion))
        {
            AppendLog("请输入 Forge 版本 (如 47.2.0)");
            return;
        }

        InstallForgeButton.IsEnabled = false;
        AppendLog($"开始安装 Forge {mcVersion}-{forgeVersion}...");

        try
        {
            var installer = new ModLoaderInstaller(Path.Combine(AppContext.BaseDirectory, "minecraft"), new Progress<string>(AppendLog));
            var javaPath = JavaPathInput.Text?.Trim() ?? "java";
            var forgeVersionId = await installer.InstallForgeAsync(mcVersion, forgeVersion, javaPath);
            AppendLog($"✅ Forge 安装成功！版本 ID: {forgeVersionId}");

            // 安装成功后刷新列表，并自动选中新版本
            await LoadVersionsAsync(forgeVersionId);
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Forge 安装失败: {ex.Message}");
        }
        finally
        {
            InstallForgeButton.IsEnabled = true;
        }
    }

    private void AppendLog(string message)
    {
        LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
