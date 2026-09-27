using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// 交互层状态协调器。加载结果通过主线程队列提交，GPU 操作只在窗口线程执行；设置、输入与绘制分文件维护。
/// </summary>
public sealed partial class Viewer : IDisposable
{
    readonly Host host;
    public Canvas Canvas { get; }
    public SceneRenderer Renderer { get; }
    readonly Fonts fonts;
    readonly Texture logo;
    readonly Transport transport = new();
    readonly ViewerSettings preferences;
    readonly string? settingsPath;
    readonly bool applyPreferences;
    readonly bool persistRecentProjects;
    MacMenuBar? macMenu;
    WindowsMenuBar? windowsMenu;
    int menuStateHash = int.MinValue;
    bool settings, settingsInput;
    public Session Current { get; private set; }
    Target? uiTarget;
    public Target UiTarget => uiTarget!;
    /// <summary>主线程队列。对话框回调等异步结果只能入队，由窗口线程在 Update 中取出执行；回调内不直接改 Viewer 状态。</summary>
    readonly ConcurrentQueue<Action> actions = new();
    /// <summary>字段持有委托实例，防止对话框尚未关闭时委托被 GC 回收、原生侧回调已失效的函数指针。</summary>
    Sdl.DialogCallback? dialog;
    /// <summary>后台解析任务。结果只在 Update 中由窗口线程取回；任务存在期间 Busy 为真，阻止再次加载。</summary>
    Task<Session>? loading;
    string? loadingSourcePath;
    VideoExport? export;
    float mouseX, mouseY;
    bool click, held, seeking, quit, full, notes = true, effects = true, integerScale, diagnostics, help;
    bool dialogOpen;
    double rangeIn, rangeOut;
    double? resumePosition;
    int fps = 60, resolution = 1, diagnosticPage;
    string message = L.Get("Open a .vsb/.vsc chart, .vsm or song folder.");
    readonly Stopwatch uptime = Stopwatch.StartNew();
    double lastFrame, measuredFps = 60;
    /// <summary>任何图片加载、谱面导出、会话加载或仍在进行的视频导出都算忙；导出一旦完成、取消或出错就不再计入，由 Update 负责回收。</summary>
    bool Busy => imageLoad != null || ChartExportBusy || loading != null || export is { Completed: false, Cancelled: false, Error: null };
    /// <summary>所有 GPU 资源（Canvas、字体、logo、Renderer）在窗口线程创建；silent 表示无音频、也不套用用户偏好。</summary>
    public Viewer(Host h, Session session, bool silent = false, bool applyPreferences = true, string? uiLanguage = null, bool showStartup = false, string? initialPath = null, string? settingsPath = null)
    {
        this.applyPreferences = applyPreferences;
        this.settingsPath = settingsPath;
        preferences = ViewerSettings.Load(settingsPath);
        persistRecentProjects = !silent || settingsPath != null;
        if (uiLanguage != null) preferences.UiLanguage = UiLanguage.Normalize(uiLanguage);
        L.SetLanguage(preferences.UiLanguage);
        host = h;
        Canvas = new(h.Gpu);
        fonts = new();
        fonts.SetInterfaceLanguage(L.Language);
        logo = Texture.Load(h.Gpu, Path.Combine(Paths.Assets, "Brand", "kuroaki.png"), linear: true);
        Renderer = new(Canvas);
        Current = session;
        UseSession(session, silent);
        startup = showStartup && session.IsEmpty;
        if (!silent) RememberRecentSource(initialPath ?? session.ProjectPath);
        if (!silent && (OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())) InstallNativeMenu();
    }

    /// <summary>
    /// 切换当前会话并复位导出区间、播放与音量。用户默认偏好只套用到没有 project 文件的会话，
    /// 带 project 文件的会话保留自己的显式值；silent 时不加载音频。
    /// </summary>
    void UseSession(Session s, bool silent = false)
    {
        if (applyPreferences && !silent && s.ProjectPath == null)
        {
            preferences.Apply(s.Project);
        }
        Current = s;
        notes = s.Project.Notes;
        effects = s.Project.PostProcessing;
        RefreshDifficulties();
        rangeIn = 0;
        rangeOut = s.Duration;
        transport.Load(silent ? null : s.Audio, s.Duration);
        transport.SetChartPlayback(s.Playback);
        transport.SetVolume(s.Project.PreviewVolume);
        transport.SetDelay(s.Project.AudioDelayMs);
        message = L.Format($"Loaded {s.Chart.Notes.Count(n=>n.Type is not (3 or 4 or 5)):N0} notes / {s.Chart.Mods.Count:N0} events.");
        if (s.IsEmpty)
        {
            message = L.Get("Open a chart, .vsm or song folder to begin.");
        }
        else if (s.Project.Chart == null)
        {
            message = L.Get("No chart attached: effects and lane only. Attach a .vsb/.vsc to load notes.");
        }
        if (transport.AudioError != null)
        {
            message = L.Get("Preview is silent: ") + transport.AudioError;
        }
        Sdl.SDL_SetWindowTitle(host.Window, "Kuroaki/Gimmick");
    }

    /// <summary>
    /// 窗口线程主循环：排空事件 → Update → Draw → 提交并呈现。所有 GPU 提交只发生在这个线程；
    /// 未播放且不 Busy 时每帧休眠 8 ms，空闲时不烧满一个核心。
    /// </summary>
    public void Run()
    {
        while (!quit)
        {
            while (Sdl.SDL_PollEvent(out var e))
            {
                Handle(e);
            }
            Update();
            Draw();
            host.Gpu.Submit(present: true);
            PresentNativeWindows();
            if (!transport.Playing && !Busy)
            {
                Sdl.SDL_Delay(8);
            }
        }
    }

    /// <summary>
    /// 在后台线程解析文件，结果由 Update 取回。主文件选取优先级：.sgv.json 项目 > .vsb/.vsc 谱面 > 歌曲文件夹、info.json 或 .vsm
    /// （attach 模式下 .vsm 与 info.json 不作为主文件）；其余路径按顺序附加为资源。没有主文件时全部附加到当前会话。
    /// </summary>
    void LoadPaths(string[] paths, bool attach = false)
    {
        if (GuardUnsaved(() => LoadPaths(paths, attach))) return;
        if (Busy)
        {
            return;
        }
        transport.SetPlaying(false);
        var old = Current;
        var selected = paths.FirstOrDefault(p => p.EndsWith(".sgv.json",
            StringComparison.OrdinalIgnoreCase)) ?? paths.FirstOrDefault(p => Path.GetExtension(p).ToLowerInvariant() is ".vsb" or ".vsc") ?? paths.FirstOrDefault(p => Directory.Exists(p) || (!attach && (p.EndsWith(".vsm", StringComparison.OrdinalIgnoreCase) || SongInfoFile(p))));
        loading = Task.Run(() =>
        {
            var s = selected != null ? Session.Load(selected) : old;
            foreach (var path in paths.Where(p => p != selected))
            {
                s = s.Attach(path);
            }
            return s;
        });
        loadingSourcePath = selected;
        message = L.Get("Loading chart and audio...");
    }

    /// <summary>重新解析源文件。先记下播放位置，加载完成后由 Update 恢复，重载不会把进度打回 0。</summary>
    void Reload()
    {
        if (GuardUnsaved(Reload)) return;
        if (Busy)
        {
            return;
        }
        resumePosition = transport.Position;
        transport.SetPlaying(false);
        var p = Current.Project.Copy();
        loading = Task.Run(() => new Session(p, Current.ProjectPath));
        loadingSourcePath = null;
        message = L.Get("Reloading source files...");
    }

    /// <summary>
    /// 用修改后的 project 副本在后台重建会话；只传入的参数生效，其余保持不变。BPM 与 offset 改动必须走重建，
    /// 因为时间轴是解析期产物；播放位置同 Reload 一样在加载完成后恢复。
    /// </summary>
    void Rebuild(double? bpm = null, double? offset = null, double? scroll = null, string? room = null)
    {
        if (GuardUnsaved(() => Rebuild(bpm, offset, scroll, room))) return;
        if (Busy)
        {
            return;
        }
        resumePosition = transport.Position;
        var p = Current.Project.Copy();
        if (bpm.HasValue)
        {
            p.Bpm = bpm.Value;
        }
        if (offset.HasValue)
        {
            p.OffsetMs = offset.Value;
        }
        if (scroll.HasValue)
        {
            p.ScrollSpeed = scroll.Value;
        }
        if (room != null)
        {
            p.RoomPreset = room;
            // 外部 FX profile 的优先级高于 room preset：不清掉它，切换的 room 不会生效。
            p.FxProfile = null;
        }
        transport.SetPlaying(false);
        loading = Task.Run(() => new Session(p, Current.ProjectPath));
        loadingSourcePath = null;
        message = L.Get("Applying timing settings...");
    }

    /// <summary>
    /// 打开原生文件对话框，同一时间只允许一个。回调期间只把路径复制出来（files 指针仅在回调内有效），
    /// result 与 dialogOpen 复位都经 actions 队列推迟到窗口线程；取消对话框时不调用 result。
    /// </summary>
    void Dialog(bool save, string? location, Action<string[]> result, Action? cancelled = null, bool folder = false)
    {
        if (dialogOpen)
        {
            return;
        }
        dialogOpen = true;
        dialog = (_, files, _) =>
        {
            var selected = new List<string>();
            if (files != 0)
            {
                // 以 null 结尾的指针数组；最多读 128 项作为上限保护。
                for (int i = 0; i < 128; i++)
                {
                    var p = Marshal.ReadIntPtr(files, i * nint.Size);
                    if (p == 0)
                    {
                        break;
                    }
                    var s = Marshal.PtrToStringUTF8(p);
                    if (s != null)
                    {
                        selected.Add(s);
                    }
                }
            }
            var values = selected.ToArray();
            actions.Enqueue(() =>
            {
                dialogOpen = false;
                if (values.Length > 0)
                {
                    result(values);
                }
                else cancelled?.Invoke();
            });
        };
        if (folder)
        {
            Sdl.SDL_ShowOpenFolderDialog(dialog, 0, host.Window, location, false);
        }
        else if (save)
        {
            Sdl.SDL_ShowSaveFileDialog(dialog, 0, host.Window, 0, 0, location);
        }
        else
        {
            Sdl.SDL_ShowOpenFileDialog(dialog, 0, host.Window, 0, 0, location, true);
        }
    }

    void ChooseOpen(bool attach = false)
    {
        if (Busy)
        {
            return;
        }
        Dialog(false, Path.GetDirectoryName(Current.Project.Chart ?? Current.Project.Gimmick), paths => LoadPaths(paths, attach));
    }

    /// <summary>编辑器打开时转交 SaveEditor。落盘前把 viewer 侧的 NOTES / FX 开关写回 project，保存的才是当前所见状态。</summary>
    void SaveProject(bool saveAs = false)
    {
        if (editor != null) { SaveEditor(saveAs); return; }
        if (Busy || Current.IsEmpty)
        {
            return;
        }
        var suggested = Current.ProjectPath ?? Path.Combine(Path.GetDirectoryName(Current.Project.Chart ?? Current.Project.Gimmick) ?? Paths.Output,
            "project.sgv.json");
        void SaveAt(string path)
        {
            try
            {
                if (!path.EndsWith(".sgv.json", StringComparison.OrdinalIgnoreCase))
                {
                    path += ".sgv.json";
                }
                Current.Project.Notes = notes;
                Current.Project.PostProcessing = effects;
                Current.Save(path);
                RememberRecentSource(path);
                message = L.Get("Project saved: ") + path;
            }
            catch (Exception ex) { message = L.Get("Save failed: ") + ex.Message; }
        }
        if (!saveAs && Current.ProjectPath != null) SaveAt(Current.ProjectPath);
        else Dialog(true, suggested, paths => SaveAt(paths[0]));
    }

    /// <summary>
    /// 选路径后创建 VideoExport，由 Update 逐帧推进。resolution 索引对应 1280/1920/2560/3840 宽、固定 16:9；
    /// 导出区间、fps 与 NOTES / FX 开关都在此刻定格，之后改动不影响进行中的导出。
    /// </summary>
    void ChooseExport()
    {
        if (Busy || Current.IsEmpty)
        {
            return;
        }
        transport.SetPlaying(false);
        Dialog(true, SuggestedExportPath("Video", "Kuroaki_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4"), paths =>
        {
            var path = paths[0];
            RememberExportDestination("Video", path);
            if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                path += ".mp4";
            }
            int width = new[]
            {
                1280,
                1920,
                2560,
                3840
            }
            [resolution];
            export = new(Canvas, Renderer, Current, new(path, rangeIn, rangeOut, fps, width, width * 9 / 16, notes, effects));
            message = L.Get("Rendering video...");
        });
    }

    void Report()
    {
        Directory.CreateDirectory(Paths.Output);
        var path = Path.Combine(Paths.Output, "compatibility_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
        File.WriteAllText(path, Current.Report(transport.Position));
        message = L.Get("Report saved: ") + path;
    }

    void ToggleFull()
    {
        full = !full;
        Sdl.SDL_SetWindowFullscreen(host.Window, full);
    }(int W, int H, int Pw, int Ph) WindowSize()
    {
        var viewport = Viewport();
        return (viewport.Width, viewport.Height, viewport.PixelWidth, viewport.PixelHeight);
    }

    void Text(string s, float x, float y, float size = 13, Color? c = null, bool mono = false, float max = float.MaxValue, bool unified = false, bool bold = false) => fonts.Text(Canvas,
        s, x, y, size, c ?? white, mono, max, unified, bold);
    /// <summary>
    /// 即时模式按钮，仅在本帧点击命中时返回 true。可用性由覆盖层门控：设置、布局、工作流与模态各自只在自己的输入作用域
    /// （settingsInput / layoutInput / workflowInput / modalInput）内放行，其余覆盖层一律屏蔽下层。
    /// 命中矩形始终是传入的 r：hover / 按下反馈只改填充、描边和文字，不改输入矩形（无移动点击目标规则）。
    /// 过渡状态以 key 为标识，未给 key 时用调用位置 + label；标签会变化的控件（如 PLAY / PAUSE）必须显式给 key，否则动画状态随标签断裂。
    /// </summary>
    bool Button(string label, Rect r, bool primary = false, bool active = false, bool enabled = true,
        string? key = null, [CallerFilePath] string caller = "", [CallerLineNumber] int callerLine = 0)
    {
        bool available = enabled && !ImageGestureActive && !dialogOpen && (!LayoutVisible || (layoutInput && layoutOpen)) && (!ImageImportVisible || imageImportInput) &&
            (resizingLayout == 0) && !ReferenceVisible && !UiClosingOverlay && (!WorkflowVisible || workflowInput || modalInput) && (!settings || settingsInput) &&
            (!infoCard || infoCardInput) &&
            (!StartupVisible || startupInput && startup) &&
            ((!modalActive && pendingDiscard == null && !help) || modalInput);
        bool over = available && r.Contains(mouseX, mouseY);
        string id = "control:" + (key ?? caller + ":" + callerLine + ":" + label);
        float hover = motion.To(id + ":hover", over ? 1 : 0, .12, 0);
        float selected = motion.To(id + ":selected", active ? 1 : 0, .16, active ? 1 : 0);
        if (over && click) motion.Snap(id + ":press", 1);
        float press = motion.To(id + ":press", 0, .19);
        Color fill = Mix(Mix(panel, Color.Hex(0x344156), hover), Color.Hex(0x543449), selected);
        if (primary) fill = Mix(red, Color.Hex(0xC72F50), hover * .6f);
        fill = enabled ? Mix(fill, white, press * .16f) : panel;
        Canvas.Fill(r, fill);
        Canvas.Border(r, primary ? red : Mix(line, soft, Math.Max(hover, selected)));
        var color = enabled ? (primary ? Color.Hex(0xFFFFFF) : white) : Color.Hex(0x8998AC);
        float availableWidth = Math.Max(1, r.W - 12);
        float size = Math.Clamp(13 * availableWidth / Math.Max(1, fonts.Measure(label, 13)), 10, 13);
        float textWidth = Math.Min(availableWidth, fonts.Measure(label, size));
        Text(label, r.X + (r.W - textWidth) / 2, r.Y + (r.H - size) / 2 - 1, size, color, max: availableWidth);
        return available && click && over;
    }

    void Label(string text, float x, float y) => Text(text, x, y, 11, muted, true);
    void Divider(float x, float y, float w) => Canvas.Fill(new(x, y, w, 1), line);
    void FileRow(string label, string? value, float x, float y, float w)
    {
        Label(label, x, y);
        Text(value == null ? L.Get("Not attached") : Path.GetFileName(value), x, y + 18, 12, value == null ? muted : white, max : w);
    }

    public void SetTime(double t) => transport.Seek(t);
    public void OpenSettings()
    {
        settings = true;
        help = false;
    }

    /// <summary>拖动音量条时只置脏标记，不逐帧写设置文件；Draw 在鼠标松开后才落盘一次。</summary>
    bool effectiveVolumeDirty;
    void SetVolume(double volume)
    {
        transport.SetVolume(volume);
        Current.Project.PreviewVolume = transport.Volume;
        effectiveVolumeDirty = true;
    }

    /// <summary>mm:ss.ff 格式；负值取绝对值显示，不带符号。</summary>
    public static string Clock(double s) => $"{(int)Math.Abs(s)/60:00}:{Math.Abs(s)%60:00.00}";
    /// <summary>
    /// 释放次序：先取消进行中的导出与手势、结束布局拖动并释放鼠标捕获，再释放原生窗口、导出、音频，
    /// 最后才释放依附 GPU 设备的 UI 目标、Renderer、字体、logo 与 Canvas。只能在窗口线程调用。
    /// </summary>
    public void Dispose()
    {
        macMenu?.Dispose();
        windowsMenu?.Dispose();
        chartExportCancellation?.Cancel();
        CancelImageGesture();
        ReleaseImageImports();
        if (resizingLayout != 0) EndLayoutResize();
        nativeWindows?.Dispose();
        if (modalActive || InlineActive) Sdl.SDL_StopTextInput(host.Window);
        export?.Dispose();
        transport.Dispose();
        cardRenderer?.Dispose();
        uiTarget?.Dispose();
        Renderer.Dispose();
        fonts.Dispose();
        logo.Dispose();
        Canvas.Dispose();
    }
}
