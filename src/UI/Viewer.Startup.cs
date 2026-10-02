using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;
using System.Runtime.InteropServices;

namespace KuroakiGimmick.UI;

/// <summary>空会话的欢迎层。所有工程解析仍由 LoadPaths 在后台执行。</summary>
public sealed partial class Viewer
{
    bool startup, startupInput, startupLoadPending;
    int startupScroll;
    string? startupTip;
    bool startupTipVisible;

    void RefreshStartupTip() => startupTip = FunTips.Pick(FunTips.Load(), startupTip);
    bool StartupVisible => startup || startupAlpha > .001f;

    /// <summary>
    /// 最近列表每帧都要重画，标题却要读同目录的歌曲信息文件；按路径缓存，最近列表一变就整体作废。
    /// 列表只在 RememberRecentSource 里变化，因此缓存不会落后于用户新打开的歌。
    /// </summary>
    readonly Dictionary<string, string> recentTitles = [];

    string RecentTitle(string path) => recentTitles.TryGetValue(path, out string? title)
        ? title : recentTitles[path] = RecentSource.Title(path);

    /// <summary>
    /// 原生菜单构建时要一次拿到全部标题，走的是启动页同一个缓存：两处入口显示的字必须一样，
    /// 也不该为了装一次菜单把同一批歌曲信息重复读一遍。
    /// </summary>
    RecentSource.Item[] RecentItems() =>
        [.. preferences.RecentProjects.Select(path => new RecentSource.Item(path, RecentTitle(path)))];

    void RememberRecentSource(string? path)
    {
        if (!preferences.RememberRecentSource(path)) return;
        recentTitles.Clear();
        RequestNativeMenuRefresh();
        if (!persistRecentProjects) return;
        try { preferences.Persist(settingsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { message = L.Get("Settings could not be saved: ") + ex.Message; }
    }

    /// <summary>
    /// 欢迎层里的打开动作。对话框、拖放与最近列表走的都是这一条，加载完一律停在 viewer：
    /// 打开是"看"，要编辑再由 VIEW → EDITOR / VIEWER 或工作区里的按钮显式进入。
    /// </summary>
    void LoadFromStartup(string[] paths)
    {
        if (Busy || paths.Length == 0) return;
        if (editor?.Dirty == true)
        {
            startup = false;
            if (GuardUnsaved(() => LoadFromStartup(paths))) return;
        }
        startupLoadPending = true;
        LoadPaths(paths);
        if (loading == null)
        {
            startupLoadPending = false;
            startup = true;
        }
    }

    void BeginStartupDialog(bool folder = false)
    {
        if (Busy || dialogOpen) return;
        startup = false;
        held = click = false;
        Dialog(false, null, paths => LoadFromStartup(paths), cancelled: () => startup = true, folder: folder);
    }

    void FinishStartupLoad(bool loaded)
    {
        if (!startupLoadPending) return;
        if (loaded) { startup = false; held = click = false; }
        else startup = true;
        startupLoadPending = false;
    }

    /// <summary>欢迎层先于工作区手势和快捷键接收事件；关闭的淡出帧也继续吞掉输入。</summary>
    bool HandleStartupInput(Sdl.Event e)
    {
        if (!StartupVisible) return false;
        if (e.Type is 0x100 or Sdl.WindowCloseRequested) return false;
        if (e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window)) return false;
        if (e.Type == 0x1000 && startup)
        {
            string? path = Marshal.PtrToStringUTF8(e.DropData);
            if (path != null) LoadFromStartup([path]);
            return true;
        }
        if (e.Type is 0x400 or 0x401 or 0x402)
        {
            mouseX = e.X; mouseY = e.Y;
            if (e.Type == 0x401 && e.Button == 1 && startup && !dialogOpen) { click = true; held = true; }
            if (e.Type == 0x402 && e.Button == 1) held = false;
        }
        if (e.Type == 0x403 && startup)
        {
            startupScroll = Math.Clamp(startupScroll - (int)Math.Sign(e.WheelY), 0, Math.Max(0, preferences.RecentProjects.Count - 1));
        }
        if (e.Type == 0x300 && e.Repeat == 0 && startup)
        {
            if (e.Scan == 41 && !dialogOpen) { startup = false; held = click = false; }
            else if (e.Scan == 18 && (e.Modifiers & 0x0CC0) != 0 && !Busy)
                BeginStartupDialog();
        }
        return true;
    }

    void DrawStartup(int w, int h)
    {
        if (!StartupVisible) { startupTipVisible = false; return; }
        if (!startupTipVisible) { RefreshStartupTip(); startupTipVisible = true; }
        Canvas.Clip(null);
        using var fade = Canvas.Opacity(startupAlpha);
        Canvas.Fill(new(0, 0, w, h), Color.Hex(0x03060B, .87f));
        float width = Math.Min(960, w - 36), height = Math.Min(690, h - 36);
        var card = new Rect((w - width) / 2, (h - height) / 2 + (1 - startupAlpha) * 10, width, height);
        Canvas.Fill(card, panel);
        Canvas.Border(card, soft);
        Canvas.Fill(new(card.X, card.Y, 5, card.H), red);
        Canvas.Quad(logo, new(card.X + 28, card.Y + 25, 52, 52), Color.White);
        Text("KUROAKI", card.X + 94, card.Y + 25, 25, white, true);
        Text("GIMMICK", card.X + 95, card.Y + 55, 11, soft, true);
        Text(Paths.BuildRevision, card.X + card.W - 168, card.Y + 43, 12, muted, true, 140);
        Divider(card.X + 28, card.Y + 100, card.W - 56);

        float left = card.X + 28;
        float split = card.X + Math.Max(210, card.W * .32f);
        float right = split + 28, rightWidth = card.X + card.W - 28 - right;
        Label(L.Get("OPEN PROJECT"), left, card.Y + 128);
        startupInput = startup;
        try
        {
            if (Button(L.Get("OPEN PROJECT / FILE"), new(left, card.Y + 159, split - left - 24, 43), primary: true, enabled: !Busy))
            {
                BeginStartupDialog();
            }
            if (Button(L.Get("OPEN SONG FOLDER"), new(left, card.Y + 212, split - left - 24, 35), enabled: !Busy))
                BeginStartupDialog(folder: true);
            Text(L.Get("Projects, charts and folders appear in Recent."), left, card.Y + 263, 11, muted,
                max: split - left - 25);
            if (startupLoadPending || message.StartsWith(L.Get("Load failed: "), StringComparison.Ordinal))
                Text(message, left, card.Y + 301, 11, red, max: split - left - 25);
            DrawStartupTip(left, card.Y + 342, split - left - 25, card.Y + card.H - 90);
            if (Button(L.Get("SETTINGS"), new(left, card.Y + card.H - 69, split - left - 24, 34)))
            {
                startup = false; settings = true; click = false;
            }

            Canvas.Fill(new(split, card.Y + 126, 1, card.H - 154), line);
            Label(L.Get("RECENTLY OPENED"), right, card.Y + 128);
            var recent = preferences.RecentProjects;
            int visibleRows = Math.Max(1, (int)((card.H - 234) / 48));
            startupScroll = Math.Clamp(startupScroll, 0, Math.Max(0, recent.Count - visibleRows));
            if (recent.Count == 0)
                Text(L.Get("No recent projects yet."), right, card.Y + 183, 13, muted, max: rightWidth);
            for (int row = 0; row < visibleRows && row + startupScroll < recent.Count; row++)
            {
                string path = recent[row + startupScroll];
                bool exists = File.Exists(path) || Directory.Exists(path);
                float y = card.Y + 158 + row * 48;
                var item = new Rect(right, y, rightWidth, 44);
                if (Button("", item, enabled: exists && !Busy, key: "startup-recent:" + path))
                {
                    click = false;
                    LoadFromStartup([path]);
                }
                string name = RecentTitle(path);
                float textWidth = rightWidth - (exists ? 22 : 95);
                Text(name, right + 11, y + 5, 14, exists ? white : muted, max: textWidth);
                Text(path, right + 11, y + 25, 10, muted, max: textWidth);
                if (!exists) Text(L.Get("MISSING"), right + rightWidth - 83, y + 14, 10, red, true, 75);
            }
            if (recent.Count > visibleRows)
                Text(L.Get("Scroll for more projects"), right, card.Y + card.H - 37, 10, muted, max: rightWidth);
        }
        finally { startupInput = false; }
    }

    void DrawStartupTip(float x, float y, float width, float bottom)
    {
        if (startupTip == null || width < 40 || bottom - y < 75) return;
        Label(L.Get("TIP"), x, y);
        float lineY = y + 24;
        string rest = startupTip.Replace("\r", "");
        while (rest.Length > 0 && lineY + 16 <= bottom)
        {
            int count = 0;
            while (count < rest.Length && rest[count] != '\n')
            {
                int next = count + (char.IsHighSurrogate(rest[count]) && count + 1 < rest.Length ? 2 : 1);
                if (fonts.Measure(rest[..next], 12) > width && count > 0) break;
                count = next;
            }
            if (count < rest.Length && rest[count] != '\n' && count > 0)
            {
                int space = rest.LastIndexOf(' ', count - 1);
                if (space > 0) count = space;
            }
            Text(rest[..count], x, lineY, 12, muted, max: width);
            rest = rest[count..].TrimStart(' ', '\t');
            if (rest.StartsWith('\n')) rest = rest[1..];
            lineY += 18;
        }
    }

    /// <summary>真实 SDL 输入与 GPU 绘制的启动页检查；调用方使用 silent Viewer，测试不会写用户设置。</summary>
    public void SmokeStartupUi(string screenshot)
    {
        if (!startup || !Current.IsEmpty) throw new Exception("Startup smoke requires an empty welcome session.");
        string sample = Path.GetFullPath("Samples/EditorDemo/demo.sgv.json");
        if (!File.Exists(sample)) throw new FileNotFoundException("Startup smoke sample is missing.", sample);
        string missing = Path.Combine(Path.GetTempPath(), "kuroaki-missing-" + Guid.NewGuid().ToString("N") + ".sgv.json");
        // 截图里要有一行真正的歌：谱面文件名本身就是难度，列表该显示的是曲名 / 曲师 @ 难度 等级。
        // 只用于这一帧，画完立刻删掉，后面的点击断言用的还是下面几个固定条目。
        string song = Path.Combine(Path.GetTempPath(), "kuroaki-startup-song-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(song);
        File.WriteAllText(Path.Combine(song, "ENCORE.vsc"), "0,3,0\n1000,0,0\n");
        File.WriteAllText(Path.Combine(song, "info.json"), """
        {"name":"Scarlet Death","artist":"lexycat","has_encore":true,
         "difficulty_display_4":"17","enc_data":{"audio_id":"music_chart_scarlet.ogg"}}
        """);
        preferences.RecentProjects = [missing, song, sample];
        try
        {
            Draw(1440, 940);
            // Exercise wrapping with fixed text independently of the bundled tips.
            startupTip = "小贴士：拖动播放头，再添加字幕。 Tips stay stable while the welcome page is visible.";
            startupTipVisible = true;
            Draw(1440, 940);
            string? shown = startupTip;
            Draw(1440, 940);
            if (startupTip != shown) throw new Exception("Welcome tip changed during drawing.");
            Draw(800, 600); Draw(1440, 940);
            Canvas.SavePpm(UiTarget, screenshot);
            // 断言的正是绘制循环取标题的那个函数：谱面文件名是 ENCORE，列表里必须是这首歌。
            if (RecentTitle(song) != "Scarlet Death / lexycat @ BACKSTAGE  LV.17")
            {
                throw new Exception("Startup recent row did not show the song: " + RecentTitle(song));
            }
        }
        finally { Directory.Delete(song, true); }
        if (!UiBlockingOverlayVisible) throw new Exception("Startup did not block the workspace.");
        bool before = notes;
        Handle(new Sdl.Event { Type = 0x300, Scan = 17 });
        if (notes != before) throw new Exception("Startup allowed a workspace shortcut through.");
        double position = transport.Position;
        Handle(new Sdl.Event { Type = 0x403, WheelY = 1 });
        if (transport.Position != position) throw new Exception("Startup allowed wheel seeking through.");
        preferences.UiAnimations = true;
        Handle(new Sdl.Event { Type = 0x300, Scan = 41 });
        Draw(1440, 940);
        if (!UiClosingOverlay) throw new Exception("Startup fade did not block closing input.");
        Handle(new Sdl.Event { Type = 0x300, Scan = 17 });
        if (notes != before) throw new Exception("Closing startup allowed a shortcut through.");
        startup = true;
        preferences.UiAnimations = false;
        Draw(1440, 940);
        string chart = Path.GetFullPath("Samples/EditorDemo/ENCORE.vsc");
        nint dropped = Marshal.StringToCoTaskMemUTF8(chart);
        try { Handle(new Sdl.Event { Type = 0x1000, DropData = dropped }); }
        finally { Marshal.FreeCoTaskMem(dropped); }
        if (loading == null || !loading.Wait(TimeSpan.FromSeconds(30)))
            throw new Exception("Startup chart load did not start or timed out.");
        Update();
        if (startup || Current.IsEmpty || preferences.RecentProjects[0] != chart)
            throw new Exception("Opening a chart did not close startup and remember it.");
        if (settingsPath != null && ViewerSettings.Load(settingsPath).RecentProjects.FirstOrDefault() != chart)
            throw new Exception("Recent chart did not persist across settings reload.");
        startup = true;
        string folder = Path.GetFullPath("Samples/EditorDemo");
        LoadFromStartup([folder]);
        if (loading == null || !loading.Wait(TimeSpan.FromSeconds(30)))
            throw new Exception("Startup folder load did not start or timed out.");
        Update();
        if (startup || Current.IsEmpty || preferences.RecentProjects[0] != folder)
            throw new Exception("Opening a song folder did not close startup and remember it.");
        if (settingsPath != null && ViewerSettings.Load(settingsPath).RecentProjects.FirstOrDefault() != folder)
            throw new Exception("Recent folder did not persist across settings reload.");
        UseSession(Session.Empty(), silent: true);
        startup = true;
        preferences.RecentProjects = [missing, folder, sample];
        Draw(1440, 940);
        void Click(float x, float y)
        {
            Handle(new Sdl.Event { Type = 0x401, Button = 1, X = x, Y = y });
            Draw(1440, 940);
            Handle(new Sdl.Event { Type = 0x402, Button = 1, X = x, Y = y });
        }
        // 1440x940: card=(240,125,960,690), recent rows begin at x=575, y=283.
        Click(700, 300);
        if (loading != null || !startup) throw new Exception("Missing recent project started loading.");
        Click(700, 348);
        if (loading == null || !loading.Wait(TimeSpan.FromSeconds(30)))
            throw new Exception("Recent folder click did not start loading.");
        Update();
        if (startup || preferences.RecentProjects[0] != folder || Current.IsEmpty)
            throw new Exception("Recent folder did not open.");
        editorMode = false;
        ResetEditorForLoad();
        UseSession(Session.Empty(), silent: true);
        startup = true;
        preferences.RecentProjects = [missing, sample];
        Draw(1440, 940);
        Click(700, 348);
        if (loading == null) throw new Exception("Recent click did not use LoadPaths.");
        if (!loading.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Recent project load timed out.");
        Update();
        if (startup || editorMode || editor != null || Current.ProjectPath != sample || preferences.RecentProjects[0] != sample)
            throw new Exception("Recent project did not open in the viewer.");
        Draw(1440, 940);
        // 后面那段 Save As 覆盖的是编辑器，这里显式进一次编辑器——打开本身不再顺带进去。
        OpenEditor();
        if (!editorMode || editor == null) throw new Exception("Opening the editor after a recent load failed.");
        string saveDir = Path.Combine(Path.GetTempPath(), "kuroaki-startup-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            string saved = Path.Combine(saveDir, "new-project.sgv.json");
            SaveEditorAt(saved);
            if (!File.Exists(saved) || preferences.RecentProjects[0] != saved)
                throw new Exception("Editor Save As did not add its project to recents: " + message);
            if (settingsPath != null && ViewerSettings.Load(settingsPath).RecentProjects.FirstOrDefault() != saved)
                throw new Exception("Editor Save As recent entry did not persist.");
        }
        finally { if (Directory.Exists(saveDir)) Directory.Delete(saveDir, true); }
        Console.WriteLine("Startup overlay, chart/folder history, missing path, input blocking, recent load, viewer entry, editor entry and Save As passed.");
    }
}
