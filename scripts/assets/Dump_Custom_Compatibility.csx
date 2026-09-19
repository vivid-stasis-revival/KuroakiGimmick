// Read-only reference extraction. Never saves or patches game data.
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Util;
EnsureDataLoaded();
string output = Environment.GetEnvironmentVariable("KUROAKI_COMPAT_OUTPUT");
if (string.IsNullOrWhiteSpace(output) || Directory.Exists(output)) throw new Exception("Set KUROAKI_COMPAT_OUTPUT to a new directory.");
Directory.CreateDirectory(output);
var context = new GlobalDecompileContext(Data);
var errors = new List<string>();
var names = Data.Code.Where(c => c?.Name?.Content != null && c.ParentEntry == null).Select(c => c.Name.Content).ToArray();
File.WriteAllLines(Path.Combine(output, "code-index.txt"), names);
foreach (var code in Data.Code.Where(c => c?.Name?.Content != null && c.ParentEntry == null))
{
    string n = code.Name.Content;
    if (!(n.Contains("obj_custom_gimmick") || n.Contains("_cc_") || n.Contains("star") || n.Contains("eachother") || n.Contains("_eo_") ||
        n.Contains("holdnote_overlay") || n.Contains("csm_") || n.Contains("songgameplay") || n.Contains("endstat") || n.Contains("endcg") || n.Contains("sekaisen") || n.Contains("_load_mods") ||
        n.Contains("read_mods") || n.Contains("obj_base_gimmick") || n.Contains("combodisplay") || n.Contains("mod_setup") || n.Contains("CustomSongsGmk") || n.Contains("init_customgmk"))) continue;
    try
    {
        string text = new Underanalyzer.Decompiler.DecompileContext(context, code, new Underanalyzer.Decompiler.DecompileSettings()).DecompileToString();
        File.WriteAllText(Path.Combine(output, n + ".gml"), text);
    }
    catch (Exception e) { errors.Add(n + ": " + e.Message); }
}
File.WriteAllLines(Path.Combine(output, "errors.txt"), errors);
File.WriteAllLines(Path.Combine(output, "sprite-index.txt"), Data.Sprites.Where(s => s?.Name?.Content != null).Select(s => s.Name.Content));
var sprites = new Dictionary<string, object>();
using (var worker = new TextureWorker())
foreach (var sprite in Data.Sprites.Where(s => s?.Name?.Content != null && new[] { "sp_sk_star", "sp_cover2", "sp_cover3", "sp_cover4", "sp_holdnote_overlay", "sp_sekaisen_track", "sp_sekaisen_point", "sp_cg_train", "sp_combofont_newer" }.Contains(s.Name.Content)))
{
    var frames = new List<string>();
    for (int i = 0; i < sprite.Textures.Count; i++)
    {
        string file = sprite.Name.Content + "_" + i + ".png";
        worker.ExportAsPNG(sprite.Textures[i].Texture, Path.Combine(output, file), null, true); frames.Add(file);
    }
    sprites[sprite.Name.Content] = new { width = sprite.Width, height = sprite.Height, originX = sprite.OriginX, originY = sprite.OriginY, frames };
}
File.WriteAllText(Path.Combine(output, "sprites.json"), JsonSerializer.Serialize(sprites, new JsonSerializerOptions { WriteIndented = true }));
ScriptMessage("Read-only compatibility export complete: " + output);
