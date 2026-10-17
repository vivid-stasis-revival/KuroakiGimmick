#if !KG_MOBILE && !KG_SWITCH
using System.Runtime.InteropServices;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>Native desktop confirmation with a real checkbox and a disabled-until-checked destructive button.</summary>
internal static class NativeRiskMessageBox
{
    internal static bool TryShow(nint parent, ChartSizeWarningText text, out bool confirmed)
    {
        confirmed = false;
        if (OperatingSystem.IsMacOS()) { confirmed = Mac.Show(text); return true; }
        if (OperatingSystem.IsWindows()) { confirmed = Windows.Show(parent, text); return true; }
        return false; // Other desktop environments use the same modal message box in the SDL UI.
    }

    static class Mac
    {
        const string Objc = "/usr/lib/libobjc.A.dylib";
        [DllImport(Objc)] static extern nint objc_getClass(string name);
        [DllImport(Objc)] static extern nint sel_registerName(string name);
        [DllImport(Objc)] static extern nint objc_allocateClassPair(nint parent, string name, nint extra);
        [DllImport(Objc)] static extern void objc_registerClassPair(nint cls);
        [DllImport(Objc)] [return: MarshalAs(UnmanagedType.I1)] static extern bool class_addMethod(nint cls, nint selector, nint method, string types);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern nint Send(nint obj, nint selector);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern nint Send(nint obj, nint selector, nint arg);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Action(nint self, nint selector, nint sender);
        static readonly Action Toggle = (_, _, sender) => { if (confirmButton != 0) Send(confirmButton, Sel("setEnabled:"), Send(sender, Sel("state")) == 1 ? 1 : 0); };
        static nint confirmButton;
        static nint Sel(string name) => sel_registerName(name);
        static nint String(string text)
        {
            nint memory = Marshal.StringToCoTaskMemUTF8(text);
            try { return Send(objc_getClass("NSString"), Sel("stringWithUTF8String:"), memory); }
            finally { Marshal.FreeCoTaskMem(memory); }
        }
        internal static bool Show(ChartSizeWarningText text)
        {
            nint cls = objc_getClass("KuroakiSizeWarningTarget");
            if (cls == 0)
            {
                cls = objc_allocateClassPair(objc_getClass("NSObject"), "KuroakiSizeWarningTarget", 0);
                if (cls == 0 || !class_addMethod(cls, Sel("toggleRisk:"), Marshal.GetFunctionPointerForDelegate(Toggle), "v@:@"))
                    throw new InvalidOperationException("Could not create the warning checkbox callback.");
                objc_registerClassPair(cls);
            }
            nint target = Send(Send(cls, Sel("alloc")), Sel("init"));
            nint alert = Send(Send(objc_getClass("NSAlert"), Sel("alloc")), Sel("init"));
            try
            {
                Send(alert, Sel("setAlertStyle:"), 0);
                Send(alert, Sel("setMessageText:"), String(text.Title));
                Send(alert, Sel("setInformativeText:"), String(text.Body));
                Send(alert, Sel("addButtonWithTitle:"), String(text.Cancel));
                confirmButton = Send(alert, Sel("addButtonWithTitle:"), String(text.Confirm));
                Send(confirmButton, Sel("setEnabled:"), 0);
                Send(confirmButton, Sel("setKeyEquivalent:"), String(""));
                Send(alert, Sel("setShowsSuppressionButton:"), 1);
                nint check = Send(alert, Sel("suppressionButton"));
                Send(check, Sel("setTitle:"), String(text.Acknowledgement));
                Send(check, Sel("setState:"), 0);
                Send(check, Sel("setTarget:"), target); Send(check, Sel("setAction:"), Sel("toggleRisk:"));
                return Send(alert, Sel("runModal")) == 1001 && Send(check, Sel("state")) == 1;
            }
            finally { confirmButton = 0; Send(alert, Sel("release")); Send(target, Sel("release")); }
        }
    }

    static unsafe class Windows
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Button { public int Id; public nint Text; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Config
        {
            public uint Size; public nint Parent, Instance; public uint Flags, CommonButtons;
            public nint Title, Icon, Instruction, Content; public uint ButtonCount; public Button* Buttons;
            public int DefaultButton; public uint RadioCount; public nint Radios; public int DefaultRadio;
            public nint Verification, ExpandedInfo, ExpandedControl, CollapsedControl, FooterIcon, Footer, Callback, Data;
            public uint Width;
        }
        [DllImport("comctl32.dll", ExactSpelling = true)] static extern int TaskDialogIndirect(in Config config, out int button, out int radio, out int verified);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Callback(nint window, uint notification, nuint wParam, nint lParam, nint data);
        static readonly Callback Handler = OnNotification;
        static int OnNotification(nint window, uint notification, nuint value, nint _, nint data)
        {
            int* checkedValue = (int*)data;
            if (notification == 0) SendMessage(window, 0x400 + 111, 101, 0); // TDN_CREATED / TDM_ENABLE_BUTTON.
            if (notification == 8)
            { *checkedValue = value != 0 ? 1 : 0; SendMessage(window, 0x400 + 111, 101, *checkedValue); }
            return notification == 2 && value == 101 && *checkedValue == 0 ? 1 : 0;
        }
        internal static bool Show(nint parent, ChartSizeWarningText text)
        {
            var strings = new List<nint>();
            nint String(string value) { nint p = Marshal.StringToCoTaskMemUni(value); strings.Add(p); return p; }
            try
            {
                Button* buttons = stackalloc Button[2];
                buttons[0] = new() { Id = 100, Text = String(text.Cancel) };
                buttons[1] = new() { Id = 101, Text = String(text.Confirm) };
                int check = 0;
                var config = new Config
                {
                    Size = (uint)sizeof(Config), Parent = Sdl.SDL_GetPointerProperty(Sdl.SDL_GetWindowProperties(parent), "SDL.window.win32.hwnd", 0),
                    Flags = 0x8 | 0x1000, Title = String("Kuroaki/Gimmick"), Icon = (nint)0xffff,
                    Instruction = String(text.Title), Content = String(text.Body), ButtonCount = 2, Buttons = buttons,
                    DefaultButton = 100, Verification = String(text.Acknowledgement),
                    Callback = Marshal.GetFunctionPointerForDelegate(Handler), Data = (nint)(&check), Width = 430
                };
                int result = TaskDialogIndirect(in config, out int button, out _, out int verified);
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                return button == 101 && verified != 0 && check != 0;
            }
            finally { foreach (nint memory in strings) Marshal.FreeCoTaskMem(memory); }
        }
    }
}
#endif
