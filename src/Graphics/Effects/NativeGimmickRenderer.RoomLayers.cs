using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>Generic declarative room backgrounds and simple inverse-visibility mask source.
/// Not a reimplementation of the Starcrashers animated game objects.</summary>
public sealed partial class NativeGimmickRenderer
{
    /// <summary>
    /// Render the actual native background layers in GameMaker depth order.
    /// Their visibility alpha is evaluated from the current Mod timeline, rather
    /// than assuming that extracting a sprite means it must always be visible.
    /// The chorus foreground scrolls -60 logical pixels/sec, per original Step_1;
    /// all other original bg_* layers are static and cross-fade via AlphaMod.
    /// </summary>
    public void DrawRoomLayers(Target scene, Session session, double time)
    {
        Prepare(session);
        if (current?.Data is not { } definition || definition.RoomLayers.Count == 0) return;
        canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
        foreach (var layer in definition.RoomLayers.OrderByDescending(l => l.Depth))
        {
            double alpha = GimmickRoomLayerTimeline.Alpha(layer, session.Timeline, time);
            if (alpha <= 0) continue;
            var tint = Color.GameMaker(layer.ColorBgr).Alpha(alpha);
            if (layer.Sprite is not { Length: > 0 })
            {
                // Source room colors are packed GameMaker BGR, not RGB.
                canvas.Fill(new(0, 0, 320, 180), tint);
                continue;
            }
            if (!current.Sprites.TryGetValue(layer.Sprite, out var spec) || Image(layer.Sprite) is not { } img)
                continue;
            float w = layer.Stretch ? 320 : spec.Width;
            float h = layer.Stretch ? 180 : spec.Height;
            if (w <= 0 || h <= 0) continue;
            double x = GimmickRoomLayerTimeline.X(layer, time);
            double y = GimmickRoomLayerTimeline.Y(layer, time);
            // The room's offset does NOT scroll the source pixels: the whole tile
            // grid translates and wraps. Do not reset offsets on every redraw.
            float left = layer.TileHorizontal ? (float)(GimmickRoomLayerTimeline.Wrap(x, w) - w) : (float)x;
            float top = layer.TileVertical ? (float)(GimmickRoomLayerTimeline.Wrap(y, h) - h) : (float)y;
            int horizontal = layer.TileHorizontal ? Math.Min(2048, (int)Math.Ceiling((320 - left) / w) + 1) : 1;
            int vertical = layer.TileVertical ? Math.Min(2048, (int)Math.Ceiling((180 - top) / h) + 1) : 1;
            for (int iy = 0; iy < vertical; iy++)
            {
                float py = top + iy * h;
                if (py >= 180 || py + h <= 0) continue;
                for (int ix = 0; ix < horizontal; ix++)
                {
                    float px = left + ix * w;
                    if (px >= 320 || px + w <= 0) continue;
                    canvas.Quad(img, new(px, py, w, h), tint);
                }
            }
        }
        canvas.Flush();
    }

    public void DrawMask(Target scene, Session session, double time)
    {
        Prepare(session);
        if (current?.Data is not { } definition || definition.MaskLayers.Count == 0) return;
        canvas.Begin(mask, mask.Texture.Width, mask.Texture.Height, 320, 180, new(0, 0, 0));
        foreach (var drawing in definition.MaskLayers)
            Draw(session, drawing, time, time, -1);
        DrawStarcrashersMaskObjects(session, time);
        canvas.Flush();
        canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
        // GameMaker: gpu_set_blendmode_ext(bm_dest_color, bm_zero); draw_surface(mask,0,0).
        canvas.Blend(BlendFactor.DestinationColor, BlendFactor.Zero);
        canvas.Quad(mask.Texture, new(0, 0, 320, 180), Color.White);
        canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        canvas.Flush();
    }
}
