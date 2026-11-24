// SDL 音频流 ABI，结构体布局及布尔值封送保持输入版本。

using System.Runtime.InteropServices;

namespace KuroakiGimmick.Native;

public static partial class Sdl
{
    // -------------------------------------------------------------------------
    // 音频
    // -------------------------------------------------------------------------

    /// <summary>字段顺序与 SDL_AudioSpec 一致，属于 ABI。Format 是 SDL_AudioFormat 枚举值，Frequency 单位 Hz。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioSpec
    {
        /// <summary>PCM 采样的 SDL_AudioFormat 值。</summary>
        public uint Format;
        /// <summary>每个采样帧包含的声道数。</summary>
        public int Channels;
        /// <summary>每秒采样帧数，单位为 Hz。</summary>
        public int Frequency;
    }

    // callback 传 0 表示不用回调，由上层自己 Put 数据；新开的流默认是暂停的，要靠 SDL_ResumeAudioStreamDevice 启动。
    /// <summary>打开音频设备并将其绑定到新流，初始为暂停状态。</summary>
    /// <param name="device">要打开的音频设备 ID。</param>
    /// <param name="spec">应用侧输入或输出采样的格式、声道数及频率。</param>
    /// <param name="callback">原生音频回调地址，0 表示不安装回调。</param>
    /// <param name="user">原样传给回调的用户数据。</param>
    /// <returns>音频流句柄，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_OpenAudioDeviceStream(
        uint device,
        in AudioSpec spec,
        nint callback,
        nint user);

    /// <summary>销毁音频流；通过 OpenAudioDeviceStream 打开的设备随流关闭。</summary>
    /// <param name="stream">要操作的 SDL 音频流句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroyAudioStream(
        nint stream);

    /// <summary>将输入缓冲区的数据复制到音频流队列。</summary>
    /// <param name="stream">要操作的 SDL 音频流句柄。</param>
    /// <param name="data">输入数据的首地址。</param>
    /// <param name="length">输入数据的字节数。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_PutAudioStreamData(
        nint stream,
        nint data,
        int length);

    // 返回的是尚未播放的字节数（不是帧数，也不是秒），上层据此判断缓冲深度。
    /// <summary>查询音频流中尚待处理的输入字节数。</summary>
    /// <param name="stream">要操作的 SDL 音频流句柄。</param>
    /// <returns>排队的输入字节数，失败时为 -1。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_GetAudioStreamQueued(
        nint stream);

    /// <summary>丢弃流中排队的数据。</summary>
    /// <param name="stream">要操作的 SDL 音频流句柄。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_ClearAudioStream(
        nint stream);

    /// <summary>设置相对于原采样率的播放频率比。</summary>
    /// <param name="stream">要操作的 SDL 音频流句柄。</param>
    /// <param name="ratio">相对于原采样率的倍率，范围为 0.01 至 100。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetAudioStreamFrequencyRatio(
        nint stream,
        float ratio);

    /// <summary>设置音频流转换时应用的线性增益。</summary>
    /// <param name="stream">要操作的 SDL 音频流句柄。</param>
    /// <param name="gain">应用于流的线性增益。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetAudioStreamGain(
        nint stream,
        float gain);

    /// <summary>恢复与音频流关联的设备。</summary>
    /// <param name="stream">要操作的 SDL 音频流句柄。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_ResumeAudioStreamDevice(
        nint stream);

    /// <summary>暂停与音频流关联的设备。</summary>
    /// <param name="stream">要操作的 SDL 音频流句柄。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_PauseAudioStreamDevice(
        nint stream);
}

