using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

/// <summary>
/// Settings panels use the same Canvas/Button hitboxes as the rest of the Viewer.
/// Only view geometry changes. Options continue to be persisted through ViewerSettings.
/// </summary>
public sealed partial class Viewer
{
    void SettingsCard(Rect r, string heading)
    {
        Canvas.Fill(r, Color.Hex(0x171219));
        Canvas.Border(r, soft);
        Text(L.Get(heading).ToUpperInvariant(), r.X + 14, r.Y + 12, 14, white, true);
    }

    void SettingsHint(string content, float x, float y, float width) =>
        Text(L.Get(content), x, y, 10, muted, max: width);

    void DrawSettingsInterface(Rect r)
    {
        SettingsCard(r, "Interface");
        var p = Current.Project;
        float sx = r.X + 14;
        float cx = r.X + MathF.Min(146, r.W * .30f);
        float cw = r.X + r.W - 16 - cx;
        float y = r.Y + 46;
        Text(L.Get("Language"), sx, y + 6, 13, white);
        string[] langs = [UiLanguage.Auto, UiLanguage.Chinese, UiLanguage.English];
        string[] labels = [L.Get("SYSTEM"), "中文", "English"];
        float third = (cw - 12) / 3;
        for (int i = 0; i < 3; i++)
            if (Button(labels[i], new(cx + i * (third + 6), y, third, 29),
                active: preferences.UiLanguage == langs[i], key: "settings-language:" + langs[i]))
                SetUiLanguage(langs[i]);

        y += 45;
        Text(L.Get("Text Font"), sx, y + 7, 13, white);
        float half = (cw - 8) / 2;
        if (Button(L.Get("DEFAULT"), new(cx, y, half, 30), active: p.GameUiFont == ViewerSettings.DefaultFont, key: "settings-font-default"))
            p.GameUiFont = ViewerSettings.DefaultFont;
        bool hasMonaco = Current.GameUi.Data?.Fonts.ContainsKey(ViewerSettings.MonacoFont) == true;
        if (Button("MONACO", new(cx + half + 8, y, half, 30), active: p.GameUiFont == ViewerSettings.MonacoFont,
            enabled: hasMonaco, key: "settings-font-monaco"))
            p.GameUiFont = ViewerSettings.MonacoFont;

        y += 45;
        Text(L.Get("Note Alignment"), sx, y + 7, 13, white);
        if (Button(L.Get("TOP"), new(cx, y, half, 30), active: p.NoteAlignment == 0, key: "settings-alignment-top")) p.NoteAlignment = 0;
        if (Button(L.Get("BOTTOM"), new(cx + half + 8, y, half, 30), active: p.NoteAlignment == 1, key: "settings-alignment-bottom")) p.NoteAlignment = 1;
        if (r.H >= 184)
            SettingsHint("Which note edge meets the judgement line. TOP sits 7 px lower.", sx, r.Y + 174, r.W - 28);
    }

    void DrawSettingsAudio(Rect r)
    {
        SettingsCard(r, "Audio & Sync");
        var p = Current.Project;
        float sx = r.X + 14;
        float cx = r.X + MathF.Min(148, r.W * .30f);
        float cw = r.X + r.W - 16 - cx;
        float y = r.Y + 46;
        Text(L.Get("Audio volume"), sx, y + 5, 13, white);
        float sw = cw - 48;
        var slider = new Rect(cx, y + 3, sw, 18);
        Canvas.Fill(new(slider.X, slider.Y + 7, slider.W, 4), line);
        Canvas.Fill(new(slider.X, slider.Y + 7, (float)p.PreviewVolume * slider.W, 4), red);
        if (settings && !UiClosingOverlay && held && new Rect(slider.X, slider.Y - 8, slider.W, 32).Contains(mouseX, mouseY))
            SetVolume((mouseX - slider.X) / slider.W);
        Text($"{p.PreviewVolume * 100:0}%", cx + sw + 9, y + 4, 12, white, true);

        void DelayRow(string title, float dy, bool audio)
        {
            double value = audio ? p.AudioDelayMs : p.VisualDelayMs;
            Text(L.Get(title), sx, dy + 7, 13, white);
            int[] steps = [-10, -1, 1, 10];
            float bw = MathF.Min(43, (cw - 80) / 4);
            float right = cx + cw;
            float[] xx = [cx, cx + bw + 4, right - bw * 2 - 4, right - bw];
            for (int i = 0; i < steps.Length; i++)
                if (Button(steps[i].ToString("+0;-0"), new(xx[i], dy, bw, 29), key: $"settings-{(audio ? "audio" : "visual")}-delay:{steps[i]}"))
                {
                    value = Math.Clamp(value + steps[i], -2000, 2000);
                    if (audio) { p.AudioDelayMs = value; transport.SetDelay(value); }
                    else p.VisualDelayMs = value;
                }
            Text($"{value:+0;-0;0} ms", cx + (cw - 75) / 2, dy + 6, 12, white, true);
        }
        DelayRow("Audio delay", y + 43, true);
        DelayRow("Visual delay", y + 83, false);
        if (r.H >= 190)
            SettingsHint("Visual delay shifts notes; gimmick timing stays on the song clock.", sx, r.Y + 174, r.W - 28);
    }

    void DrawSettingsDisplay(Rect r)
    {
        SettingsCard(r, "Display");
        var p = Current.Project;
        float sx = r.X + 14, y = r.Y + 45;
        float cx = r.X + MathF.Min(145, r.W * .30f);
        float cw = r.X + r.W - 16 - cx;
        Text(L.Get("Render resolution"), sx, y + 4, 13, white);
        float bw = (cw - 12) / 3;
        for (int i = 0; i < ViewerSettings.Widths.Length; i++)
        {
            int size = ViewerSettings.Widths[i];
            if (Button($"{size} x {size * 9 / 16}", new(cx + (i % 3) * (bw + 6), y + (i / 3) * 34, bw, 29),
                active: p.RenderWidth == size, key: "settings-resolution:" + size))
                p.RenderWidth = size;
        }
    }

    void DrawSettingsWorkflow(Rect r)
    {
        SettingsCard(r, "Workflow");
        float sx = r.X + 14, y = r.Y + 42;
        float cx = r.X + MathF.Min(108, r.W * .25f);
        float cw = r.X + r.W - 16 - cx;
        Text(L.Get("UI theme"), sx, y + 8, 13, white);
        string[] themes = ViewerSettings.UiThemes;
        float bw = (cw - 12) / 3;
        for (int i = 0; i < themes.Length; i++)
        {
            string theme = themes[i];
            if (Button(theme.ToUpperInvariant(), new(cx + i * (bw + 6), y, bw, 29),
                active: string.Equals(preferences.UiTheme, theme, StringComparison.OrdinalIgnoreCase), key: "settings-theme:" + theme))
                SetUiTheme(theme);
        }
        y += 41;
        float toggleW = (r.W - 40) / 3;
        if (Button(preferences.UiAnimations ? L.Get("ON") : L.Get("OFF"), new(sx, y, toggleW, 29),
            active: preferences.UiAnimations, key: "settings-ui-motion")) preferences.UiAnimations = !preferences.UiAnimations;
        if (Button(preferences.ModalValueEditor ? L.Get("MODAL FIELDS") : L.Get("INLINE FIELDS"), new(sx + toggleW + 6, y, toggleW, 29),
            active: preferences.ModalValueEditor, key: "settings-modal-fields")) preferences.ModalValueEditor = !preferences.ModalValueEditor;
        if (Button(preferences.AlwaysFollow ? L.Get("ALWAYS FOLLOW") : L.Get("AUTO RELEASE"), new(sx + (toggleW + 6) * 2, y, toggleW, 29),
            active: preferences.AlwaysFollow, key: "settings-always-follow")) preferences.AlwaysFollow = !preferences.AlwaysFollow;
    }

    void DrawSettingsEditor(Rect r)
    {
        SettingsCard(r, "Editor");
        float x = r.X + 14, y = r.Y + 46, toggleX = r.X + r.W - 126;
        Text(L.Get("Group gimmick tracks"), x, y + 7, 13, white, max: toggleX - x - 12);
        if (Button(L.Get(preferences.GroupGimmickTracks ? "ON" : "OFF"), new(toggleX, y, 110, 30),
            active: preferences.GroupGimmickTracks, key: "settings-group-gimmicks"))
            SetGimmickGrouping(!preferences.GroupGimmickTracks);
        y += 48;
        Text(L.Get("Show expanded GMK summaries"), x, y + 7, 13, white, max: toggleX - x - 12);
        if (Button(L.Get(preferences.ShowExpandedGimmickSummary ? "ON" : "OFF"), new(toggleX, y, 110, 30),
            active: preferences.ShowExpandedGimmickSummary, key: "settings-expanded-gimmick-summary"))
        { preferences.ShowExpandedGimmickSummary = !preferences.ShowExpandedGimmickSummary; SaveSettings(); }
        SettingsHint("Collapsed groups always show summaries. Expanded groups keep their title and collapse button.", x, y + 46, r.W - 28);
#if !KG_MOBILE && !KG_SWITCH
        Divider(x, r.Y + 180, r.W - 28);
        DrawChartSizeOptions(new(r.X, r.Y + 192, r.W, r.H - 192));
#endif
    }

    void DrawSettingsVsUi(Rect r)
    {
        SettingsCard(r, "VS UI");
        var p = Current.Project;
        float sx = r.X + 14, y = r.Y + 43;
        float cx = r.X + MathF.Min(140, r.W * .28f);
        float cw = r.X + r.W - 16 - cx;
        Text(L.Get("VS UI elements"), sx, y + 7, 13, white);
        float bw = (cw - 12) / 3;
        if (Button(L.Get("SCORE"), new(cx, y, bw, 30), active: p.GameUiScore, key: "settings-vsui-score")) p.GameUiScore = !p.GameUiScore;
        if (Button(L.Get("EX SCORE"), new(cx + bw + 6, y, bw, 30), active: p.GameUiExScore, key: "settings-vsui-ex")) p.GameUiExScore = !p.GameUiExScore;
        if (Button(L.Get("HOLD FX"), new(cx + (bw + 6) * 2, y, bw, 30), active: p.GameUiHoldEffects, key: "settings-vsui-hold")) p.GameUiHoldEffects = !p.GameUiHoldEffects;

        y += 42;
        float splitW = (r.W - 38) / 2;
        Text(L.Get("TOP COMBO READOUT"), sx, y, 10, muted, true);
        Text(L.Get("JUDGEMENT POPUP"), sx + splitW + 10, y, 10, muted, true);
        if (Button(L.Get(ViewerSettings.ComboModes[p.GameUiCombo]), new(sx, y + 16, splitW, 29), active: p.GameUiCombo != 0, key: "settings-vsui-combo"))
            p.GameUiCombo = (p.GameUiCombo + 1) % ViewerSettings.ComboModes.Length;
        if (Button(L.Get(ViewerSettings.JudgementModes[p.GameUiJudgement]), new(sx + splitW + 10, y + 16, splitW, 29), active: p.GameUiJudgement != 0, key: "settings-vsui-judge"))
            p.GameUiJudgement = (p.GameUiJudgement + 1) % ViewerSettings.JudgementModes.Length;

        y += 64;
        Text(L.Get("Info card size"), sx, y + 7, 13, white);
        float quarter = (cw - 18) / 4;
        for (int i = 0; i < ViewerSettings.CardWidths.Length; i++)
        {
            int cardWidth = ViewerSettings.CardWidths[i];
            string name = cardWidth == 1280 ? "720P" : cardWidth == 1920 ? "1080P" : cardWidth == 2560 ? "1440P" : "4K";
            if (Button(name, new(cx + i * (quarter + 6), y, quarter, 29), active: CardWidth == cardWidth, key: "settings-card-size:" + cardWidth))
            { preferences.CardWidth = cardWidth; SaveSettings(); }
        }
        if (r.H >= 208)
            SettingsHint("Master switch is the VS UI button in the top bar. Sprites come from the installed Game UI pack.", sx, r.Y + r.H - 23, r.W - 28);
    }

    void DrawSettingsExport(Rect r)
    {
        SettingsCard(r, "Export");
        float sx = r.X + 14, y = r.Y + 42;
        float toggleX = r.X + r.W - 86;
        Text(L.Get("Put Image Gimmick Into Assets folder"), sx, y + 6, 12, white, max: toggleX - sx - 8);
        if (Button(preferences.PutImageGimmickIntoAssetsFolder ? L.Get("ON") : L.Get("OFF"), new(toggleX, y, 70, 28),
            active: preferences.PutImageGimmickIntoAssetsFolder, key: "settings-image-assets-folder"))
        { preferences.PutImageGimmickIntoAssetsFolder = !preferences.PutImageGimmickIntoAssetsFolder; SaveSettings(); }
        if (r.H >= 102)
        {
            Text(L.Get("Overwrite exports"), sx, y + 34, 12, white);
            if (Button(preferences.OverwriteExports ? L.Get("ON") : L.Get("OFF"), new(toggleX, y + 31, 70, 28),
                active: preferences.OverwriteExports, key: "settings-overwrite-exports"))
            { preferences.OverwriteExports = !preferences.OverwriteExports; SaveSettings(); }
        }
    }

    void DrawSettingsPerformance(Rect r, bool compact)
    {
        SettingsCard(r, "Performance");
        float sx = r.X + 14;
        float cx = compact ? sx : r.X + MathF.Min(192, r.W * .32f);
        float cw = r.X + r.W - 16 - cx;
        float y = r.Y + (compact ? 55 : 47);
        Text(L.Get("Calculation Engine"), sx, compact ? y - 18 : y + 7, 13, white, max: compact ? cw : cx - sx - 8);
        float half = (cw - 6) / 2;
        if (Button("OPTIMIZATION (RUST)", new(cx, y, half, 30), active: preferences.CalculationEngine == "optimization", key: "engine-rust"))
        { preferences.CalculationEngine = "optimization"; SaveSettings(); }
        if (Button("COMPATIBILITY (C#)", new(cx + half + 6, y, half, 30), active: preferences.CalculationEngine == "compatibility", key: "engine-managed"))
        { preferences.CalculationEngine = "compatibility"; SaveSettings(); }

        y += compact ? 62 : 76;
        Text(L.Get("Audio Engine"), sx, compact ? y - 18 : y + 7, 13, white, max: compact ? cw : cx - sx - 8);
        if (Button("OPTIMIZATION (RUST)", new(cx, y, half, 30), active: preferences.AudioEngine == "optimization", key: "audio-rust"))
        { preferences.AudioEngine = "optimization"; SaveSettings(); }
        if (Button("NVORBIS (LEGACY)", new(cx + half + 6, y, half, 30), active: preferences.AudioEngine == "nvorbis", key: "audio-legacy"))
        { preferences.AudioEngine = "nvorbis"; SaveSettings(); }

        y += compact ? 62 : 75;
        Text(L.Get("Parallel Loading"), sx, compact ? y - 18 : y + 7, 13, white, max: compact ? cw : cx - sx - 8);
        if (Button(preferences.ParallelLoading ? L.Get("ENABLED") : L.Get("DISABLED"), new(cx, y, MathF.Min(155, cw), 30),
            active: preferences.ParallelLoading, key: "parallel-loading"))
        { preferences.ParallelLoading = !preferences.ParallelLoading; SaveSettings(); }

        y += compact ? 62 : 75;
        Text(L.Get("Decoded Audio Cache"), sx, compact ? y - 18 : y + 7, 13, white, max: compact ? cw : cx - sx - 8);
        if (Button(preferences.DecodedAudioCache ? L.Get("ENABLED") : L.Get("DISABLED"), new(cx, y, MathF.Min(155, cw), 30),
            active: preferences.DecodedAudioCache, key: "audio-cache"))
        { preferences.DecodedAudioCache = !preferences.DecodedAudioCache; SaveSettings(); }
        y += 42;
        if (compact)
            SettingsHint("Changes apply when opening the next chart. Debug environment overrides take priority.", sx, y, r.W - 28);
        else
        {
            SettingsHint("Changes apply when opening the next chart. Debug environment overrides take priority.", sx, y, r.W - 28);
            SettingsHint("Memory + disk PCM cache; stored in the application cache directory.", sx, y + 24, r.W - 28);
        }
        if (!compact && r.H >= 430)
            if (Button(L.Get("RESET PERFORMANCE"), new(sx, MathF.Min(y + 85, r.Y + r.H - 56), 205, 32), key: "performance-reset"))
            {
                preferences.CalculationEngine = "optimization";
                preferences.AudioEngine = "optimization";
                preferences.ParallelLoading = true;
                preferences.DecodedAudioCache = true;
                SaveSettings();
            }
    }
}
