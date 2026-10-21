using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MCLauncher;

public class JavaService
{
    public async Task<List<string>> FindJavaInstallationsAsync()
    {
        var results = new HashSet<string>();

        // 1. 检查环境变量 JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var path = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(path)) results.Add(path);
        }

        // 2. 扫描常见安装目录 (Adoptium, Oracle, etc.)
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        await Task.Run(() =>
        {
            var searchDirs = new[]
            {
                Path.Combine(programFiles, "Java"),
                Path.Combine(programFiles, "Eclipse Adoptium"),
                Path.Combine(programFilesX86, "Java"),
                Path.Combine(programFiles, "Microsoft", "jdk")
            };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var exe in Directory.GetFiles(dir, "java.exe", SearchOption.AllDirectories))
                    {
                        if (exe.Contains("bin")) results.Add(exe);
                    }
                }
                catch { }
            }
        });

        // 3. 测试系统环境变量里的 "java"
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = "-version",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var err = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode == 0 || err.Contains("version")) results.Add("java");
            }
        }
        catch { }

        return new List<string>(results);
    }
}
