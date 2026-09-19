// Read-only dialogue source extraction; never saves the game.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Util;
EnsureDataLoaded();
string output = Environment.GetEnvironmentVariable("KUROAKI_STORY_OUTPUT");
if (string.IsNullOrWhiteSpace(output) || Directory.Exists(output)) throw new Exception("Use a new KUROAKI_STORY_OUTPUT directory.");
Directory.CreateDirectory(output);
var context = new GlobalDecompileContext(Data);
var wanted = new HashSet<string>(new[] { "text", "text_clear", "name_set", "c_playm", "TextDrawer", "TweenEasyMove" });
var parents = Data.Code.Where(c => c?.Name?.Content != null && wanted.Any(w => c.Name.Content == "gml_Script_" + w || c.Name.Content.StartsWith("gml_Script_" + w + "@")))
    .Select(c => c.ParentEntry ?? c).ToHashSet();
foreach (var code in Data.Code.Where(c => c?.Name?.Content != null && c.ParentEntry == null))
{
    string n = code.Name.Content;
    if (parents.Contains(code) || n.Contains("textbox") || n.Contains("TextDrawer") || n.Contains("ss_en_story") || n.Contains("GlobalScript_text") || n.Contains("GlobalScript_name_set") || n.Contains("GlobalScript_c_playm"))
        File.WriteAllText(Path.Combine(output, n + ".gml"), new Underanalyzer.Decompiler.DecompileContext(context, code, new Underanalyzer.Decompiler.DecompileSettings()).DecompileToString());
}
var sprites = new Dictionary<string, object>();
using (var worker = new TextureWorker())
foreach (var s in Data.Sprites.Where(s => s?.Name?.Content != null && new[] { "sp_dialogue", "sp_dialogue_grads", "sp_namebox_new" }.Contains(s.Name.Content)))
{
    var frames = new List<string>();
    for (int i = 0; i < s.Textures.Count; i++)
    {
        string file = s.Name.Content + "_" + i + ".png";
        worker.ExportAsPNG(s.Textures[i].Texture, Path.Combine(output, file), null, true); frames.Add(file);
    }
    sprites[s.Name.Content] = new { width = s.Width, height = s.Height, originX = s.OriginX, originY = s.OriginY, frames };
}
File.WriteAllText(Path.Combine(output, "sprites.json"), JsonSerializer.Serialize(sprites, new JsonSerializerOptions { WriteIndented = true }));
ScriptMessage("Read-only dialogue export complete.");
