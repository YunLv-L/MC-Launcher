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
        FileCountText.Text = "";
        LogList.Items.Clear();

        var baseDir = Path.Combine(AppContext.BaseDirectory, "minecraft");
        var log = new Progress<string>(AppendLog);
        var progress = new Progress<double>(p => ProgressBar.Value = p * 100);
        var status = new Progress<string>(s => StatusText.Text = s);
        // 新增：用于更新文件数的回调
        var fileProgress = new Progress<string>(s => FileCountText.Text = s);

        try
        {
            // 将 fileProgress 传进去
            var service = new DownloadService(baseDir, log, progress, status, fileProgress);
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
        }
    }

    private void AppendLog(string message)
    {
        LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}