using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KuroakiGimmick.Core;

/// <summary>
/// 把一张 PNG 放进系统剪贴板。SDL 的剪贴板绑定只有文本，图片要走各平台自己的路：
/// macOS 用 osascript 读成 «class PNGf»，Windows 用 PowerShell 的 Clipboard.SetImage。
/// 失败时不抛错，只返回错误文本，由调用方退回"已保存到文件"的提示。
/// </summary>
public static class ImageClipboard
{
    /// <summary>本平台是否可能支持复制图片。Linux 目前没有交互式图形后端，这里也不假装能做。</summary>
    public static bool Supported => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    /// <summary>
    /// 复制成功返回 null，否则返回可显示的失败原因。图片先落到临时文件再交给系统工具 ——
    /// 这两个平台的剪贴板 API 都不接受管道进来的字节。
    /// </summary>
    public static string? CopyPng(byte[] png)
    {
        if (!Supported)
        {
            return "Copying images is not supported on this platform.";
        }
        string temp = Path.Combine(Path.GetTempPath(), "kuroaki-card-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(temp, png);
            var info = OperatingSystem.IsMacOS()
                ? Script("osascript", ["-e", $"set the clipboard to (read (POSIX file \"{temp}\") as «class PNGf»)"])
                : Script("powershell", ["-NoProfile", "-STA", "-Command",
                    "Add-Type -AssemblyName System.Windows.Forms,System.Drawing; "
                    + $"$i=[System.Drawing.Image]::FromFile('{temp.Replace("'", "''")}'); "
                    + "[System.Windows.Forms.Clipboard]::SetImage($i); $i.Dispose()"]);
            using var process = Process.Start(info);
            if (process == null)
            {
                return "Could not start the system clipboard helper.";
            }
            string errors = process.StandardError.ReadToEnd();
            // 剪贴板持有的是数据而不是文件句柄，等它退出之后删临时文件是安全的。
            if (!process.WaitForExit(15000))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { /* 已退出 */ }
                return "The system clipboard helper timed out.";
            }
            return process.ExitCode == 0 ? null
                : errors.Trim() is { Length: > 0 } text ? text : "Clipboard helper failed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return ex.Message;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 临时文件留着也无妨 */ }
        }
    }

    static ProcessStartInfo Script(string program, string[] arguments)
    {
        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string a in arguments)
        {
            info.ArgumentList.Add(a);
        }
        return info;
    }
}
