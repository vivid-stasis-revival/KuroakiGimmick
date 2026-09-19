using System.Diagnostics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Core;

/// <summary>
/// 只负责发现 FFmpeg 可执行文件：优先显式环境变量，其次程序旁工具目录及常见安装位置。
/// 不启动进程；音频解码和视频编码的进程生命周期分别由 AudioData 与 VideoExport 管理。
/// </summary>
public static class Ffmpeg
{
    public static string Find()
    {
        var configured = Environment.GetEnvironmentVariable("KUROAKI_FFMPEG") ?? Environment.GetEnvironmentVariable("SCARLET_FFMPEG");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }
        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var p in new[]
        {
            Path.Combine(AppContext.BaseDirectory, exe),
            Path.Combine(AppContext.BaseDirectory, "tools", exe),
            "/opt/homebrew/bin/ffmpeg",
            "/usr/local/bin/ffmpeg"
        })
        {
            if (File.Exists(p))
            {
                return p;
            }
        }
        return exe;
    }
}

