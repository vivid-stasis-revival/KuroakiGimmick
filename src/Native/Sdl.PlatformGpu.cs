using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace KuroakiGimmick.Native;

/// <summary>
/// SDL_GPU 的设备选择策略与 swapchain 绑定。
/// macOS 用 Metal；Windows 优先 Direct3D 12，不可用时回退 Vulkan。
/// 刻意不使用 OpenGL。
/// Windows 上的 Vulkan 必须是硬件加速：软件实现会被主动销毁并拒绝，宁可启动失败也不当 CPU 幻灯片。
/// </summary>
public static partial class Sdl
{
    // -------------------------------------------------------------------------
    // SDL_GPU
    // -------------------------------------------------------------------------

    /// <summary>
    /// SDL_GPU 接受/提供的 shader 格式。
    /// 取值与 SDL_GPUShaderFormat 完全一致，属于 ABI，不要重排或重新赋值。
    /// </summary>
    [Flags]
    public enum GpuShaderFormat : uint
    {
        Invalid  = 0,
        Private  = 1u << 0,
        SpirV    = 1u << 1,
        Dxbc     = 1u << 2,
        Dxil     = 1u << 3,
        Msl      = 1u << 4,
        MetalLib = 1u << 5
    }

    /// <summary>
    /// swapchain 的颜色格式 / 色彩空间模式；取值与 SDL_GPUSwapchainComposition 一致。
    /// </summary>
    public enum GpuSwapchainComposition
    {
        Sdr = 0,
        SdrLinear = 1,
        HdrExtendedLinear = 2,
        Hdr10St2084 = 3
    }

    /// <summary>
    /// swapchain 的呈现时序；取值与 SDL_GPUPresentMode 一致。
    /// </summary>
    public enum GpuPresentMode
    {
        VSync = 0,
        Immediate = 1,
        Mailbox = 2
    }

    /// <summary>
    /// 命令行 --force-vulkan 之类的后端覆盖，进程级；为 null 时按平台默认顺序挑。
    /// 必须在创建第一个 GPU 设备之前设好，之后再改不会让已建成的设备换后端。
    /// </summary>
    public static string? ForcedGpuDriver { get; private set; }

    /// <summary>
    /// 把后端钉死在某个驱动上（--force-vulkan）。必须在 SDL_Init 和第一个 GPU 设备之前调用。
    ///
    /// macOS 上还要顺带把 Vulkan loader 找出来：SDL 是 dlopen("libvulkan.1.dylib") 去加载的，而
    /// Apple Silicon 上 Homebrew 装到 /opt/homebrew/lib，那个目录不在 dyld 的默认搜索路径里，于是
    /// loader 明明装着也只会得到一句 "SDL_HINT_GPU_DRIVER vulkan unsupported!"。这里按常见位置探一遍
    /// 并写进 SDL_VULKAN_LIBRARY；用户自己设过就不动，显式配置优先于我们的猜测。
    /// </summary>
    public static void ForceGpuDriver(string driver)
    {
        ForcedGpuDriver = driver;
        if (driver != "vulkan" || !OperatingSystem.IsMacOS() ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SDL_VULKAN_LIBRARY")))
        {
            return;
        }
        string?[] roots = [Environment.GetEnvironmentVariable("VULKAN_SDK") + "/lib", "/opt/homebrew/lib", "/usr/local/lib"];
        string? found = roots
            .Where(root => !string.IsNullOrEmpty(root))
            .SelectMany(root => new[] { root + "/libvulkan.1.dylib", root + "/libMoltenVK.dylib" })
            .FirstOrDefault(File.Exists);
        if (found != null)
        {
            // 必须走 SDL_SetHint 而不是 Environment.SetEnvironmentVariable：在 Unix 上后者只改 .NET
            // 自己那份托管副本，原生 getenv 根本看不见，SDL 读到的还是空。
            SDL_SetHint("SDL_VULKAN_LIBRARY", found);
        }
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    /// <summary>
    /// 本项目为某个后端唯一能生成的 shader 字节码格式。候选表和建成设备后的校验共用这一份映射，
    /// 免得两处各写一份、加后端时漏改一边。
    /// </summary>
    public static GpuShaderFormat ShaderFormatFor(string driver) => driver.ToLowerInvariant() switch
    {
        "metal" => GpuShaderFormat.Msl,
        "direct3d12" => GpuShaderFormat.Dxbc,
        "vulkan" => GpuShaderFormat.SpirV,
        _ => GpuShaderFormat.Invalid
    };

    /// <summary>
    /// 本平台首选的 SDL_GPU 后端。
    /// Windows 上这只是第一选择，创建流程失败后会继续尝试 Vulkan。
    /// </summary>
    public static string PlatformGpuDriver
    {
        get
        {
            if (ForcedGpuDriver is { } forced)
                return forced;

            if (OperatingSystem.IsMacOS())
                return "metal";

            if (OperatingSystem.IsWindows())
                return "direct3d12";

            throw new PlatformNotSupportedException(
                "Kuroaki GPU backend currently supports only macOS/Metal " +
                "and Windows/Direct3D 12/Vulkan.");
        }
    }

    /// <summary>
    /// 按优先级排列的后端，以及每个后端能消费的 shader 格式。
    /// 探测或创建设备之前，会先把应用自己能提供的格式掩码与这里的值取交集；
    /// 数组顺序就是回退顺序，不要重排。
    /// </summary>
    private static (string Driver, GpuShaderFormat Formats)[] GetGpuCandidates()
    {
        // --force-vulkan：候选表里只留它一个。强制某个后端的整个意义就是"我要看它到底能不能跑"，
        // 这时候悄悄回退到 Metal / D3D12 会让结论作废，所以宁可带着 SDL 的原始错误直接启动失败。
        if (ForcedGpuDriver is { } forced)
        {
            return [(forced, ShaderFormatFor(forced))];
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                ("metal", GpuShaderFormat.Msl | GpuShaderFormat.MetalLib)
            ];
        }

        if (OperatingSystem.IsWindows())
        {
            return
            [
                ("direct3d12", GpuShaderFormat.Dxbc | GpuShaderFormat.Dxil),
                ("vulkan", GpuShaderFormat.SpirV)
            ];
        }

        throw new PlatformNotSupportedException(
            "Kuroaki GPU backend currently supports only macOS/Metal " +
            "and Windows/Direct3D 12/Vulkan.");
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_GPUSupportsShaderFormats(
        GpuShaderFormat formatFlags,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUDevice(
        GpuShaderFormat formatFlags,
        [MarshalAs(UnmanagedType.I1)] bool debugMode,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

    // Vulkan 走基于 properties 的创建路径，这样由 SDL 自己拒绝软件实现，
    // 而不是悄悄选中 SwiftShader / Lavapipe / Dozen+WARP。
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint SDL_CreateProperties();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroyProperties(uint props);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetBooleanProperty(
        uint props,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetStringProperty(
        uint props,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUDeviceWithProperties(uint props);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroyGPUDevice(
        nint device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_GetGPUDeviceDriver(
        nint device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern GpuShaderFormat SDL_GetGPUShaderFormats(
        nint device);

    // SDL 3.4+ 才有的设备元数据。调用点在运行期做了保护，因此同一份托管构建
    // 仍能配合没有导出这个符号的旧版 SDL3.dll 运行。
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint SDL_GetGPUDeviceProperties(nint device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_GetStringProperty(
        uint props,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? defaultValue);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_ClaimWindowForGPUDevice(
        nint device,
        nint window);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseWindowFromGPUDevice(
        nint device,
        nint window);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetGPUAllowedFramesInFlight(
        nint device,
        uint allowedFramesInFlight);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_WindowSupportsGPUPresentMode(
        nint device,
        nint window,
        GpuPresentMode presentMode);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetGPUSwapchainParameters(
        nint device,
        nint window,
        GpuSwapchainComposition swapchainComposition,
        GpuPresentMode presentMode);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint SDL_GetGPUSwapchainTextureFormat(
        nint device,
        nint window);

    // -------------------------------------------------------------------------
    // GPU 命令缓冲 / 呈现
    // -------------------------------------------------------------------------

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_AcquireGPUCommandBuffer(
        nint device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_WaitAndAcquireGPUSwapchainTexture(
        nint commandBuffer,
        nint window,
        out nint swapchainTexture,
        out uint width,
        out uint height);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_AcquireGPUSwapchainTexture(
        nint commandBuffer,
        nint window,
        out nint swapchainTexture,
        out uint width,
        out uint height);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SubmitGPUCommandBuffer(
        nint commandBuffer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_CancelGPUCommandBuffer(
        nint commandBuffer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_WaitForGPUIdle(
        nint device);

    // -------------------------------------------------------------------------
    // 托管侧 GPU 辅助方法
    // -------------------------------------------------------------------------

    /// <summary>
    /// 创建 SDL_GPU 设备。
    /// macOS：Metal。
    /// Windows：先 Direct3D 12，不可用再 Vulkan。
    /// </summary>
    public static nint CreatePlatformGpuDevice(
        bool debugMode = false,
        GpuShaderFormat shaderFormats = GpuShaderFormat.Invalid)
    {
        return CreatePlatformGpuDeviceInternal(
            window: 0,
            claimWindow: false,
            debugMode,
            shaderFormats);
    }

    /// <summary>
    /// 创建 SDL_GPU 设备并把窗口 claim 上去。Windows 上，D3D12 的探测/创建/claim 任一步失败，
    /// 都会继续走 Vulkan 候选；失败的设备在换下一个候选之前一定会被销毁，不会泄漏。
    /// </summary>
    public static nint CreatePlatformGpuDeviceForWindow(
        nint window,
        bool debugMode = false,
        GpuShaderFormat shaderFormats = GpuShaderFormat.Invalid)
    {
        if (window == 0)
        {
            throw new ArgumentException(
                "SDL window is null.",
                nameof(window));
        }

        return CreatePlatformGpuDeviceInternal(
            window,
            claimWindow: true,
            debugMode,
            shaderFormats);
    }

    private static nint CreatePlatformGpuDeviceInternal(
        nint window,
        bool claimWindow,
        bool debugMode,
        GpuShaderFormat shaderFormats)
    {
        // 这里是"本应用实际能提供"的格式，不是驱动支持的格式。
        GpuShaderFormat availableFormats =
            shaderFormats == GpuShaderFormat.Invalid
                ? PlatformShaderFormats
                : shaderFormats;

        List<string> failures = [];

        foreach (var candidate in GetGpuCandidates())
        {
            string driver = candidate.Driver;
            GpuShaderFormat candidateFormats = availableFormats & candidate.Formats;

            if (candidateFormats == GpuShaderFormat.Invalid)
            {
                failures.Add($"{driver}: application provides no compatible shader format");
                continue;
            }

            if (!SDL_GPUSupportsShaderFormats(candidateFormats, driver))
            {
                failures.Add($"{driver}: unsupported for shader formats {candidateFormats}: {Error}");
                continue;
            }

            nint device = CreateGpuCandidateDevice(driver, candidateFormats, debugMode);
            if (device == 0)
            {
                string reason = driver.Equals("vulkan", StringComparison.OrdinalIgnoreCase)
                    ? "SDL_CreateGPUDeviceWithProperties failed (hardware Vulkan required)"
                    : "SDL_CreateGPUDevice failed";
                failures.Add($"{driver}: {reason}: {Error}");
                continue;
            }

            // 即使机器根本没有硬件 Vulkan，SDL 仍可能给出一个软件 Vulkan 实现
            // （例如跑在 Microsoft Basic Render Driver 上的 Mesa Dozen）。它确实能创建出
            // SDL_GPU 设备，但渲染发生在 CPU 上，对 Kuroaki 这种实时编辑器完全不可用。
            // 所以在这里直接销毁并拒绝：Windows 要么拿到 D3D12 / 硬件 Vulkan，要么立刻带着
            // 明确错误启动失败，而不是打开成一个幻灯片。
            // 这段不是"过度防御"，删掉它等于把一次干净的启动失败换成一个卡到没法用的编辑器。
            if (OperatingSystem.IsWindows() &&
                driver.Equals("vulkan", StringComparison.OrdinalIgnoreCase) &&
                TryGetGpuDeviceInfo(device, out var vkInfo) &&
                IsSoftwareVulkan(vkInfo))
            {
                Console.Error.WriteLine(
                    $"[Kuroaki/GPU] rejecting software Vulkan device: " +
                    $"device={vkInfo.Name}, driver={vkInfo.Driver}, version={vkInfo.Version}");
                SDL_DestroyGPUDevice(device);
                failures.Add(
                    $"vulkan: only a software renderer is available " +
                    $"({vkInfo.Name}, driver {vkInfo.Driver}); hardware Vulkan is required");
                continue;
            }

            if (claimWindow && !SDL_ClaimWindowForGPUDevice(device, window))
            {
                string claimError = Error;
                SDL_DestroyGPUDevice(device);
                failures.Add($"{driver}: SDL_ClaimWindowForGPUDevice failed: {claimError}");
                continue;
            }

            string selectedDriver = GetGpuDriverName(device);
            Console.WriteLine(
                $"[Kuroaki/GPU] backend={selectedDriver}, " +
                $"shaderFormats={SDL_GetGPUShaderFormats(device)}");
            TryLogGpuDeviceInfo(device, selectedDriver);

            return device;
        }

        string details = failures.Count == 0
            ? "No GPU backend candidates were available."
            : string.Join(Environment.NewLine, failures);

        // 强制后端时没有第二个候选，所以这条错误就是最终结论，得说清下一步做什么，
        // 不能只把 SDL 的原话甩出来——下面两条平台提示分别对应各自最常见的失败原因。
        if (ForcedGpuDriver is { } forcedDriver)
        {
            // macOS：最常见的不是"没装 Vulkan"，而是 loader 装了却不在 dyld 的默认搜索路径里
            // （Homebrew 的 /opt/homebrew/lib 就是这种）。ForceGpuDriver 已经替用户找过一遍了，
            // 走到这里说明那几个位置都没有。
            // Windows：vulkan-1.dll 由显卡驱动装进 System32，找不到的情况极少；真正常见的是机器只有
            // 软件 Vulkan（Dozen / Lavapipe），被上面那段有意拒掉了——这里得讲明白是"故意不要"，
            // 否则用户会以为 --force-vulkan 本身坏了。
            string hint = forcedDriver != "vulkan" ? ""
                : OperatingSystem.IsMacOS()
                    ? "macOS reaches Vulkan through MoltenVK. Install it (brew install molten-vk vulkan-loader, " +
                      "or the LunarG Vulkan SDK), or point SDL_VULKAN_LIBRARY at libvulkan.1.dylib directly."
                : OperatingSystem.IsWindows()
                    ? "Kuroaki requires hardware-accelerated Vulkan. Software renderers (Mesa Dozen, Lavapipe, " +
                      "SwiftShader) are rejected on purpose: they render on the CPU and turn the editor into a " +
                      "slideshow. Install a GPU driver that ships a hardware Vulkan ICD, or drop --force-vulkan " +
                      "to use Direct3D 12."
                    : "";
            throw new PlatformNotSupportedException(
                $"--force-{forcedDriver} was requested but that backend could not be created, " +
                "and forcing a backend disables fallback." + Environment.NewLine +
                Environment.NewLine +
                details +
                (hint.Length == 0 ? "" : Environment.NewLine + Environment.NewLine + hint));
        }

        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "No supported hardware graphics backend is available." + Environment.NewLine +
                Environment.NewLine +
                "Kuroaki requires Direct3D 12 or hardware-accelerated Vulkan on Windows." + Environment.NewLine +
                "Software Vulkan renderers are not supported because they cause severe CPU usage and dropped frames." + Environment.NewLine +
                Environment.NewLine +
                details);
        }

        throw new PlatformNotSupportedException(
            "Unable to initialize an SDL_GPU backend." + Environment.NewLine + details);
    }


    static nint CreateGpuCandidateDevice(
        string driver,
        GpuShaderFormat formats,
        bool debugMode)
    {
        // 传统的 SDL_CreateGPUDevice() 路径是刻意允许软件 Vulkan 实现的。
        // Kuroaki 不能用它做实时渲染，所以 Windows 上的 Vulkan 改用带
        // requirehardwareacceleration 属性的创建方式。
        if (!OperatingSystem.IsWindows() ||
            !driver.Equals("vulkan", StringComparison.OrdinalIgnoreCase))
        {
            return SDL_CreateGPUDevice(formats, debugMode, driver);
        }

        uint props = SDL_CreateProperties();
        if (props == 0)
        {
            return 0;
        }

        try
        {
            bool ok = true;
            ok &= SDL_SetStringProperty(props, "SDL.gpu.device.create.name", driver);
            ok &= SDL_SetBooleanProperty(props, "SDL.gpu.device.create.debugmode", debugMode);
            ok &= SDL_SetBooleanProperty(props, "SDL.gpu.device.create.shaders.spirv",
                (formats & GpuShaderFormat.SpirV) != 0);
            ok &= SDL_SetBooleanProperty(props,
                "SDL.gpu.device.create.vulkan.requirehardwareacceleration", true);

            if (!ok)
            {
                return 0;
            }

            return SDL_CreateGPUDeviceWithProperties(props);
        }
        finally
        {
            SDL_DestroyProperties(props);
        }
    }

    readonly record struct GpuDeviceInfo(string Name, string Driver, string Version);

    /// <summary>属性名字符串是 SDL 的公开契约，拼错不会编译报错，只会安静地取到默认值。</summary>
    static bool TryGetGpuDeviceInfo(nint device, out GpuDeviceInfo info)
    {
        info = default;
        try
        {
            uint props = SDL_GetGPUDeviceProperties(device);
            if (props == 0)
            {
                return false;
            }

            string Read(string key)
            {
                nint value = SDL_GetStringProperty(props, key, null);
                return value == 0 ? "unknown" : Marshal.PtrToStringUTF8(value) ?? "unknown";
            }

            info = new GpuDeviceInfo(
                Read("SDL.gpu.device.name"),
                Read("SDL.gpu.device.driver_name"),
                Read("SDL.gpu.device.driver_version"));
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            // SDL < 3.4：拿不到设备元数据。这里只返回 false，让旧的 DLL 继续可用，
            // 不要把它升级成异常。
            return false;
        }
    }

    /// <summary>
    /// 按设备名与驱动名的关键字识别软件渲染器。这是启发式判断，宁可漏判也不要放宽 ——
    /// 每一项关键字都对应现实中遇到过的软件实现，删条目等于让那种配置重新混进来。
    /// </summary>
    static bool IsSoftwareVulkan(GpuDeviceInfo info)
    {
        string id = (info.Name + " " + info.Driver).ToLowerInvariant();
        return id.Contains("swiftshader") ||
               id.Contains("llvmpipe") ||
               id.Contains("lavapipe") ||
               id.Contains("software") ||
               id.Contains("basic render") ||
               // Mesa Dozen 把 Vulkan 翻译到 D3D12。Dozen 本身可以是硬件加速的，
               // 所以只有当所选适配器是微软的软件/基础渲染器时才拒绝
               // （这种情况已被上面的判断覆盖）。
               (id.Contains("dozen") && id.Contains("microsoft basic"));
    }

    static void TryLogGpuDeviceInfo(nint device, string backend)
    {
        if (!TryGetGpuDeviceInfo(device, out var info))
        {
            return;
        }

        Console.WriteLine(
            $"[Kuroaki/GPU] device={info.Name}, driver={info.Driver}, version={info.Version}");

        if (backend.Equals("vulkan", StringComparison.OrdinalIgnoreCase) && IsSoftwareVulkan(info))
        {
            // 正常情况下到不了这里：CreatePlatformGpuDeviceInternal 会在返回设备之前就拒绝它。
            // 保留这条作为防御性诊断，真的打印出来说明上面的拒绝逻辑被改坏了。
            Console.Error.WriteLine(
                "[Kuroaki/GPU] WARNING: Vulkan is software-rendered; " +
                "this device should have been rejected during backend selection.");
        }
    }

    /// <summary>
    /// 释放窗口并销毁其 GPU 设备。次序不可调换：先等 GPU 空闲，再解绑窗口，最后销毁设备。
    /// </summary>
    public static void DestroyGpuDeviceForWindow(
        nint device,
        nint window)
    {
        if (device == 0)
            return;

        SDL_WaitForGPUIdle(device);

        if (window != 0)
            SDL_ReleaseWindowFromGPUDevice(device, window);

        SDL_DestroyGPUDevice(device);
    }

    /// <summary>
    /// 实际启用的 SDL_GPU 后端名，例如 "metal"、"direct3d12" 或 "vulkan"。
    /// 这是 SDL 报告的真实后端，不是 PlatformGpuDriver 那个首选值，回退发生后两者会不同。
    /// </summary>
    public static string GetGpuDriverName(nint device)
    {
        if (device == 0)
            return "none";

        nint ptr = SDL_GetGPUDeviceDriver(device);

        return ptr == 0
            ? "unknown"
            : Marshal.PtrToStringUTF8(ptr) ?? "unknown";
    }
}
