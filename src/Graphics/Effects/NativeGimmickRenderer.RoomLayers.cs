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
    public void DrawRoomLayers(Target scene, Session session, double time, int minimumDepth = int.MinValue, int maximumDepth = int.MaxValue)
    {
        Prepare(session);
        if (current?.Data is not { } definition || definition.RoomLayers.Count == 0) return;
        canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
        foreach (var layer in definition.RoomLayers.Where(l => l.Depth > minimumDepth && l.Depth <= maximumDepth).OrderByDescending(l => l.Depth))
        {
            foreach(var drawing in layer.Drawings)Draw(session,drawing,time,time,-1);
            if(layer.Drawings.Count>0 && layer.Sprite==null)continue;
            double alpha = GimmickRoomLayerTimeline.Alpha(layer, session.Timeline, time);
            if (alpha <= 0) continue;
            if (layer.FilterOnly)
            {
                canvas.Flush();
                if (layer.Effect is { } fullLayer && ApplyShader(filtered, scene.Texture, session, time, fullLayer))
                    canvas.Pass(scene, filtered.Texture, canvas.Basic);
                canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
                continue;
            }
            var tint = Color.GameMaker(layer.ColorBgr).Alpha(alpha);
            if (layer.Sprite is not { Length: > 0 })
            {
                // Source room colors are packed GameMaker BGR, not RGB.
                canvas.Fill(new(0, 0, 320, 180), tint);
                continue;
            }
            if (!current.Sprites.TryGetValue(layer.Sprite, out var spec))continue;
            string selectedSprite=layer.Sprite;
            int selectedFrame=GimmickRoomLayerTimeline.Frame(layer,session.Timeline,time,spec.Frames.Count);
            if(definition.Console is { } stopmotion && layer.FrameSelectionEvent is { } selectionEvent)
            {
                var selected=OperationConsoleState.Background(stopmotion,selectedSprite,selectedFrame,
                    selectionEvent,session.Timeline,time,layer.FrameSelectionSeed);
                selectedSprite=selected.Sprite;selectedFrame=selected.Frame;
                if(!current.Sprites.TryGetValue(selectedSprite,out spec))continue;
            }
            if (Image(selectedSprite, selectedFrame) is not { } img)
                continue;
            float w = layer.Stretch ? 320 : spec.Width;
            float h = layer.Stretch ? 180 : spec.Height;
            if (w <= 0 || h <= 0) continue;
            double x = GimmickRoomLayerTimeline.X(layer, session.Timeline, time);
            double y = GimmickRoomLayerTimeline.Y(layer, session.Timeline, time);
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue) continue;
            // The room's offset does NOT scroll the source pixels: the whole tile
            // grid translates and wraps. Do not reset offsets on every redraw.
            float left = layer.TileHorizontal ? (float)(GimmickRoomLayerTimeline.Wrap(x, w) - w) : (float)x;
            float top = layer.TileVertical ? (float)(GimmickRoomLayerTimeline.Wrap(y, h) - h) : (float)y;
            int horizontal = layer.TileHorizontal ? Math.Min(2048, (int)Math.Ceiling((320 - left) / w) + 1) : 1;
            int vertical = layer.TileVertical ? Math.Min(2048, (int)Math.Ceiling((180 - top) / h) + 1) : 1;
            if (layer.Effect != null)
            {
                canvas.Flush();
                canvas.Begin(background, background.Texture.Width, background.Texture.Height, 320, 180, new(0, 0, 0, 0));
                tint = Color.GameMaker(layer.ColorBgr);
            }
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
            if (layer.Effect is { } effect)
            {
                canvas.Flush();
                bool available = ApplyShader(filtered, background.Texture, session, time, effect, preserveAlpha:true);
                canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
                if (available) canvas.Quad(filtered.Texture, new(0, 0, 320, 180), Color.White.Alpha(alpha), shader:roomLayerComposite);
                canvas.Flush();
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
        DrawMaskedStarFieldMaskObjects(session, time);
        canvas.Flush();
        canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
        // GameMaker: gpu_set_blendmode_ext(bm_dest_color, bm_zero); draw_surface(mask,0,0).
        canvas.Blend(BlendFactor.DestinationColor, BlendFactor.Zero);
        canvas.Quad(mask.Texture, new(0, 0, 320, 180), Color.White);
        canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        canvas.Flush();
    }
}
