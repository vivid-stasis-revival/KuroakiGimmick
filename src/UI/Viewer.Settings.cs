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
    void SetUiLanguage(string language, bool persist = true)
    {
        preferences.UiLanguage = UiLanguage.Normalize(language);
        L.SetLanguage(preferences.UiLanguage);
        fonts.SetInterfaceLanguage(L.Language);
        RequestNativeMenuRefresh();
        // Cached help and track labels must be measured again in the selected language.
        cachedHelpKey = manualCacheKey = referenceLayoutKey = "";
        layoutRevision = -1;
        message = L.Get("Interface language updated.");
        if (persist) SaveSettings();
        click = false;
    }

    /// <summary>把当前 project 的预览偏好写回设置文件。IO / 权限失败只提示不抛出，设置落盘失败不能打断交互。</summary>
    void SaveSettings()
    {
        try
        {
            preferences.Save(Current.Project, settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = L.Get("Settings could not be saved: ") + ex.Message;
        }
    }

    string SuggestedExportPath(string kind, string filename)
    {
        string directory = preferences.ExportDirectory(kind);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, filename);
    }

    void RememberExportDestination(string kind, string path)
    {
        preferences.RememberExportDestination(kind, path);
        try { preferences.Persist(settingsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { message = L.Get("Settings could not be saved: ") + ex.Message; }
    }

    /// <summary>修改当前项目的预览偏好；字体/音量等设置不应触发谱面解析或重置播放进度。</summary>
    void DrawSettings(int width, int height)
    {
        // 面板内按钮只在 settingsInput 为真时通过 Button 门控；finally 保证异常时也复位。
        settingsInput = true;
        try
        {
            var p = Current.Project;
            var r = new Rect(width / 2 - 340, height / 2 - 444 + (1 - settingsAlpha) * 10, 680, 888);
            Canvas.Fill(new(0, 0, width, height), Color.Hex(0, .83f));
            Canvas.Fill(r, panel);
            Canvas.Border(r, soft);
            Text(L.Get("KUROAKI / SETTINGS"), r.X + 30, r.Y + 25, 21, white, true);
            Text(L.Get("Applied immediately. Saved for the next launch."), r.X + 30, r.Y + 57, 12, muted, max: 310);
            Text(L.Get("Language"), r.X + 356, r.Y + 17, 12, muted);
            string[] languages = [UiLanguage.Auto, UiLanguage.Chinese, UiLanguage.English];
            string[] languageNames = [L.Get("SYSTEM"), "中文", "English"];
            for (int i = 0; i < languages.Length; i++)
                if (Button(languageNames[i], new(r.X + 356 + i * 100, r.Y + 39, 94, 29),
                    active: preferences.UiLanguage == languages[i], key: "settings-language:" + languages[i]))
                    SetUiLanguage(languages[i]);
            float label = r.X + 30, control = r.X + 265;
            Text(L.Get("Text Font"), label, r.Y + 103, 14, white);
            // 字体、对齐、分辨率都只改 project 字段，渲染路径每帧读取即可生效；不要在这里调用 Rebuild / Reload。
            if (Button(L.Get("DEFAULT"), new(control, r.Y + 91, 155, 32), active: p.GameUiFont == ViewerSettings.DefaultFont))
            {
                p.GameUiFont = ViewerSettings.DefaultFont;
            }
            bool hasMonaco = Current.GameUi.Data?.Fonts.ContainsKey(ViewerSettings.MonacoFont) == true;
            if (Button("MONACO", new(control + 165, r.Y + 91, 155, 32), active: p.GameUiFont == ViewerSettings.MonacoFont, enabled: hasMonaco))
            {
                p.GameUiFont = ViewerSettings.MonacoFont;
            }
            Text(hasMonaco ? L.Get("Game text / HUD. Image lettering stays as authored.") : L.Get("Monaco is missing from the selected Game UI pack."), label,
                r.Y + 134, 11, muted);
            r = r with
            {
                Y = r.Y + 60, H = r.H - 60
            };
            Text(L.Get("Note Alignment"), label, r.Y + 103, 14, white);
            if (Button(L.Get("TOP"), new(control, r.Y + 91, 155, 32), active: p.NoteAlignment == 0))
            {
                p.NoteAlignment = 0;
            }
            if (Button(L.Get("BOTTOM"), new(control + 165, r.Y + 91, 155, 32), active: p.NoteAlignment == 1))
            {
                p.NoteAlignment = 1;
            }
            // 选项命名的是"音符的哪条边碰到判定线"，不是音符摆在哪：原版 note_alignment_offset = 3 - 7 * op_note_alignment，
            // 所以 TOP 反而把音符往下推七像素。不写出来几乎所有人都会以为这两个标签装反了。
            Text(L.Get("Which note edge meets the judgement line. TOP sits 7 px lower."), control, r.Y + 127, 10, muted, max: r.W - 295);
            Text(L.Get("Render resolution"), label, r.Y + 153, 14, white);
            Text(L.Get("320 x 180 = VS native"), label, r.Y + 178, 11, muted);
            for (int i = 0; i < ViewerSettings.Widths.Length; i++)
            {
                int size = ViewerSettings.Widths[i];
                if (Button($"{size} x {size*9/16}", new(control + i % 3 * 108, r.Y + 142 + i / 3 * 38, 103, 32), active: p.RenderWidth == size))
                {
                    p.RenderWidth = size;
                }
            }
            Text(L.Get("Audio volume"), label, r.Y + 244, 14, white);
            var slider = new Rect(control, r.Y + 244, 250, 18);
            Canvas.Fill(new(slider.X, slider.Y + 7, slider.W, 4), line);
            Canvas.Fill(new(slider.X, slider.Y + 7, (float) p.PreviewVolume * slider.W, 4), red);
            if (settings && !UiClosingOverlay && held && new Rect(slider.X, slider.Y - 8, slider.W, 32).Contains(mouseX, mouseY))
            {
                SetVolume((mouseX - slider.X) / slider.W);
            }
            Text($"{p.PreviewVolume*100:0}%", control + 265, r.Y + 244, 13, white, true);
            void Delay(string title, float y, bool audio)
            {
                double value = audio ? p.AudioDelayMs : p.VisualDelayMs;
                Text(title, label, y + 10, 14, white);
                Text($"{value:+0;-0;0} ms", control + 104, y + 9, 14, white, true);
                int[] steps = [-10, -1, 1, 10];
                float[] xs = [control, control + 48, control + 224, control + 272];
                for (int i = 0; i < steps.Length; i++)
                {
                    if (Button(steps[i].ToString("+0;-0"), new(xs[i], y, 43, 31)))
                    {
                        // 单位 ms，范围 ±2000。音频延迟由 transport 缓存，改 project 后必须同步 SetDelay；
                        // 视觉延迟由 SceneRenderer 每帧从 project 读取，只改字段即可。
                        value = Math.Clamp(value + steps[i], -2000, 2000);
                        if (audio)
                        {
                            p.AudioDelayMs = value;
                            transport.SetDelay(value);
                        }
                        else
                        {
                            p.VisualDelayMs = value;
                        }
                    }
                }
            }
            Delay(L.Get("Audio delay"), r.Y + 296, true);
            Delay(L.Get("Visual delay"), r.Y + 351, false);
            Text(L.Get("Positive delay = later. Audio calibration / volume affect preview."), label, r.Y + 404, 12, muted);
            Text(L.Get("Visual delay shifts notes; gimmick timing stays on the song clock."), label, r.Y + 424, 12, muted);
            Text(L.Get("UI transitions"), label, r.Y + 466, 14, white);
            if (Button(preferences.UiAnimations ? L.Get("ON") : L.Get("OFF"), new(control, r.Y + 457, 78, 32), active: preferences.UiAnimations, key: "settings-ui-motion"))
                preferences.UiAnimations = !preferences.UiAnimations;
            // 编辑器 inspector 的字段编辑方式。默认就地改（带光标、可框选）；这里可以换回旧的全屏输入框。
            if (Button(preferences.ModalValueEditor ? L.Get("MODAL FIELDS") : L.Get("INLINE FIELDS"), new(control + 88, r.Y + 457, 140, 32),
                active: preferences.ModalValueEditor, key: "settings-modal-fields"))
                preferences.ModalValueEditor = !preferences.ModalValueEditor;
            // 播放中手动平移时间轴时 FOLLOW 的去留。默认平移即让位；打开则永远滑回播放头。
            if (Button(preferences.AlwaysFollow ? L.Get("ALWAYS FOLLOW") : L.Get("AUTO RELEASE"), new(control + 238, r.Y + 457, 147, 32),
                active: preferences.AlwaysFollow, key: "settings-always-follow"))
                preferences.AlwaysFollow = !preferences.AlwaysFollow;
            Text(L.Get("UI theme"), label, r.Y + 512, 14, white);
            string[] themeNames = ViewerSettings.UiThemes;
            for (int i = 0; i < themeNames.Length; i++)
            {
                string themeName = themeNames[i];
                if (Button(themeName.ToUpperInvariant(), new(control + i * 108, r.Y + 502, 103, 32),
                    active: string.Equals(preferences.UiTheme, themeName, StringComparison.OrdinalIgnoreCase), key: "settings-theme:" + themeName))
                    SetUiTheme(themeName);
            }
            Text(L.Get("Nekomiya = current default · Scarlet = legacy · Kuroaki = high-contrast red/black"), label, r.Y + 541, 11, muted, max: r.W - 60);
            // 以下五项复刻 vivid/stasis 的游戏内 HUD，总开关仍是顶栏的 VS UI 按钮；这里只调各元素的开关与样式。
            Text(L.Get("VS UI elements"), label, r.Y + 583, 14, white);
            if (Button(L.Get("SCORE"), new(control, r.Y + 573, 103, 32), active: p.GameUiScore, key: "settings-vsui-score"))
            {
                p.GameUiScore = !p.GameUiScore;
            }
            if (Button(L.Get("EX SCORE"), new(control + 108, r.Y + 573, 103, 32), active: p.GameUiExScore, key: "settings-vsui-ex"))
            {
                p.GameUiExScore = !p.GameUiExScore;
            }
            if (Button(L.Get("HOLD FX"), new(control + 216, r.Y + 573, 103, 32), active: p.GameUiHoldEffects, key: "settings-vsui-hold"))
            {
                p.GameUiHoldEffects = !p.GameUiHoldEffects;
            }
            Label(L.Get("TOP COMBO READOUT"), label, r.Y + 615);
            Label(L.Get("JUDGEMENT POPUP"), label + 320, r.Y + 615);
            // 两个多档选项按点击循环，对应游戏里的 op_minusscore / 判定显示样式。
            if (Button(L.Get(ViewerSettings.ComboModes[p.GameUiCombo]), new(label, r.Y + 631, 300, 32), active: p.GameUiCombo != 0, key: "settings-vsui-combo"))
            {
                p.GameUiCombo = (p.GameUiCombo + 1) % ViewerSettings.ComboModes.Length;
            }
            if (Button(L.Get(ViewerSettings.JudgementModes[p.GameUiJudgement]), new(label + 320, r.Y + 631, 300, 32), active: p.GameUiJudgement != 0,
                key: "settings-vsui-judge"))
            {
                p.GameUiJudgement = (p.GameUiJudgement + 1) % ViewerSettings.JudgementModes.Length;
            }
            Text(L.Get("Master switch is the VS UI button in the top bar. Sprites come from the installed Game UI pack."), label, r.Y + 672, 11, muted,
                max: r.W - 60);
            // 信息卡片的导出分辨率。卡面按 1280x720 的逻辑坐标绘制，这里只决定输出的像素密度。
            Text(L.Get("Info card size"), label, r.Y + 704, 14, white);
            for (int i = 0; i < ViewerSettings.CardWidths.Length; i++)
            {
                int cardWidth = ViewerSettings.CardWidths[i];
                if (Button(cardWidth == 1280 ? "720P" : cardWidth == 1920 ? "1080P" : cardWidth == 2560 ? "1440P" : "4K",
                    new(control + i * 88, r.Y + 694, 83, 32), active: CardWidth == cardWidth, key: "settings-card-size:" + cardWidth))
                {
                    preferences.CardWidth = cardWidth;
                    SaveSettings();
                }
            }
            Text(L.Format($"Exported info cards are {CardWidth} x {CardWidth * 9 / 16}, always 16:9."), label, r.Y + 733, 11, muted, max: r.W - 60);
            Text(L.Get("Put Image Gimmick Into Assets folder"), label + 220, r.Y + 750, 11, white, max: 160);
            if (Button(preferences.PutImageGimmickIntoAssetsFolder ? L.Get("ON") : L.Get("OFF"), new(label + 385, r.Y + 740, 70, 30),
                active: preferences.PutImageGimmickIntoAssetsFolder, key: "settings-image-assets-folder"))
            {
                preferences.PutImageGimmickIntoAssetsFolder = !preferences.PutImageGimmickIntoAssetsFolder;
                SaveSettings();
            }
            Text(L.Get("Overwrite exports"), label + 220, r.Y + 782, 12, white, max: 155);
            if (Button(preferences.OverwriteExports ? L.Get("ON") : L.Get("OFF"), new(label + 385, r.Y + 772, 70, 34),
                active: preferences.OverwriteExports, key: "settings-overwrite-exports"))
            {
                preferences.OverwriteExports = !preferences.OverwriteExports;
                SaveSettings();
            }
            if (Button(L.Get("RESET DEFAULTS"), new(label, r.Y + 772, 205, 34)))
            {
                preferences.UiAnimations = true;
                preferences.ModalValueEditor = false;
                preferences.AlwaysFollow = false;
                preferences.OverwriteExports = false;
                preferences.PutImageGimmickIntoAssetsFolder = false;
                preferences.UiTheme = "Nekomiya";
                preferences.CardWidth = new ViewerSettings().CardWidth;
                SetUiLanguage(UiLanguage.Auto, persist: false);
                // 默认值只套到 project 上；transport 自己缓存音量和延迟，必须再同步一次。工作区布局不在此重置。
                new ViewerSettings().Apply(p);
                transport.SetVolume(p.PreviewVolume);
                transport.SetDelay(p.AudioDelayMs);
            }
            if (Button(L.Get("DONE"), new(r.X + r.W - 180, r.Y + 772, 150, 34), primary: true))
            {
                settings = false;
                SaveSettings();
            }
        }
        finally
        {
            settingsInput = false;
        }
    }
}
