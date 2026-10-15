using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Timeline? picturedTimeline;
    SpriteDialogueState? picturedObjects;

    private SpriteDialogueState? PreparePictured(Session session)
    {
        if (!session.NativeGimmick.HasBehavior("sprite-dialogue")) return null;
        Prepare(session);
        if (!ReferenceEquals(picturedTimeline, session.Timeline))
        {
            picturedTimeline = session.Timeline;
            picturedObjects = new(session.Timeline);
        }
        return picturedObjects;
    }

    private void PicturedSprite(string name, double frame, double x, double y, double scale = 1, double alpha = 1)
    {
        if (current == null || alpha <= 0 || scale == 0 || !double.IsFinite(frame) || !double.IsFinite(scale)
            || !double.IsFinite(x) || !double.IsFinite(y) || !current.Sprites.TryGetValue(name, out var sprite)) return;
        int index = (int)Math.Floor((frame % sprite.Frames.Count + sprite.Frames.Count) % sprite.Frames.Count);
        if (Image(name, index) is not { } image) return;
        canvas.Quad(image, new((float)(x - sprite.OriginX * scale), (float)(y - sprite.OriginY * scale),
            (float)(sprite.Width * scale), (float)(sprite.Height * scale)), Color.White.Alpha(alpha));
    }

    /// <summary>o_pictured_2_pics is at depth 750, behind the source depth701 ambient particles and depth700 jacket.</summary>
    public void DrawSpriteDialogueBackground(Session session, double time)
    {
        if (PreparePictured(session) is not { } state) return;
        var picture = state.PictureAt(time);
        if (picture.Visible) PicturedSprite("sp_pictured_2_pics", picture.Frame, 0, 0);
        canvas.Flush();
    }

    /// <summary>Negative-depth normal Draw events, after source normal HUD and before Draw GUI Begin's post pass.</summary>
    public void DrawSpriteDialogueForeground(Session session, double time)
    {
        if (PreparePictured(session) is not { } state) return;
        double M(string name) => session.Timeline.Get(name, time);
        PicturedSprite("sp_pictured_3_holdguard", 0, 0, 144, alpha: M("3_pinkbg_alph"));
        double size = M("2_dialbox_size"), circles = M("2_dialbox_circles");
        PicturedSprite("sp_pictured_2_dialbox", 0, 160, 170, size);
        if (circles > -1)
        {
            for (int i = 0; i < 12; i++) PicturedSprite("sp_pictured_2_circ", 0, 89 + 12 * i, 170, size);
            for (int i = 1; i < circles + 1 && i <= 4096; i++) PicturedSprite("sp_pictured_2_circ", 1, 89 + 12 * (i - 1), 170, size);
        }
        if (current?.Data?.SourceTextTables.TryGetValue("pictured-dialbox", out var speech) == true)
        {
            double index = M("2_dialbox_speech");
            if (index > 0 && index < speech.Length + 1)
            {
                string text = speech[(int)index - 1];
                // Source texture-page crops are width2 for letters and width1
                // for blank space/comma frames. font_add_sprite adds sep2 to
                // every glyph, including the final one in string_width.
                double Shift(char letter) => letter is ' ' or ',' ? 3 : 4;
                double pen = 186 - text.Sum(Shift) / 2;
                foreach (char letter in text)
                {
                    if (letter != ' ') PicturedSprite("sp_pictured_font", letter - 32, pen, 167);
                    pen += Shift(letter);
                }
            }
        }
        int first = SortedSearch.LowerBound(state.Pulses, time - 1.0 / 3, x => x);
        for (int i = first; i < state.Pulses.Count && state.Pulses[i] <= time; i++)
        {
            double ticks = Math.Floor((time - state.Pulses[i]) * 60) + 1;
            double alpha = 1 - .06 * ticks;
            if (alpha <= 0) continue;
            PicturedSprite("sp_pictured_2_pulsar", 0, 80 - 2 * ticks, 166, alpha: alpha);
            PicturedSprite("sp_pictured_2_pulsar", 0, 228 + 2 * ticks, 166, alpha: alpha);
        }
        PicturedSprite("sp_pictured_1_speech", M("1_speech"), 160, 90);
        if (time < state.CubeDeath)
            foreach (var batch in state.CubeBatches)
            {
                if (batch.Time > time) break;
                for (int i = 0; i < batch.Count; i++)
                {
                    uint seed = unchecked(batch.Seed + (uint)i * 29);
                    double x = Timeline.Hash(seed) * 320, y = Timeline.Hash(seed + 1) * 180;
                    for (int pixel = 0; pixel < 4; pixel++)
                        PicturedSprite("sp_pictured_pixel", Math.Min(2, (int)(Timeline.Hash(seed + 2 + (uint)pixel) * 3)),
                            x + pixel % 2, y + pixel / 2);
                }
            }
        PicturedSprite("sp_pictured_wipe", 0, 0, 0, alpha: M("c_flash"));
        canvas.Flush();
    }
}
