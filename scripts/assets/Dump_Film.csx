// Read-only UTMT export of the game's original film shader, texture and room parameters.
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using UndertaleModLib.Util;
EnsureDataLoaded();
string output = Environment.GetEnvironmentVariable("KUROAKI_FILM_OUTPUT");
if (string.IsNullOrWhiteSpace(output) || Directory.Exists(output)) throw new Exception("Set KUROAKI_FILM_OUTPUT to a new output directory.");
Directory.CreateDirectory(output);
var shader = Data.Shaders.FirstOrDefault(s => s?.Name?.Content == "_filter_old_film_shader");
if (shader == null) throw new Exception("Original film shader missing.");
if (!string.IsNullOrWhiteSpace(shader.GLSL_ES_Fragment?.Content)) File.WriteAllText(Path.Combine(output, "old-film.frag"), shader.GLSL_ES_Fragment.Content);
else if (!string.IsNullOrWhiteSpace(shader.GLSL_Fragment?.Content)) File.WriteAllText(Path.Combine(output, "old-film.frag"), shader.GLSL_Fragment.Content);
else throw new Exception("Film GLSL source missing.");
var sprite = Data.Sprites.FirstOrDefault(s => s?.Name?.Content == "_filter_old_film_texture");
if (sprite == null || sprite.Textures.Count != 1) throw new Exception("Original film texture missing.");
using (var worker = new TextureWorker()) worker.ExportAsPNG(sprite.Textures[0].Texture, Path.Combine(output, "_filter_old_film_texture.png"), null, true);
object Prop(object o, string n) => o == null ? null : o.GetType().GetProperty(n)?.GetValue(o);
string Str(object o) => o == null ? "" : o as string ?? Prop(o, "Content") as string ?? o.ToString();
IEnumerable<object> List(object o) => o is IEnumerable a ? a.Cast<object>() : Enumerable.Empty<object>();
var layers = new Dictionary<string, object>();
foreach (object room in List(Prop(Data, "Rooms")))
{
    string name = Str(Prop(Prop(room, "Name"), "Content"));
    if (!name.StartsWith("scene_gameplay")) continue;
    foreach (object layer in List(Prop(room, "Layers")))
    {
        if (Str(Prop(layer, "LayerName")) != "FX_film") continue;
        var parameters = new Dictionary<string, object>();
        foreach (object p in List(Prop(layer, "EffectProperties")))
        {
            string key = Str(Prop(p, "Name")), value = Str(Prop(p, "Value"));
            parameters[key] = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? (object)number : value;
        }
        layers[name] = new { name = "FX_film", filter = Str(Prop(layer, "EffectType")), depth = Convert.ToInt32(Prop(layer, "LayerDepth")),
            visible = Convert.ToBoolean(Prop(layer, "IsVisible")), enabled = Convert.ToBoolean(Prop(layer, "EffectEnabled")), parameters };
    }
}
File.WriteAllText(Path.Combine(output, "film-rooms.json"), JsonSerializer.Serialize(layers, new JsonSerializerOptions { WriteIndented = true }));
ScriptMessage("Read-only film export complete: " + output);
