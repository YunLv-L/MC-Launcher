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

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts = null;
            DownloadButton.Content = "开始下载";
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
        ProgressBar.Value = 0;
        LogList.Items.Clear();

        var baseDir = Path.Combine(AppContext.BaseDirectory, "minecraft");
        var log = new Progress<string>(AppendLog);
        var progress = new Progress<double>(p => ProgressBar.Value = p * 100);
        var status = new Progress<string>(s => StatusText.Text = s);

        try
        {
            var service = new DownloadService(baseDir, log, progress, status);
            await service.DownloadVersionAsync(version, _cts.Token);
            AppendLog("✅ 下载完成！");
        }
        catch (OperationCanceledException)
        {
            AppendLog("⚠️ 下载已取消");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ 错误: {ex.Message}");
        }
        finally
        {
            _cts = null;
            DownloadButton.Content = "开始下载";
        }
    }

    private void AppendLog(string message)
    {
        LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}