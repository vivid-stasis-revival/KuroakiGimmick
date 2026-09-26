using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// 交互层状态协调器。加载结果通过主线程队列提交，GPU 操作只在窗口线程执行；设置、输入与绘制分文件维护。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>绘制预览、检查面板和设置覆盖层；使用 Renderer.Final 展示场景而不是再实现一套预览逻辑。</summary>
    public void Draw(int? forcedWidth = null, int? forcedHeight = null)
    {
        var dims = WindowSize();
        int w = forcedWidth ?? dims.W, h = forcedHeight ?? dims.H, pw = forcedWidth ?? dims.Pw, ph = forcedHeight ?? dims.Ph;
        if (w <= 0 || h <= 0 || pw <= 0 || ph <= 0)
        {
            return;
        }
        if (uiTarget == null || uiTarget.Texture.Width != pw || uiTarget.Texture.Height != ph)
        {
            uiTarget?.Dispose();
            uiTarget = new(host.Gpu, pw, ph, linear: true);
        }
        double now = uptime.Elapsed.TotalSeconds, delta = now - lastFrame;
        lastFrame = now;
        if (delta > 0)
        {
            // 指数平滑的帧率读数，单帧上限 240，仅用于面板显示。
            measuredFps = measuredFps * .95 + Math.Min(240, 1 / delta) * .05;
        }
        BeginUiMotion();
        workspaceGeometry = WorkspaceGeometry.Create(w, h, editorMode && editor != null, LayoutOptions);
        var time = transport.Position;
        // 本帧全部 GPU 工作从这里开始，只在窗口线程执行：先渲染场景，再把 UI 画到离屏 uiTarget。
        Renderer.Render(Current, time, notes, effects);
        // 信息卡片有自己的离屏目标，必须在 UI 合成开始之前画完，中途不再切换渲染目标。
        RenderInfoCard();
        Canvas.Begin(uiTarget, pw, ph, w, h, bg);
        using var workspaceFade = Canvas.Opacity(workspaceAlpha);
        if (editorMode && editor != null)
        {
            DrawEditor(w, h, pw, ph, time);
            FinishUiFrame(w, h, pw, ph);
            return;
        }
        Canvas.Quad(logo, new(24, 20, 46, 46), Color.White);
        Text("KUROAKI", 82, 22, 23, white, true);
        Text("GIMMICK", 83, 50, 10, soft, true);
        Canvas.Fill(new(272, 28, 1, 30), line);
        Text(Paths.BuildRevision, 292, 35, 12, muted, true, max: Math.Max(1, w - 1024));
        if (Button(L.Get("LAYOUT"), new(w - 634, 25, 90, 31), enabled: !Busy)) { OpenLayout(); click = false; }
        if (Button(L.Get("CARD"), new(w - 724, 25, 80, 31), enabled: !Busy && !Current.IsEmpty)) { OpenInfoCard(); }
        if (Button(L.Get("EXPORT"), new(w - 534, 25, 90, 31), enabled: !Busy && !Current.IsEmpty))
        { OpenChartExport(); click = false; }
        if (Button(L.Get("EDITOR UI"), new(w - 434, 25, 116, 31), enabled: !Busy && !Current.IsEmpty))
        { ToggleWorkspace(); click = false; }
        if (Button(L.Get("SETTINGS"), new(w - 308, 25, 102, 31), enabled: !Busy))
        {
            settings = true;
            help = false;
        }
        if (Button(L.Get("HELP  H"), new(w - 196, 25, 78, 31)))
        {
            help = !help;
        }
        if (Button(L.Get("SAVE"), new(w - 108, 25, 80, 31), enabled: !Busy))
        {
            SaveProject();
        }
        Divider(24, 79, w - 48);
        var geometry = workspaceGeometry;
        float lx = 24, lw = geometry.LeftWidth, cx = geometry.PreviewX, rw = geometry.RightWidth, rx = geometry.RightX, cw = geometry.PreviewWidth;
        Label(L.Get("01 / SOURCES"), lx, 102);
        if (Button(L.Get("OPEN CHART / VSM"), new(lx, 128, lw, 38), primary: true, enabled: !Busy))
        {
            ChooseOpen();
        }
        if (Button(L.Get("+ FILES / RESOURCES"), new(lx, 177, lw, 34), enabled: !Busy))
        {
            ChooseOpen(true);
        }
        if (DifficultyBarVisible)
        {
            DrawDifficulties(lx, 213, lw);
        }
        else
        {
            Text(L.Get("Drop files / song folder"), lx + 18, 224, 11, muted);
        }
        Divider(lx, 253, lw);
        FileRow(activeDifficulty.Length > 0 ? L.Get("CHART") + " / " + DifficultyLabel : L.Get("CHART"), Current.Project.Chart, lx, 272, lw);
        FileRow(L.Get("GIMMICK"), Current.Project.Gimmick ?? (Current.Chart.Mods.Count > 0 ? L.Get("Embedded in VSB") : null), lx, 325, lw);
        FileRow(L.Get("IMAGES / ") + Current.Images.Items.Count, Current.Images.Path, lx, 378, lw);
        FileRow(L.Get("AUDIO"), Current.Project.Audio, lx, 431, lw);
        Divider(lx, 483, lw);
        FileRow(L.Get("TEXT / ") + Current.Texts.Tracks.Count, Current.Texts.Tracks.Count > 0 ? Current.Texts.Tracks.Count + L.Get(" subtitle tracks") : null,
            lx, 503, lw);
        FileRow(L.Get("JACKET / ") + Current.Jackets.Mode, Current.Jackets.At(Current.Timeline, time), lx, 556, lw);
        Label(L.Get("ROOM FX"), lx, 606);
        string roomLabel = Current.Fx.Source is "external" or "local" ? L.Get("EXTERNAL FX") : Current.Project.RoomPreset == "auto" ? L.Get("AUTO / ") + (Current.Fx.Room?.Replace("scene_gameplay_", "").Replace("scene_gameplay", "gameplay") ?? L.Get("NONE")).ToUpperInvariant() : Current.Project.RoomPreset.ToUpperInvariant();
        if (Button(roomLabel, new(lx, 626, lw, 32), enabled: !Busy && !Current.IsEmpty))
        {
            int at = Array.IndexOf(GameFxProfile.Presets, GameFxProfile.NormalizePreset(Current.Project.RoomPreset));
            Rebuild(room: GameFxProfile.Presets[(at + 1) % GameFxProfile.Presets.Length]);
        }
        if (Button(L.Get("RELOAD FILES  R"), new(lx, 674, lw, 33), enabled: !Busy && !Current.IsEmpty))
        {
            Reload();
        }
        Text(L.Get("WINDOW MOTION"), lx, Math.Max(735, h - 123), 10, soft, true);
        if (Button(desktopPreview ? L.Get("SCENE") : L.Get("DESKTOP"), new(lx, Math.Max(753, h - 101), 94, 26), key: "viewer-desktop")) desktopPreview = !desktopPreview;
        if (Button(nativeWindows != null ? L.Get("LIVE: ON") : L.Get("LIVE: OFF"), new(lx + 100, Math.Max(753, h - 101), 92, 26), key: "viewer-live")) ToggleNativeWindows();
        Label(L.Get("02 / PREVIEW"), cx, 102);
        Text(Current.IsEmpty ? L.Get("Open a chart or song folder") : Current.Title, cx, 126, 22, white, max: cw - 10);
        Text(Current.NativeGimmick.Data?.DisplayName ?? L.Get("CORE PROFILE"), cx, 158, 10, soft, true);
        Text(L.Format($"{Renderer.Final.Texture.Width} x {Renderer.Final.Texture.Height} / NEAREST"), cx + cw - 205, 158, 10, muted, true);
        float previewY = 174, previewH = Math.Max(160, geometry.SplitY - previewY - 8);
        var frame = new Rect(cx, previewY, cw, previewH);
        previewFrame = frame;
        Canvas.Fill(frame, Color.Hex(0x030204));
        Canvas.Border(frame, line);
        var image = Canvas.PreviewRect(new(cx + 1, previewY + 1, cw - 2, previewH - 2), pw / (float) w, ph / (float) h, integerScale);
        previewImage = image;
        // 歌曲画面不随工作区淡入淡出：直接展示 Renderer.Final，不另做一套预览渲染。
        using (Canvas.Unfaded())
        {
            if (desktopPreview) DrawDesktop(frame, time);
            else Canvas.Quad(Renderer.Final.Texture, image, Color.White);
        }
        if (Current.IsEmpty)
        {
            Text(L.Get("OPEN A CHART TO BEGIN"), cx + cw / 2 - 125, previewY + previewH / 2 - 10, 18, muted);
        }
        // 四个小的猩红色角括号，始终画在歌曲图像之外。
        foreach (var (x, y, sx, sy) in new[]
        {
            (cx, previewY, 1, 1),
            (cx + cw, previewY, -1, 1),
            (cx, previewY + previewH, 1, -1),
            (cx + cw, previewY + previewH, -1, -1)
        })
        {
            Canvas.Line(x, y, x + sx * 12, y, 2, soft);
            Canvas.Line(x, y, x, y + sy * 12, 2, soft);
        }
        float controls = previewY + previewH + 12;
        if (Button(transport.Playing? L.Get("PAUSE") : L.Get("PLAY"), new(cx, controls, 79, 34), primary : true, enabled : !Busy, key: "viewer-play"))
        {
            transport.SetPlaying(!transport.Playing);
        }
        if (Button("<", new(cx + 88, controls, 33, 34), enabled: !Busy))
        {
            transport.SetPlaying(false);
            transport.Seek(time - 1.0 / fps);
        }
        if (Button(">", new(cx + 127, controls, 33, 34), enabled: !Busy))
        {
            transport.SetPlaying(false);
            transport.Seek(time + 1.0 / fps);
        }
        if (Button($"{transport.Speed:0.00}x", new(cx + 170, controls, 65, 34), enabled: !Busy, key: "viewer-speed"))
        {
            transport.SetSpeed(transport.Speed >= 2?.25 : transport.Speed + .25);
        }
        if (Button(L.Get("NOTES"), new(cx + 245, controls, 65, 34), active: notes, enabled: !Busy))
        {
            notes = !notes;
        }
        if (Button(L.Get("FX"), new(cx + 317, controls, 40, 34), active: effects, enabled: !Busy))
        {
            effects = !effects;
        }
        if (Button(integerScale ? L.Get("INTEGER") : L.Get("FIT"), new(cx + 364, controls, 73, 34)))
        {
            integerScale = !integerScale;
        }
        if (Button(L.Get("VS UI"), new(cx + 444, controls, 65, 34), active: Current.Project.GameUiEnabled, enabled: !Busy))
        {
            Current.Project.GameUiEnabled = !Current.Project.GameUiEnabled;
        }
        if (Button(L.Get("FULL"), new(cx + cw - 57, controls, 57, 34)))
        {
            ToggleFull();
        }
        float ty = controls + 49;
        Text(Clock(time), cx, ty, 24, white, true);
        Text("/ " + Clock(Current.Duration), cx + 164, ty + 8, 12, muted, true);
        if (cw > 540 && Math.Abs(Current.Playback.RateAt(time) - 1) > 1e-9)
            Text(L.Format($"CHART {Current.Playback.RateAt(time):0.##}x"), cx + 290, ty + 8, 12, soft, true, cw - 430);
        Text(L.Get("BEAT ") + Current.Timeline.Bpm.Beat(time).ToString("0.00", CultureInfo.InvariantCulture), cx + cw - 126, ty + 8, 12, soft, true);
        var seek = new Rect(cx, ty + 38, cw, 45);
        Canvas.Fill(seek, panel);
        // 事件密度直接由已加载文件统计，从不使用占位波形数据。
        int bins = Math.Max(1, (int) cw / 4);
        var density = new int[bins];
        foreach (var m in Current.Chart.Mods)
        {
            int bin = (int)(Current.Timeline.Bpm.Time(m.Beat) / Current.Duration * bins);
            if (bin >= 0 && bin < bins)
            {
                density[bin]++;
            }
        }
        int peak = Math.Max(1, density.Max());
        for (int i = 0; i < bins; i++)
        {
            float bar = 3 + 30 * MathF.Sqrt(density[i] / (float) peak);
            Canvas.Fill(new(cx + i * cw / bins, seek.Y + seek.H - bar - 6, 2, bar),
                i / (double) bins <= time / Current.Duration? red.Alpha(.65) : soft.Alpha(.3));
        }
        Canvas.Fill(new(cx, seek.Y + seek.H - 1, cw, 1), line);
        float cursor = cx + (float)(time / Current.Duration) * cw;
        Canvas.Fill(new(cursor, seek.Y - 2, 2, seek.H + 4), white);
        // 拖动开始后只要按住就持续 seek，指针离开进度条也不中断；覆盖层可见或 Busy 时整体禁用。
        if (!UiOverlayVisible && !Busy && ((click && seek.Contains(mouseX, mouseY)) || seeking && held))
        {
            seeking = true;
            transport.Seek(Math.Clamp((mouseX - cx) / cw, 0, 1) * Current.Duration);
        }
        for (int i = 0; i <= 4; i++)
        {
            Text(Clock(Current.Duration * i / 4).Split('.')[0], cx + cw * i / 4 - (i == 4 ? 46 : 0), seek.Y + 54, 10, muted, true);
        }
        float infoY = seek.Y + 78;
        Divider(cx, infoY, cw);
        Label(L.Get("EVENTS"), cx, infoY + 16);
        Text(Current.Chart.Mods.Count.ToString("N0"), cx, infoY + 34, 19, white, true);
        Label(L.Get("NOTES"), cx + cw * .27f, infoY + 16);
        Text(Current.Chart.Notes.Count(n => n.Type is not (3 or 4 or 5)).ToString("N0"), cx + cw * .27f, infoY + 34, 19, white, true);
        Label("BPM", cx + cw * .52f, infoY + 16);
        Text(Current.Timeline.Bpm.BpmAtBeat(Current.Timeline.Bpm.Beat(time)).ToString("0.##"), cx + cw * .52f, infoY + 34, 19, white, true);
        Label(L.Get("PREVIEW"), cx + cw * .77f, infoY + 16);
        Text($"{measuredFps:0} fps", cx + cw * .77f, infoY + 34, 19, white, true);
        Label(L.Get("03 / INSPECT & EXPORT"), rx, 102);
        float reportTabGap = 8;
        float reportTabWidth = Math.Max(1, (rw - reportTabGap) / 2);
        if (Button(L.Get("INSPECT"), new(rx, 128, reportTabWidth, 31), active: !diagnostics))
        {
            diagnostics = false;
        }
        if (Button(L.Get("REPORT ") + Current.Chart.Diagnostics.Count, new(rx + reportTabWidth + reportTabGap, 128, reportTabWidth, 31), active: diagnostics))
        {
            diagnostics = true;
        }
        if (animatedDiagnostics != diagnostics) { animatedDiagnostics = diagnostics; motion.Snap("inspect-panel", 0); }
        using (Canvas.Opacity(motion.To("inspect-panel", 1, .14)))
        {
        if (diagnostics)
        {
            // 旧的报告视图仍是最初固定 282 px 的布局：消息被硬切成 32 字符一块，
            // 导航按钮放在写死的 Y 值上。
            // 一旦右侧面板宽度或全局 UI 缩放改变，面板变大了内容却不会重排。
            // 报告布局必须与工作区其余部分留在同一套响应式坐标系里，
            // 不要再回到固定像素的写法。
            Text(L.Get("Compatibility details"), rx, 177, 15, white, max: rw);
            var list = Current.Chart.Diagnostics;
            int index = list.Count == 0 ? 0 : Math.Clamp(diagnosticPage, 0, list.Count - 1);
            diagnosticPage = index;

            float navY = Math.Max(360, h - 142);
            float saveY = Math.Max(navY + 38, h - 100);
            float bodyTop = 205;
            float bodyBottom = Math.Max(bodyTop + 72, navY - 22);
            float yy = bodyTop;
            int shown = 0;

            if (list.Count == 0)
            {
                Text(L.Get("No unsupported events found."), rx, yy, 12, muted, max: rw);
            }
            else
            {
                for (int item = index; item < list.Count && yy < bodyBottom - 24; item++)
                {
                    var d = list[item];
                    string[] rows = EditorHelpLayout.Wrap(d.Message, Math.Max(80, rw - 4),
                        text => fonts.Measure(text, 11));
                    int maxRows = Math.Max(1, (int)MathF.Floor((bodyBottom - yy - 25) / 16));
                    int drawRows = Math.Min(rows.Length, maxRows);
                    if (drawRows <= 0) break;

                    Text(d.Error ? L.Get("ERROR") : L.Get("NOTICE"), rx, yy, 9, d.Error ? red : soft, true);
                    for (int row = 0; row < drawRows; row++)
                    {
                        string text = rows[row];
                        if (row == drawRows - 1 && drawRows < rows.Length)
                            text = EditorHelpLayout.FitTitle(text + " …", Math.Max(80, rw - 4), value => fonts.Measure(value, 11));
                        Text(text, rx, yy + 18 + row * 16, 11, muted, max: rw);
                    }

                    yy += 28 + drawRows * 16;
                    shown++;
                    if (drawRows < rows.Length) break;
                }
            }

            if (list.Count > 0)
            {
                string range = $"{index + 1}–{Math.Min(list.Count, index + Math.Max(1, shown))} / {list.Count}";
                Text(range, rx, navY - 19, 10, muted, true, max: rw);
            }

            float navGap = 10;
            float navWidth = Math.Max(1, (rw - navGap) / 2);
            int nextIndex = Math.Min(Math.Max(0, list.Count - 1), index + Math.Max(1, shown));
            if (Button(L.Get("PREV"), new(rx, navY, navWidth, 31), enabled: list.Count > 0 && index > 0))
            {
                diagnosticPage = Math.Max(0, index - Math.Max(1, shown));
            }
            if (Button(L.Get("NEXT"), new(rx + navWidth + navGap, navY, navWidth, 31), enabled: list.Count > 0 && nextIndex > index))
            {
                diagnosticPage = nextIndex;
            }
            if (Button(L.Get("SAVE REPORT"), new(rx, saveY, rw, 36)))
            {
                Report();
            }
        }
        else
        {
            string[] mods = Current.NativeGimmick.Data?.PreviewMods ?? ["scrollspeed", "velocity", "wave", "notealp", "uialpha"];
            for (int i = 0; i < Math.Min(mods.Length, 5); i++)
            {
                Label(mods[i].ToUpperInvariant(), rx, 182 + i * 29);
                Text(Current.Timeline.Get(mods[i], time).ToString("0.000", CultureInfo.InvariantCulture), rx + 162, 180 + i * 29, 12, white, true);
            }
            Divider(rx, 340, rw);
            Label(L.Get("TIMING"), rx, 357);
            if (Button(Current.Project.NoteAlignment == 0 ? L.Get("ALIGN: TOP") : L.Get("ALIGN: BOTTOM"), new(rx + 100, 349, 147, 26), enabled : !Busy))
            {
                Current.Project.NoteAlignment = 1 - Math.Clamp(Current.Project.NoteAlignment, 0, 1);
            }
            Text("BPM", rx, 386, 11, muted);
            // 固定节拍来自对象定义；不能显示一个已禁用编辑、却与实际时间轴不同的项目值。
            double displayedBpm = Current.Timeline.Bpm.IsFixed? Current.Timeline.Bpm.BpmAtBeat(0) : Current.Project.Bpm;
            Text(displayedBpm.ToString("0.##", CultureInfo.InvariantCulture), rx + 91, 386, 12, white, true);
            if (Button("-", new(rx + 178, 377, 31, 26), enabled: !Busy && !Current.Timeline.Bpm.IsFixed))
            {
                Rebuild(bpm: Math.Max(1, Current.Project.Bpm - 1));
            }
            if (Button("+", new(rx + 216, 377, 31, 26), enabled: !Busy && !Current.Timeline.Bpm.IsFixed))
            {
                Rebuild(bpm: Current.Project.Bpm + 1);
            }
            Text(L.Get("Offset / ms"), rx, 422, 11, muted);
            Text(Current.Project.OffsetMs.ToString("0"), rx + 105, 422, 12, white, true);
            if (Button("-", new(rx + 178, 413, 31, 26), enabled: !Busy))
            {
                Rebuild(offset: Current.Project.OffsetMs - 1);
            }
            if (Button("+", new(rx + 216, 413, 31, 26), enabled: !Busy))
            {
                Rebuild(offset: Current.Project.OffsetMs + 1);
            }
            Text(L.Get("Scroll"), rx, 458, 11, muted);
            Text(Current.Project.ScrollSpeed.ToString("0.0"), rx + 105, 458, 12, white, true);
            if (Button("-", new(rx + 178, 449, 31, 26), enabled: !Busy))
            {
                Rebuild(scroll: Math.Max(.1, Current.Project.ScrollSpeed - .1));
            }
            if (Button("+", new(rx + 216, 449, 31, 26), enabled: !Busy))
            {
                Rebuild(scroll: Current.Project.ScrollSpeed + .1);
            }
            Divider(rx, 491, rw);
            Label(L.Get("VIDEO EXPORT"), rx, 509);
            if (Button(new[]
            {
                "1280 x 720",
                "1920 x 1080",
                "2560 x 1440",
                "3840 x 2160"
            }
            [resolution], new(rx, 532, 145, 31), enabled: !Busy))
            {
                resolution = (resolution + 1) % 4;
            }
            if (Button(fps + " FPS", new(rx + 155, 532, 95, 31), enabled: !Busy))
            {
                fps = fps == 30 ? 60 : fps == 60 ? 120 : 30;
            }
            Text(L.Get("IN  ") + Clock(rangeIn), rx, 581, 12, muted, true);
            Text(L.Get("OUT ") + Clock(rangeOut), rx, 605, 12, muted, true);
            if (Button(L.Get("SET IN  I"), new(rx, 635, 120, 30), enabled: !Busy))
            {
                rangeIn = Math.Clamp(time, 0, Math.Max(0, rangeOut - 1.0 / fps));
            }
            if (Button(L.Get("SET OUT  U"), new(rx + 130, 635, 120, 30), enabled: !Busy))
            {
                rangeOut = Math.Max(time, rangeIn + 1.0 / fps);
            }
            if (Button(L.Get("FULL SONG"), new(rx, 675, rw, 28), enabled: !Busy))
            {
                rangeIn = 0;
                rangeOut = Current.Duration;
            }
            if (Button(export == null ? L.Get("EXPORT MP4") : L.Get("CANCEL EXPORT"), new(rx, 716, rw, 42), primary : true, enabled : loading == null))
            {
                if (export == null)
                {
                    ChooseExport();
                }
                else
                {
                    export.Cancel();
                }
            }
            if (export != null)
            {
                Canvas.Fill(new(rx, 770, rw, 3), line);
                Canvas.Fill(new(rx, 770, (float) export.Progress * rw, 3), red);
            }
            Text(L.Get("Fixed frames / original audio"), rx, 788, 10, muted);
        }
        }
        Divider(24, h - 55, w - 48);
        Canvas.Fill(new(24, h - 35, 5, 5), Busy? red : soft);
        using (Canvas.Opacity(motion.To("status", 1, .15))) Text(message, 38, h - 40, 11, muted, max: w - 310);
        Text(L.Get("VOL"), w - 237, h - 38, 9, muted, true);
        var volumeRect = new Rect(w - 198, h - 38, 93, 12);
        Canvas.Fill(new(volumeRect.X, volumeRect.Y + 5, volumeRect.W, 2), line);
        Canvas.Fill(new(volumeRect.X, volumeRect.Y + 5, (float) transport.Volume * volumeRect.W, 2), soft);
        if (!UiOverlayVisible && held && volumeRect.Contains(mouseX, mouseY))
        {
            SetVolume((mouseX - volumeRect.X) / volumeRect.W);
        }
        // 松开鼠标后才写一次设置文件，拖动过程中不逐帧落盘。
        if (effectiveVolumeDirty && !held)
        {
            SaveSettings();
            effectiveVolumeDirty = false;
        }
        Text($"{transport.Volume*100:0}%", w - 89, h - 40, 10, white, true);
        DrawInfoCard(w, h);
        if ((help || helpAlpha > .001f) && !settings)
        {
            using var fade = Canvas.Opacity(helpAlpha);
            // 快捷键卡片的按钮借用 modalInput 通过 Button 门控；离开分支前必须复位。
            modalInput = help;
            Canvas.Fill(new(0, 0, w, h), Color.Hex(0x000000, .78f));
            var r = new Rect(w / 2 - 275, h / 2 - 230 + (1 - helpAlpha) * 10, 550, 460);
            Canvas.Fill(r, panel);
            Canvas.Border(r, soft);
            Text(L.Get("KUROAKI / QUICK CONTROLS"), r.X + 30, r.Y + 29, 20, white, true);
            string[] rows = [L.Get("Space                  Play / pause"), L.Get("Left / Right           Previous / next frame"),
                L.Get("Shift + Left / Right   Move one second"), L.Get("Home / End             Start / end"), L.Get("I / U                  Mark export in / out"),
                L.Get("N / P                  Notes / post FX"), L.Get("F / M                  Fullscreen / mute"), L.Get("Ctrl or Cmd + O        Open files"),
                L.Get("Ctrl or Cmd + S        Save project"), L.Get("Ctrl or Cmd + E        Export MP4"), L.Get("R                      Reload source files")];
            for (int i = 0; i < rows.Length; i++)
            {
                if (!fonts.ChineseInterface)
                {
                    Text(rows[i], r.X + 32, r.Y + 85 + i * 26, 12, muted, true);
                    continue;
                }
                int split = rows[i].IndexOf("  ", StringComparison.Ordinal);
                if (split < 0) Text(rows[i], r.X + 32, r.Y + 85 + i * 26, 12, muted, max: r.W - 64);
                else
                {
                    Text(rows[i][..split], r.X + 32, r.Y + 85 + i * 26, 12, muted, max: 205);
                    Text(rows[i][split..].TrimStart(), r.X + 250, r.Y + 85 + i * 26, 12, muted, max: r.W - 282);
                }
            }
            if (Button(L.Get("VSM DOCS / F1"), new(r.X + 283, r.Y + 400, 237, 32)))
            { OpenReference(); click = false; }
            if (Button(L.Get("CLOSE"), new(r.X + 30, r.Y + 400, 237, 32)))
            {
                help = false;
            }
            modalInput = false;
        }

        FinishUiFrame(w, h, pw, ph);
    }

    /// <summary>
    /// 每帧的收尾 Z 序：分隔条 → 轨道帮助 → 图片拖放 → 参考文档 → 不受工作区淡入淡出影响的各覆盖层，
    /// 然后把离屏 uiTarget 整幅合成到 swapchain。click 在此清零，本帧之后的绘制不能再消费点击。
    /// </summary>
    void FinishUiFrame(int w, int h, int pw, int ph)
    {
        Canvas.Clip(null);
        // 工作区分隔条应位于临时文档层之下。帮助卡片在分隔条之后绘制，而不是把分隔条隐藏再重新显示；
        // 这样悬停帮助就只是一个纯粹的 Z 序覆盖层，
        // 消除了曾让两层同时闪烁的可见性反馈回路。
        DrawLayoutGrips();
        if (editorMode && editor != null)
            DrawAnimatedTrackHelp(editorTimelineRect,
                !UiBlockingOverlayVisible && !ImageGestureActive ? hoveredEditTrack : null, w, h);
        DrawImageDropOverlay();
        if (ReferenceVisible)
        {
            using var unfaded = Canvas.Unfaded();
            DrawReference(w, h);
        }
        using (Canvas.Unfaded())
        {
            // 关闭后 alpha 仍大于 .001 时继续绘制，让淡出动画播完；期间 Button 由 UiClosingOverlay 拦截。
            if (settings || settingsAlpha > .001f)
            {
                using var fade = Canvas.Opacity(settingsAlpha);
                DrawSettings(w, h);
            }
            DrawWorkflow(w, h);
            DrawEditorModals(w, h);
            DrawLayout(w, h);
            DrawImageImport(w, h);
            DrawStartup(w, h);
        }
        Canvas.Flush();
        Canvas.Begin(null, pw, ph, w, h, bg);
        Canvas.Quad(UiTarget.Texture, new(0, 0, w, h), Color.White);
        Canvas.Flush();
        click = false;
    }
}
