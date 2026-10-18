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
            OptimizationEngineSettings.Apply(preferences);
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

    // Settings navigation is UI-local. Changing pages never rebuilds the current Session.
    string settingsSection = "general";

    void DrawSettings(int width, int height)
    {
        settingsInput = true;
        try
        {
            // Keep a comfortable gap from the viewport edge. Focused pages remain usable in small windows.
            float pw = MathF.Min(1230, width - 24);
            float ph = MathF.Min(840, height - 24);
            var r = new Rect((width - pw) / 2, (height - ph) / 2 + (1 - settingsAlpha) * 10, pw, ph);
            Canvas.Fill(new(0, 0, width, height), Color.Hex(0, .83f));
            Canvas.Fill(r, panel);
            Canvas.Border(r, soft);
            Text(L.Get("KUROAKI / SETTINGS"), r.X + 22, r.Y + 20, 21, white, true);
            Text(L.Get("Applied immediately. Saved for the next launch."), r.X + 22, r.Y + 54, 12, muted, max: r.W - 44);

            float navX = r.X + 14, navY = r.Y + 90, navW = 164;
            (string Key, string Name)[] entries =
            [
                ("general", "General"), ("audio", "Audio & Sync"),
                ("display", "Display"), ("editor", "Editor"), ("vsui", "VS UI"),
                ("performance", "Performance"), ("export", "Export")
            ];
            for (int i = 0; i < entries.Length; i++)
            {
                var item = entries[i];
                if (Button(L.Get(item.Name), new(navX, navY + i * 53, navW, 43),
                    active: settingsSection == item.Key, key: "settings-nav:" + item.Key))
                    settingsSection = item.Key;
            }

            float x = navX + navW + 14;
            float y = r.Y + 90;
            float w = r.X + r.W - 14 - x;
            float bottom = r.Y + r.H - 70;
            bool dashboard = settingsSection == "general" && w >= 800 && bottom - y >= 664;
            if (dashboard)
            {
                float gap = 12;
                float leftW = MathF.Floor((w - gap) * .52f);
                float rightW = w - gap - leftW;
                float rx = x + leftW + gap;
                DrawSettingsInterface(new(x, y, leftW, 190));
                DrawSettingsAudio(new(rx, y, rightW, 190));
                DrawSettingsDisplay(new(x, y + 202, leftW, 124));
                DrawSettingsWorkflow(new(rx, y + 202, rightW, 124));
                DrawSettingsVsUi(new(x, y + 338, leftW, 210));
                DrawSettingsPerformance(new(rx, y + 338, rightW, 326), compact: true);
                DrawSettingsExport(new(x, y + 560, leftW, 104));
            }
            else
            {
                // Sidebar navigation has real destinations rather than decorative highlighting.
                // Small windows use one focused panel instead of clipping the dashboard columns.
                var focus = new Rect(x, y, w, MathF.Max(310, bottom - y - 4));
                switch (settingsSection)
                {
                    case "audio": DrawSettingsAudio(focus); break;
                    case "display": DrawSettingsDisplay(new(x, y, w, 135)); DrawSettingsWorkflow(new(x, y + 148, w, 135)); break;
                    case "editor": DrawSettingsEditor(focus); break;
                    case "vsui": DrawSettingsVsUi(focus); break;
                    case "performance": DrawSettingsPerformance(focus, compact: focus.H < 430); break;
                    case "export": DrawSettingsExport(focus); break;
                    default: DrawSettingsInterface(focus); break;
                }
            }

            Divider(x, r.Y + r.H - 63, w);
            if (Button(L.Get("RESET DEFAULTS"), new(x, r.Y + r.H - 50, 190, 32), key: "settings-reset"))
            {
                var p = Current.Project;
                preferences.UiAnimations = true;
                preferences.ModalValueEditor = false;
                preferences.GroupGimmickTracks = true; ResetGimmickTracks(); layoutRevision = -1;
                preferences.ShowExpandedGimmickSummary = false;
                preferences.SplitOverlappingTimelineClips = true;
                preferences.UnlimitedChartSize = false;
                preferences.AlwaysFollow = false;
                preferences.OverwriteExports = false;
                preferences.VideoRenderWorkers = 1;
                preferences.PutImageGimmickIntoAssetsFolder = false;
                preferences.UiTheme = "Nekomiya";
                preferences.CardWidth = new ViewerSettings().CardWidth;
                SetUiLanguage(UiLanguage.Auto, persist: false);
                new ViewerSettings().Apply(p);
                audioMuted = false;
                transport.SetVolume(p.PreviewVolume);
                transport.SetDelay(p.AudioDelayMs);
                SaveSettings();
            }
            if (Button(L.Get("DONE"), new(r.X + r.W - 178, r.Y + r.H - 50, 158, 32), primary: true, key: "settings-done"))
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
