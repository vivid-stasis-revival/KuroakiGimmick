using System.Runtime.InteropServices;

namespace KuroakiGimmick.Native;

/// <summary>SDL 事件联合体与输入接口，显式偏移量属于原生 ABI。</summary>
public static partial class Sdl
{
    // -------------------------------------------------------------------------
    // 事件 / 输入
    // -------------------------------------------------------------------------

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_PollEvent(out Event e);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint SDL_GetMouseState(
        out float x,
        out float y);

    /// <summary>
    /// SDL_Event 的联合体镜像，固定 128 字节。字段重叠是设计本身：同一个 [FieldOffset] 上挂着
    /// 不同事件类型的成员（偏移 24 同时是 TextData / WheelX / Scan / Button，偏移 40 是 DropData），
    /// 这就是受支持的 64 位 RID 上真实的 SDL3 布局，不是写错。
    ///
    /// 只允许读取与当前 Type 对应的那组字段；读别的字段拿到的是同一块内存的另一种解释。
    /// 不要重排、不要重新计算偏移、不要为了"看起来整齐"把重叠的成员拆成嵌套结构 ——
    /// 任何一项都会静默改变事件解析结果。
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public struct Event
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(16)] public uint WindowID;
        [FieldOffset(24)] public nint TextData;
        [FieldOffset(24)] public float WheelX;
        [FieldOffset(26)] public byte Clicks;

        // 键盘事件
        [FieldOffset(24)]
        public int Scan;

        [FieldOffset(28)]
        public uint Key;

        [FieldOffset(32)]
        public ushort Modifiers;

        [FieldOffset(37)]
        public byte Repeat;

        // 鼠标按键事件
        [FieldOffset(24)]
        public byte Button;

        // 鼠标移动事件
        [FieldOffset(28)]
        public float X;

        [FieldOffset(32)]
        public float Y;

        // 鼠标滚轮事件
        [FieldOffset(28)]
        public float WheelY;

        // SDL3 的拖放坐标在 20/24；在 64 位 RID 上拖放文本仍然位于偏移 40。
        [FieldOffset(20)] public float DropX;
        [FieldOffset(24)] public float DropY;
        // 拖放事件
        [FieldOffset(40)]
        public nint DropData;
    }
}

