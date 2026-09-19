// Read-only extraction for Custom FX compatibility. Output must be a new private directory.
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Globalization;
using UndertaleModLib.Util;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
string output = Environment.GetEnvironmentVariable("KUROAKI_FX_OUTPUT");
if (string.IsNullOrWhiteSpace(output) || Directory.Exists(output)) throw new Exception("Set KUROAKI_FX_OUTPUT to a new directory.");
Directory.CreateDirectory(output);
var context = new GlobalDecompileContext(Data);
foreach (var code in Data.Code.Where(c => c?.Name?.Content != null && c.ParentEntry == null))
{
    string name = code.Name.Content;
    if (name.Contains("o_unravel_sidething") || name.Contains("o_unravel_particle") || name.Contains("o_csm_df_handler") || name.Contains("obj_distortedfate_sideline") ||
        name.Contains("spawn_particles_directional") || name.Contains("o_pt_diamonddust") || name.Contains("TweenFire") ||
        name.Contains("obj_custom_gimmick") || name.Contains("_cc_") || name.Contains("init_customgmk") || name == "gml_Room_scene_gameplay_Create")
        File.WriteAllText(Path.Combine(output, name + ".gml"), new Underanalyzer.Decompiler.DecompileContext(context, code, new Underanalyzer.Decompiler.DecompileSettings()).DecompileToString());
}
var sprites = new Dictionary<string, object>();
var objectSprites = new HashSet<string>();
var objects = new List<object>();
foreach (var obj in Data.GameObjects.Where(o => o?.Name?.Content != null && (o.Name.Content.Contains("o_unravel_") || o.Name.Content.Contains("obj_distortedfate_sideline"))))
{
    string sprite = Str(Prop(Prop(obj, "Sprite"), "Name")); if (sprite.Length > 0) objectSprites.Add(sprite);
    objects.Add(new { name = obj.Name.Content, sprite, parent = Str(Prop(Prop(obj, "ParentId"), "Name")) });
}
File.WriteAllText(Path.Combine(output, "objects.json"), JsonSerializer.Serialize(objects));
using (var worker = new TextureWorker())
foreach (var sprite in Data.Sprites.Where(s => s?.Name?.Content != null && (new[] { "sp_noise2", "sp_static", "sp_df_sideline", "sp_df_grid" }.Contains(s.Name.Content) || objectSprites.Contains(s.Name.Content))))
{
    var frames = new List<string>();
    for (int i = 0; i < sprite.Textures.Count; i++)
    { string file = sprite.Name.Content + "_" + i + ".png"; worker.ExportAsPNG(sprite.Textures[i].Texture, Path.Combine(output, file), null, true); frames.Add(file); }
    sprites[sprite.Name.Content] = new { width = sprite.Width, height = sprite.Height, originX = sprite.OriginX, originY = sprite.OriginY, frames };
}
File.WriteAllText(Path.Combine(output, "sprites.json"), JsonSerializer.Serialize(sprites, new JsonSerializerOptions { WriteIndented = true }));
foreach (var shader in Data.Shaders.Where(s => s?.Name?.Content != null && s.Name.Content.Contains("edge")))
    File.WriteAllText(Path.Combine(output, shader.Name.Content + ".frag"), shader.GLSL_ES_Fragment?.Content ?? shader.GLSL_Fragment?.Content);
object Prop(object o, string n) => o == null ? null : o.GetType().GetProperty(n)?.GetValue(o);
string Str(object o) => o == null ? "" : o as string ?? Prop(o, "Content") as string ?? o.ToString();
IEnumerable<object> List(object o) => o is IEnumerable a ? a.Cast<object>() : Enumerable.Empty<object>();
var rooms = new Dictionary<string, object>();
foreach (object room in List(Prop(Data, "Rooms")))
{
    string name = Str(Prop(room, "Name")); if (!name.StartsWith("scene_gameplay")) continue;
    foreach (object layer in List(Prop(room, "Layers")))
    {
        if (Str(Prop(layer, "LayerName")) != "FX_edge") continue;
        var parameters = new Dictionary<string, object>();
        foreach (object p in List(Prop(layer, "EffectProperties")))
        {
            string key = Str(Prop(p, "Name")), value = Str(Prop(p, "Value"));
            parameters[key] = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? (object)n : value;
        }
        rooms[name] = new { name = "FX_edge", filter = Str(Prop(layer, "EffectType")), depth = Convert.ToInt32(Prop(layer, "LayerDepth")),
            visible = Convert.ToBoolean(Prop(layer, "IsVisible")), enabled = Convert.ToBoolean(Prop(layer, "EffectEnabled")), parameters };
    }
}
File.WriteAllText(Path.Combine(output, "edge-rooms.json"), JsonSerializer.Serialize(rooms, new JsonSerializerOptions { WriteIndented = true }));
foreach (var entry in Data.Strings)
{
    string raw = entry?.Content; if (raw == null || !raw.TrimStart().StartsWith("{")) continue;
    try { using var doc = JsonDocument.Parse(raw); if (doc.RootElement.TryGetProperty("name", out var n) && n.GetString() == "_filter_edgedetect") File.WriteAllText(Path.Combine(output, "edge-definition.json"), raw); } catch (JsonException) { }
}
ScriptMessage("Read-only Custom FX extraction complete.");
