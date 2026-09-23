using System.Text.Json;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    /// <summary>Exercise both languages without saving preferences or changing the scene.</summary>
    public void SmokeLocalization()
    {
        string language = preferences.UiLanguage;
        bool animations = preferences.UiAnimations, wasSettings = settings, wasEditor = editorMode;
        var session = Current;
        double position = transport.Position;
        string project = JsonSerializer.Serialize(Current.Project, ViewerProject.Json);
        string? vsm = editor?.Vsm.Text;
        preferences.UiAnimations = false;
        try
        {
            Dictionary<string, float> widths = [];
            foreach (string next in new[] { UiLanguage.English, UiLanguage.Chinese, UiLanguage.English })
            {
                SetUiLanguage(next, persist: false);
                if (L.Language != next || L.Get("PLAY") != (next == UiLanguage.Chinese ? "播放" : "PLAY"))
                    throw new InvalidOperationException("UI language did not update immediately.");
                if (fonts.ChineseInterface != (next == UiLanguage.Chinese))
                    throw new InvalidOperationException("Font did not follow the resolved UI language.");
                const string sample = "KUROAKI 0123456789";
                foreach (bool mono in new[] { false, true })
                {
                    foreach (float fontSize in new[] { 13f, 24f })
                    {
                        float measured = fonts.Measure(sample, fontSize, monospaced: mono);
                        if (measured <= 0)
                        {
                            throw new InvalidOperationException("UI text measured to nothing.");
                        }
                        // 中英文共用同一份字体，换界面语言不应该改变任何一段文字的宽度。
                        string widthKey = mono + "|" + fontSize;
                        if (widths.TryGetValue(widthKey, out float first) && Math.Abs(first - measured) > .001f)
                        {
                            throw new InvalidOperationException("UI text width changed with the interface language.");
                        }
                        widths[widthKey] = measured;
                        // 步进来自 em 归一化的字体度量，因此宽度对字号严格线性；
                        // 如果它改为依赖某个栅格像素尺寸，这里就会失败。
                        if (Math.Abs(fonts.Measure(sample, fontSize * 2, monospaced: mono) - measured * 2) > .001f)
                        {
                            throw new InvalidOperationException("UI text width was not linear in font size.");
                        }
                        // 等宽请求要保证的是数字列对齐：任意数字串与等长的 "0" 串同宽，
                        // 卡片统计面板靠它右对齐。更宽的字形（汉字整格一个 em）保留自身宽度，
                        // 否则相邻的字会叠在一起。
                        if (mono && Math.Abs(fonts.Measure("0123456789", fontSize, monospaced: true)
                                - fonts.Measure("0000000000", fontSize, monospaced: true)) > .001f)
                        {
                            throw new InvalidOperationException("Monospaced digits did not share a uniform advance.");
                        }
                        if (mono && fonts.Measure("中", fontSize, monospaced: true)
                                < fonts.Measure("0", fontSize, monospaced: true))
                        {
                            throw new InvalidOperationException("Monospacing squeezed a full-em glyph into the digit cell.");
                        }
                    }
                }
                foreach (var size in new[] { (1180, 860), (1440, 940) })
                {
                    settings = false;
                    Draw(size.Item1, size.Item2);
                    settings = true;
                    Draw(size.Item1, size.Item2);
                }
                if (!ReferenceEquals(session, Current) || transport.Position != position
                    || JsonSerializer.Serialize(Current.Project, ViewerProject.Json) != project || editor?.Vsm.Text != vsm)
                    throw new InvalidOperationException("Language switching changed the song, playback or editor document.");
            }
        }
        finally
        {
            SetUiLanguage(language, persist: false);
            preferences.UiAnimations = animations;
            settings = wasSettings;
            editorMode = wasEditor;
        }
    }
}
