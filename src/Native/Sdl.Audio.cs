using System.Runtime.InteropServices;

namespace KuroakiGimmick.Native;

/// <summary>SDL 音频流 ABI，结构体布局及布尔值封送保持输入版本。</summary>
public static partial class Sdl
{
    // -------------------------------------------------------------------------
    // 音频
    // -------------------------------------------------------------------------

    /// <summary>字段顺序与 SDL_AudioSpec 一致，属于 ABI。Format 是 SDL_AudioFormat 枚举值，Frequency 单位 Hz。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioSpec
    {
        public uint Format;
        public int Channels;
        public int Frequency;
    }

    // callback 传 0 表示不用回调，由上层自己 Put 数据；新开的流默认是暂停的，要靠 SDL_ResumeAudioStreamDevice 启动。
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_OpenAudioDeviceStream(
        uint device,
        in AudioSpec spec,
        nint callback,
        nint user);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroyAudioStream(
        nint stream);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_PutAudioStreamData(
        nint stream,
        nint data,
        int length);

    // 返回的是尚未播放的字节数（不是帧数，也不是秒），上层据此判断缓冲深度。
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_GetAudioStreamQueued(
        nint stream);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_ClearAudioStream(
        nint stream);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetAudioStreamFrequencyRatio(
        nint stream,
        float ratio);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetAudioStreamGain(
        nint stream,
        float gain);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_ResumeAudioStreamDevice(
        nint stream);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_PauseAudioStreamDevice(
        nint stream);
}

