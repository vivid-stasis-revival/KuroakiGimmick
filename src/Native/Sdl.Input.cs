// SDL 事件联合体与输入接口，显式偏移量属于原生 ABI。

using System.Runtime.InteropServices;

namespace KuroakiGimmick.Native;

public static partial class Sdl
{
    // SDL3 手指按下、抬起、移动及取消事件编号为 0x700 至 0x703。
    /// <summary>手指接触、抬起、移动和取消这四类 SDL 事件编号。</summary>
    public const uint FingerDown = 0x700, FingerUp = 0x701, FingerMotion = 0x702, FingerCanceled = 0x703;
    /// <summary>由触摸事件合成鼠标输入时使用的设备 ID。</summary>
    public const uint TouchMouseId = uint.MaxValue; // SDL_TOUCH_MOUSEID

    // -------------------------------------------------------------------------
    // 事件 / 输入
    // -------------------------------------------------------------------------

    /// <summary>从队列取出下一条 SDL 事件。</summary>
    /// <param name="e">接收队首事件的联合体。</param>
    /// <returns>取到事件时为 true，队列为空时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_PollEvent(out Event e);

    /// <summary>取得相对于焦点窗口的鼠标位置及按键状态。</summary>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    /// <returns>当前按下的鼠标按钮位掩码。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint SDL_GetMouseState(
        out float x,
        out float y);

    /// <summary>取得桌面坐标系中的鼠标位置及按键状态。</summary>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    /// <returns>当前按下的鼠标按钮位掩码。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint SDL_GetGlobalMouseState(out float x, out float y);

    /// <summary>将指针移动到指定的桌面坐标。</summary>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_WarpMouseGlobal(float x, float y);

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
        /// <summary>SDL 事件类型，决定联合体中哪些字段有效。</summary>
        [FieldOffset(0)]
        public uint Type;

        /// <summary>窗口相关事件的目标窗口 ID。</summary>
        [FieldOffset(16)] public uint WindowID;
        /// <summary>产生鼠标事件的设备 ID。</summary>
        [FieldOffset(20)] public uint MouseWhich; // SDL_MouseMotionEvent/SDL_MouseButtonEvent.which
        // SDL_TouchFingerEvent 的布局不同于鼠标事件；只有 Finger 类型才能读取这些偏移。
        /// <summary>手指事件所属触摸设备的 ID。</summary>
        [FieldOffset(16)] public ulong TouchID;
        /// <summary>手指事件中的接触点 ID。</summary>
        [FieldOffset(24)] public ulong FingerID;
        /// <summary>手指在窗口中的归一化横坐标。</summary>
        [FieldOffset(32)] public float FingerX;
        /// <summary>手指在窗口中的归一化纵坐标。</summary>
        [FieldOffset(36)] public float FingerY;
        /// <summary>手指事件关联的窗口 ID。</summary>
        [FieldOffset(52)] public uint FingerWindowID;
        /// <summary>文本输入事件中的 UTF-8 文本地址。</summary>
        [FieldOffset(24)] public nint TextData;
        /// <summary>滚轮事件的水平滚动量。</summary>
        [FieldOffset(24)] public float WheelX;
        /// <summary>鼠标按键事件记录的连击次数。</summary>
        [FieldOffset(26)] public byte Clicks;

        // 键盘事件
        /// <summary>键盘事件的物理扫描码。</summary>
        [FieldOffset(24)]
        public int Scan;

        /// <summary>键盘事件的虚拟键码。</summary>
        [FieldOffset(28)]
        public uint Key;

        /// <summary>键盘事件发生时的修饰键位掩码。</summary>
        [FieldOffset(32)]
        public ushort Modifiers;

        /// <summary>键盘事件是否由按键重复产生。</summary>
        [FieldOffset(37)]
        public byte Repeat;

        // 鼠标按键事件
        /// <summary>鼠标按键事件的按钮编号。</summary>
        [FieldOffset(24)]
        public byte Button;

        // 鼠标移动事件
        /// <summary>鼠标事件的窗口横坐标。</summary>
        [FieldOffset(28)]
        public float X;

        /// <summary>鼠标事件的窗口纵坐标。</summary>
        [FieldOffset(32)]
        public float Y;

        // 鼠标滚轮事件
        /// <summary>滚轮事件的垂直滚动量。</summary>
        [FieldOffset(28)]
        public float WheelY;

        // SDL3 的拖放坐标在 20/24；在 64 位 RID 上拖放文本仍然位于偏移 40。
        /// <summary>拖放事件在目标窗口中的横坐标。</summary>
        [FieldOffset(20)] public float DropX;
        /// <summary>拖放事件在目标窗口中的纵坐标。</summary>
        [FieldOffset(24)] public float DropY;
        // 拖放事件
        /// <summary>拖放事件携带的文件名或文本的 UTF-8 地址。</summary>
        [FieldOffset(40)]
        public nint DropData;
    }
}

