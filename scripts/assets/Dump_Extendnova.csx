// Read-only reference extraction; original game data is never saved.
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Util;
EnsureDataLoaded();
string output = Environment.GetEnvironmentVariable("KUROAKI_EXTENDNOVA_OUTPUT");
if (string.IsNullOrWhiteSpace(output) || Directory.Exists(output)) throw new Exception("Use a new KUROAKI_EXTENDNOVA_OUTPUT directory.");
Directory.CreateDirectory(output);
var context = new GlobalDecompileContext(Data);
foreach (var code in Data.Code.Where(c => c?.Name?.Content != null && c.ParentEntry == null))
{
    string n = code.Name.Content;
    if (n.Contains("extendnova") || n.Contains("extnova") || n.Contains("ss_en_story") || n.Contains("o_sg_endblip") || n.Contains("_cc_") || n.Contains("o_en_") || n.Contains("obj_en_") || n.Contains("_effect_glow"))
        File.WriteAllText(Path.Combine(output, n + ".gml"), new Underanalyzer.Decompiler.DecompileContext(context, code, new Underanalyzer.Decompiler.DecompileSettings()).DecompileToString());
}
object Prop(object o, string name) => o == null ? null : o.GetType().GetProperty(name)?.GetValue(o);
string Str(object o) => o == null ? "" : o as string ?? Prop(o, "Content") as string ?? o.ToString();
IEnumerable<object> List(object o) => o is IEnumerable a ? a.Cast<object>() : Enumerable.Empty<object>();
var objects = new List<object>(); var selectedSprites = new HashSet<string>();
foreach (var obj in Data.GameObjects.Where(o => o?.Name?.Content != null && (o.Name.Content.Contains("extendnova") || o.Name.Content.Contains("extnova") || o.Name.Content.StartsWith("o_en_") || o.Name.Content.StartsWith("obj_en_"))))
{
    string sprite = Str(Prop(Prop(obj, "Sprite"), "Name")); if (sprite.Length > 0) selectedSprites.Add(sprite);
    objects.Add(new { name = obj.Name.Content, sprite, parent = Str(Prop(Prop(obj, "ParentId"), "Name")) });
}
File.WriteAllText(Path.Combine(output, "objects.json"), JsonSerializer.Serialize(objects, new JsonSerializerOptions { WriteIndented = true }));
var room = Data.Rooms.First(r => r?.Name?.Content == "scene_gameplay_extendnova");
var layers = new List<object>();
foreach (object layer in List(Prop(room, "Layers")))
{
    var parameters = new Dictionary<string, object>();
    foreach (var group in List(Prop(layer, "EffectProperties")).GroupBy(p => Str(Prop(p, "Name"))))
    {
        object Value(object p) { string s = Str(Prop(p, "Value")); return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? (object)d : s; }
        var values = group.Select(Value).ToArray(); parameters[group.Key] = values.Length == 1 ? values[0] : values;
    }
    object data = Prop(layer, "Data");
    var properties = data == null ? null : data.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0).ToDictionary(p => p.Name, p => Str(p.GetValue(data)));
    string background = Str(Prop(Prop(data, "Sprite"), "Name")); if (background.Length > 0) selectedSprites.Add(background);
    layers.Add(new { name = Str(Prop(layer, "LayerName")), filter = Str(Prop(layer, "EffectType")), depth = Prop(layer, "LayerDepth"), visible = Prop(layer, "IsVisible"), enabled = Prop(layer, "EffectEnabled"), parameters, background, properties });
}
File.WriteAllText(Path.Combine(output, "room-layers.json"), JsonSerializer.Serialize(new { room = room.Name.Content, layers }, new JsonSerializerOptions { WriteIndented = true }));
foreach (var s in Data.Sprites.Where(s => s?.Name?.Content != null && (s.Name.Content.Contains("extendnova") || s.Name.Content.StartsWith("sp_extnova") || s.Name.Content.StartsWith("sp_en_") || s.Name.Content == "sp_holdnote_overlay" || selectedSprites.Contains(s.Name.Content))))
{
    selectedSprites.Add(s.Name.Content);
}
File.WriteAllLines(Path.Combine(output, "sprite-index.txt"), Data.Sprites.Where(s => s?.Name?.Content != null).Select(s => s.Name.Content));
File.WriteAllLines(Path.Combine(output, "shader-index.txt"), Data.Shaders.Where(s => s?.Name?.Content != null).Select(s => s.Name.Content));
foreach (var s in Data.Shaders.Where(s => s?.Name?.Content != null && (s.Name.Content.Contains("supernova") || s.Name.Content.Contains("heathaze") || s.Name.Content.Contains("chroma"))))
    File.WriteAllText(Path.Combine(output, s.Name.Content + ".frag"), s.GLSL_ES_Fragment?.Content ?? s.GLSL_Fragment?.Content);
var sprites = new Dictionary<string, object>();
using (var worker = new TextureWorker())
foreach (var s in Data.Sprites.Where(s => s?.Name?.Content != null && selectedSprites.Contains(s.Name.Content)))
{
    var frames = new List<string>();
    for (int i = 0; i < s.Textures.Count; i++) { string file = s.Name.Content + "_" + i + ".png"; worker.ExportAsPNG(s.Textures[i].Texture, Path.Combine(output, file), null, true); frames.Add(file); }
    sprites[s.Name.Content] = new { width = s.Width, height = s.Height, originX = s.OriginX, originY = s.OriginY, frames };
}
File.WriteAllText(Path.Combine(output, "sprites.json"), JsonSerializer.Serialize(sprites, new JsonSerializerOptions { WriteIndented = true }));
ScriptMessage("Read-only Extendnova export complete.");
