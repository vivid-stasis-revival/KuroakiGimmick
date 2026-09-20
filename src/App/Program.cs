using System.Globalization;
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;
using KuroakiGimmick.UI;

namespace KuroakiGimmick;

/// <summary>
/// 应用入口：解析命令行、选择交互/检查/导出模式，并统一输出错误与退出码。窗口和 GPU 生命周期由 Host/Viewer 管理。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 退出码契约：0 正常结束；1 未捕获异常（已写入 viewer.log）；2 strict 模式或 --inspect 发现错误诊断；130 用户取消视频导出。
    /// 数字解析一律走 InvariantCulture，命令行不随系统区域设置改变含义。
    /// </summary>
    [STAThread] static int Main(string[] args)
    {
        _ = UiLanguage.SystemLanguage;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        // 这一串是所有无人值守模式（自测、检查、导出、截图）。cli 为真时失败只写 stderr，不弹消息框卡住脚本。
        bool cli = args.Any(a => a is "--inspect" or "--render" or "--snapshot" or "--self-test" or "--smoke-ui" or "--smoke-text-ui" or "--smoke-i18n" or "--native-sequence-self-test" or "--text-film-self-test" or "--custom-adaptation-self-test" or "--gpu-test" or "--editor-self-test" or "--reference-self-test" or "--authoring-self-test" or "--layout-image-self-test" or "--image-object-self-test");
        try
        {
            // 后端覆盖必须早于任何 GPU 设备创建，所以在自检分派之前就读掉；否则 --gpu-test --force-vulkan
            // 检的还是平台默认后端，而"能不能强制起 Vulkan"恰恰是这个组合唯一想回答的问题。
            if (args.Contains("--force-vulkan")) Sdl.ForceGpuDriver("vulkan");
            // 自测与 --version/--help 在解析工程之前返回：它们不需要 Session，也不应为了跑一次检查去解码音频。
            if (args.Contains("--native-sequence-self-test")) return NativeSequenceSelfTest.Run(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)));
            if (args.Contains("--custom-adaptation-self-test")) return CustomAdaptationSelfTest.Run();
            if (args.Contains("--text-film-self-test")) return TextFilmSelfTest.Run();
            if (args.Contains("--version"))
            {
                Console.WriteLine("KuroakiGimmick " + Paths.BuildRevision);
                return 0;
            }
            if (args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine(Help);
                return 0;
            }
            if (args.Contains("--image-object-self-test")) return ImageObjectSelfTest.Run();
            if (args.Contains("--layout-image-self-test")) return LayoutImageSelfTest.Run();
            if (args.Contains("--authoring-self-test")) return AuthoringSelfTest.Run();
            if (args.Contains("--reference-self-test")) return ReferenceSelfTest.Run();
            if (args.Contains("--editor-self-test")) return EditorSelfTest.Run();
            if (args.Contains("--self-test"))
            {
                return SelfTest.Run();
            }
            if (args.Contains("--gpu-test"))
            {
                return GpuSelfTest.Run();
            }
            var options = Parse(args);
            var path = options.GetValueOrDefault("file");
            string? projectPath = null;
            // ReadProject 只解析工程并把相对路径解析成绝对路径（基准是工程文件所在目录，不是当前工作目录）；此处还没有任何资源被加载。
            var p = path == null ? Session.EmptyProject() : Session.ReadProject(path, out projectPath);
            // changed 记录命令行是否覆盖过工程字段；有覆盖时 Viewer 不再套用用户偏好，以免把显式指定的参数改回去。
            bool changed = false;
            void Set(string key, Action<string> action)
            {
                if (options.TryGetValue(key, out var value))
                {
                    action(value);
                    changed = true;
                }
            }
            // 命令行给的路径按当前工作目录展开成绝对路径——它不属于工程文件，不适用工程相对解析规则。
            Set("game-ui", v => p.GameUi = Path.GetFullPath(v));
            Set("ui-font", v => p.GameUiFont = v);
            Set("song-name", v => p.SongName = v);
            Set("song-artist", v => p.SongArtist = v);
            Set("song-level", v => p.SongLevel = v);
            if (options.ContainsKey("no-ui"))
            {
                p.GameUiEnabled = false;
                changed = true;
            }
            // --render-width 只改离屏分辨率，不改 320×180 逻辑空间；两个 delay 与 --offset 单位都是毫秒。
            Set("render-width", v => p.RenderWidth = int.Parse(v, CultureInfo.InvariantCulture));
            Set("visual-delay", v => p.VisualDelayMs = double.Parse(v, CultureInfo.InvariantCulture));
            Set("audio-delay", v => p.AudioDelayMs = double.Parse(v, CultureInfo.InvariantCulture));
            Set("note-alignment", v => p.NoteAlignment = v.ToLowerInvariant() switch
            {
                "top" => 0,
                "bottom" => 1,
                _ => throw new ArgumentException("--note-alignment must be top or bottom.")
            });
            Set("gimmick-definition", v => p.GimmickDefinition = Path.GetFullPath(v));
            Set("window-motion", v => p.WindowMotion = Path.GetFullPath(v));
            Set("vsm", v => p.Gimmick = Path.GetFullPath(v));
            Set("vsp", v => p.Images = Path.GetFullPath(v));
            Set("audio", v => p.Audio = Path.GetFullPath(v));
            Set("jacket", v => p.Jacket = Path.GetFullPath(v));
            Set("fx-profile", v => p.FxProfile = Path.GetFullPath(v));
            Set("room", v => p.RoomPreset = GameFxProfile.NormalizePreset(v));
            Set("bpm", v => p.Bpm = double.Parse(v, CultureInfo.InvariantCulture));
            Set("offset", v => p.OffsetMs = double.Parse(v, CultureInfo.InvariantCulture));
            Set("profile", v => p.Profile = v);
            // 覆盖值到此全部应用完毕，CPU 资源只构建一次。绝不能为了多覆盖一个选项再 new 一次 Session——那会重复解码音频、重建粒子。
            var session = new Session(p, projectPath);
            if (options.ContainsKey("inspect"))
            {
                // 绘制前的报告：只反映解析与资源装载结果。真实 shader 错误只可能出现在绘制后的 --snapshot --report 里。
                double? inspectionTime = options.TryGetValue("time", out var at) ? double.Parse(at, CultureInfo.InvariantCulture) : null;
                Console.WriteLine(session.Report(inspectionTime));
                return session.Chart.Diagnostics.Any(d => d.Error) ? 2 : 0;
            }
            if (options.ContainsKey("render") && session.IsEmpty)
            {
                throw new InvalidDataException("Load a chart or VSM before exporting.");
            }
            using var host = new Host(cli);
            Console.Error.WriteLine("Build: " + Paths.BuildRevision);
            Console.Error.WriteLine("GPU: " + host.Device);
            if (options.ContainsKey("render"))
            {
                using var canvas = new Canvas(host.Gpu);
                using var renderer = new SceneRenderer(canvas);
                double Number(string key, double fallback) => options.TryGetValue(key, out var s) ? double.Parse(s,
                    CultureInfo.InvariantCulture) : fallback;
                var output = options.GetValueOrDefault("out") ?? throw new ArgumentException("--render requires --out output.mp4");
                // start/end 是秒；高度按 16:9 由宽度推出，不单独给 --height，避免导出非谱面比例的画面。
                int width = (int) Number("width", 1920);
                var exportOptions = new ExportOptions(Path.GetFullPath(output), Number("start", 0), Number("end", session.Duration),
                    (int) Number("fps", 60), width, width * 9 / 16, session.Project.Notes && !options.ContainsKey("no-notes"),
                    session.Project.PostProcessing && !options.ContainsKey("no-fx"));
                using var job = new VideoExport(canvas, renderer, session, exportOptions);
                ConsoleCancelEventHandler cancel = (_, e) =>
                {
                    e.Cancel = true;
                    job.Cancel();
                };
                Console.CancelKeyPress += cancel;
                try
                {
                    int logged = -1;
                    while (!job.Completed && !job.Cancelled && job.Error == null)
                    {
                        while (Sdl.SDL_PollEvent(out var e))
                        {
                            // 0x100 = SDL_EVENT_QUIT。即便无窗口导出也要抽干事件队列，否则系统会判定进程无响应。
                            if (e.Type == 0x100)
                            {
                                job.Cancel();
                            }
                        }
                        job.Tick();
                        int pct = (int)(job.Progress * 100);
                        if (pct / 10 != logged)
                        {
                            logged = pct / 10;
                            Console.Error.WriteLine($"{pct}% / {job.Status}");
                        }
                        Sdl.SDL_Delay(1);
                    }
                }
                finally
                {
                    Console.CancelKeyPress -= cancel;
                }
                if (job.Error != null)
                {
                    throw new IOException(job.Error);
                }
                // 用户中断按 130 报出（等价于 SIGINT），与"导出失败"区分开，脚本不应把它当错误重试。
                if (job.Cancelled)
                {
                    return 130;
                }
                Console.WriteLine(Path.GetFullPath(output));
                return options.ContainsKey("strict") && session.Chart.Diagnostics.Any(d => d.Error) ? 2 : 0;
            }
            using var viewer = new Viewer(host, session, silent: cli, applyPreferences: !changed,
                uiLanguage: options.GetValueOrDefault("language"));
            // 单帧截图必须抓到最终布局，而不是入场动画那一帧全透明的画面。
            if (options.ContainsKey("snapshot") || options.ContainsKey("smoke-ui") || options.ContainsKey("smoke-text-ui") || options.ContainsKey("smoke-i18n")) viewer.SetUiAnimationsForTest(false);
            if (options.ContainsKey("editor")) viewer.OpenEditor();
            if (options.ContainsKey("settings"))
            {
                viewer.OpenSettings();
            }
            if (options.ContainsKey("manual")) viewer.OpenReferenceForTest(options.GetValueOrDefault("reference"));
            if (options.ContainsKey("smoke-i18n")) viewer.SmokeLocalization();
            if (options.ContainsKey("smoke-ui"))
            {
                viewer.SmokeUi();
                Console.WriteLine("UI shortcuts, frame stepping and export markers passed.");
            }
            if (options.ContainsKey("smoke-text-ui")) viewer.SmokeTextUi();
            if (options.ContainsKey("snapshot") || options.ContainsKey("smoke-ui") || options.ContainsKey("smoke-text-ui") || options.ContainsKey("smoke-i18n"))
            {
                // --time 单位是秒，缺省取 45 秒这一通常已进入演出主体的位置。
                double time = options.TryGetValue("time", out var t) ? double.Parse(t, CultureInfo.InvariantCulture) : 45;
                viewer.SetTime(time);
                viewer.Draw(1440, 940);
                string output = Path.GetFullPath(options.GetValueOrDefault("out", "KuroakiGimmick.ppm"));
                // --scene 取渲染器离屏结果，否则取带 GUI 覆盖层的界面目标；两者不是同一张图。
                viewer.Canvas.SavePpm(options.ContainsKey("scene") ? viewer.Renderer.Final : viewer.UiTarget, output);
                Console.WriteLine(output);
                if (options.ContainsKey("report"))
                {
                    // 必须在 Draw 之后写：只有绘制后的报告才可能包含真实的 shader 编译与纹理错误。
                    File.WriteAllText(Path.GetFullPath(options["report"]), session.Report(time));
                }
                return options.ContainsKey("strict") && session.Chart.Diagnostics.Any(d => d.Error) ? 2 : 0;
            }
            viewer.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            try
            {
                Directory.CreateDirectory(Paths.LogDirectory);
                File.AppendAllText(Path.Combine(Paths.LogDirectory, "viewer.log"), DateTimeOffset.Now + "\n" + ex + "\n\n");
            }
            catch (IOException)
            {
            }
            // 日志写不进去也不能掩盖原始异常：吞掉 IO 失败，继续把错误呈现给用户并返回 1。
            if (!cli)
            {
                try
                {
                    Sdl.SDL_ShowSimpleMessageBox(0x10, "KuroakiGimmick", ex.Message + "\n\nSee viewer.log for details.", 0);
                }
                catch (Exception)
                {
                }
            }
            return 1;
        }
    }

    /// <summary>
    /// 把参数拆成 flags（出现即 "true"）与 values（必须紧跟一个取值），未知选项直接抛错而不是静默忽略。
    /// 位置参数只允许一个，存入 "file" 键。
    /// </summary>
    static Dictionary<string, string> Parse(string[] args)
    {
        var p = new Dictionary<string, string>();
        var flags = new HashSet<string>
        {
            "editor",
            "manual",
            "strict",
            "inspect",
            "render",
            "snapshot",
            "smoke-ui",
            "smoke-text-ui",
            "smoke-i18n",
            "no-notes",
            "no-fx",
            "no-ui",
            "scene",
            "settings",
            // Main 在解析之前就消费掉了，这里登记只是为了它不撞上"Unknown option"。
            "force-vulkan"
        };
        var values = new HashSet<string>
        {
            "language",
            "reference",
            "window-motion",
            "report",
            "gimmick-definition",
            "out",
            "time",
            "start",
            "end",
            "fps",
            "width",
            "vsm",
            "vsp",
            "audio",
            "jacket",
            "fx-profile",
            "room",
            "game-ui",
            "ui-font",
            "song-name",
            "song-artist",
            "song-level",
            "note-alignment",
            "render-width",
            "visual-delay",
            "audio-delay",
            "bpm",
            "offset",
            "profile"
        };
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--"))
            {
                if (p.ContainsKey("file"))
                {
                    throw new ArgumentException("Only one chart/project positional argument is allowed.");
                }
                p["file"] = args[i];
                continue;
            }
            var key = args[i][2..];
            if (flags.Contains(key))
            {
                p[key] = "true";
                continue;
            }
            if (!values.Contains(key))
            {
                throw new ArgumentException("Unknown option --" + key);
            }
            if (++ i >= args.Length)
            {
                throw new ArgumentException("Missing value for --" + key);
            }
            p[key] = args[i];
        }
        return p;
    }

    // 版本号从 Paths 取，别在这里再写一遍：上一次改版本就漏了这行，--help 一直报着旧号。
    static string Help => "KuroakiGimmick " + Paths.BuildRevision + " / C# + SDL3 GPU (Metal / Direct3D 12)\n" + Usage;

    const string Usage = """

    KuroakiGimmick [chart.vsb | chart.vsc | gimmick.vsm | song-folder | project.sgv.json]
    KuroakiGimmick --inspect chart.vsb [--vsm file.vsm] [--vsp file.vsp]
    KuroakiGimmick --render project.sgv.json --out clip.mp4
                        [--start 30] [--end 45] [--fps 60] [--width 1920]
                        [--no-notes] [--no-fx]
    KuroakiGimmick --snapshot project.sgv.json --time 45 --out ui.ppm [--scene]
    UI language: --language auto|zh-CN|en (also available in Settings)
    UI localization check: --smoke-i18n [--editor] --out i18n.ppm
    KuroakiGimmick --editor Samples/EditorDemo/demo.sgv.json
    KuroakiGimmick --editor-self-test
    KuroakiGimmick --reference-self-test
    KuroakiGimmick --manual [--reference vsm.row.57]
    KuroakiGimmick --manual --reference vsm.row.57 --snapshot --out reference.ppm
    KuroakiGimmick --gpu-test
    KuroakiGimmick --self-test
    KuroakiGimmick --text-film-self-test
    KuroakiGimmick --custom-adaptation-self-test
    KuroakiGimmick --native-sequence-self-test [/path/ENCORE.vsb]
    KuroakiGimmick /path/project.sgv.json --smoke-text-ui --time 0 --out text-ui.ppm
    Add --strict to snapshot/render to exit 2 on resource/renderer diagnostics.
    Snapshot: --report frame.json writes the post-render report, including shader errors.
    Inspect: --time SECONDS also evaluates foreground FX visibility at that time.

    Original HUD: --game-ui /path/GameUI --ui-font Default|Monaco [--no-ui]
                 Original font resource names are also accepted.
                  --song-name NAME --song-artist ARTIST --song-level 14+
                  --note-alignment top|bottom (same option as VS; default top)
                  --render-width 320|640|1280|1920|2560|3840 --visual-delay MS --audio-delay MS
    Optional: --room auto|gameplay|plaudite|angelstar|scarletdeath|extendnova|sekaisen|starcrashers|none
              --window-motion ENCORE_cgmk_config.json
              --gimmick-definition gimmick-object.json
              --fx-profile room.fx.json --jacket jacket.jpg --audio song.ogg --bpm 174 --offset 0 --profile auto|core|<manifest-alias>
    GPU: --force-vulkan pins the SDL_GPU backend to Vulkan instead of the platform default
              (Metal on macOS, Direct3D 12 on Windows). No fallback: if Vulkan cannot be
              created, startup fails with the SDL error. macOS needs MoltenVK installed.
              Combines with --gpu-test to check the backend without opening the editor.
    F1: open the read-only VSM/Custom Gimmick reference (available in Viewer and Editor).
    No arguments: open an empty workspace. Song folders auto-associate chart, VSM, images, text and music.
    Note skin: Assets/NoteSkinFull/NotesExact (preferred) or Assets/Notes; press R to reload.
    Ogg/Vorbis preview is built in. MP4 export and other audio formats require FFmpeg.
    KUROAKI_FFMPEG may point to a specific FFmpeg executable.
    """;
}
