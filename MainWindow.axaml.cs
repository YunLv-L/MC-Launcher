using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MCLauncher;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;
    private AppSettings _settings;

    public MainWindow()
    {
        InitializeComponent();

        // 启动时加载配置
        _settings = AppSettings.Load();
        ThreadInput.Value = _settings.MaxThreads;
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

        var version = VersionInput.Text?.Trim();
        if (string.IsNullOrEmpty(version))
        {
            AppendLog("请输入版本号");
            return;
        }

        _cts = new CancellationTokenSource();
        DownloadButton.Content = "取消";
        VerifyButton.IsEnabled = false; // 下载时禁用校验
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
            VerifyButton.IsEnabled = true; // 恢复校验按钮
        }
    }

    private async void OnVerifyClick(object? sender, RoutedEventArgs e)
    {
        if (_cts != null) return; // 如果正在下载，不允许校验

        var version = VersionInput.Text?.Trim();
        if (string.IsNullOrEmpty(version))
        {
            AppendLog("请输入版本号");
            return;
        }

        _cts = new CancellationTokenSource();
        VerifyButton.IsEnabled = false;
        DownloadButton.IsEnabled = false; // 校验时禁用下载
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
        catch (OperationCanceledException)
        {
            AppendLog("⚠️ 校验已取消");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ 校验失败: {ex.Message}");
        }
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
        var version = VersionInput.Text?.Trim();
        var username = UsernameInput.Text?.Trim();
        var javaPath = JavaPathInput.Text?.Trim();

        if (string.IsNullOrEmpty(version))
        {
            AppendLog("请输入版本号");
            return;
        }

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

    private void AppendLog(string message)
    {
        LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
