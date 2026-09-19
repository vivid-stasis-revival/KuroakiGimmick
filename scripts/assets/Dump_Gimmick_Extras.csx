using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using UndertaleModLib.Util;
using UndertaleModLib.Decompiler;
using System.Collections;
using System.Globalization;

// Read-only, tightly scoped export. No songs, jackets, full atlases or data writes.
EnsureDataLoaded();
string output=Environment.GetEnvironmentVariable("KUROAKI_EXTRAS_OUTPUT");
if(string.IsNullOrWhiteSpace(output))output=PromptChooseDirectory();
if(string.IsNullOrWhiteSpace(output))throw new Exception("No output directory selected.");
output=Path.Combine(Path.GetFullPath(output),"GimmickExtras");Directory.CreateDirectory(output);
var missing=new List<string>();var sprites=new Dictionary<string,object>();var shaders=new Dictionary<string,string>();
using(var worker=new TextureWorker())
{
    string name="sp_angelstar_checker";
    var sprite=Data.Sprites.FirstOrDefault(s=>s?.Name?.Content==name);
    if(sprite==null||sprite.Textures.Count!=3)missing.Add(name+" (three frames)");
    else
    {
        var frames=new List<string>();
        for(int i=0;i<sprite.Textures.Count;i++)
        {
            if(sprite.Textures[i]?.Texture==null)throw new Exception("Missing checker texture frame "+i);
            string file=name+"_"+i+".png";
            worker.ExportAsPNG(sprite.Textures[i].Texture,Path.Combine(output,file),null,true);frames.Add(file);
        }
        sprites[name]=new{width=sprite.Width,height=sprite.Height,originX=sprite.OriginX,originY=sprite.OriginY,frames};
    }
}
// Capture actual platform variants, including GameMaker's built-in glow.
string Safe(string name)=>new string(name.Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'?c:'_').ToArray());
var shaderInventory=Data.Shaders.Where(s=>s?.Name?.Content!=null&&s.Name.Content.IndexOf("glow",StringComparison.OrdinalIgnoreCase)>=0).Select(s=>s.Name.Content).ToArray();
foreach(var shader in Data.Shaders.Where(s=>s?.Name?.Content!=null&&(s.Name.Content.IndexOf("glow",StringComparison.OrdinalIgnoreCase)>=0||s.Name.Content=="_filter_underwater_shader")))
{
    foreach(var pair in new[]{Tuple.Create("GLSL_Fragment",shader.GLSL_Fragment?.Content),Tuple.Create("GLSL_ES_Fragment",shader.GLSL_ES_Fragment?.Content)})
    {
        if(string.IsNullOrWhiteSpace(pair.Item2))continue;
        string file=Safe(shader.Name.Content)+"_"+pair.Item1+".frag";
        File.WriteAllText(Path.Combine(output,file),pair.Item2);shaders[shader.Name.Content+"/"+pair.Item1]=file;
    }
}
if(shaderInventory.Length==0)missing.Add("No shader name contains glow; inspect runtime code/layers for this platform.");

object Prop(object value,string name)=>value==null?null:value.GetType().GetProperty(name)?.GetValue(value);
string Str(object value)=>value==null?"":value as string??Prop(value,"Content") as string??value.ToString();
IEnumerable<object> List(object value)=>value is IEnumerable list?list.Cast<object>().Where(x=>x!=null):Enumerable.Empty<object>();
string ResourceName(object value)=>Str(Prop(value,"Name"));
object Parameter(object property)
{
    string value=Str(Prop(property,"Value"));int kind=Convert.ToInt32(Prop(property,"Kind")??0);
    if(kind==0&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n))return n;
    if(kind==1&&value.StartsWith("#")&&(value.Length==7||value.Length==9))
    {uint rgb=uint.Parse(value.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);return new double[]{((rgb>>16)&255)/255.0,((rgb>>8)&255)/255.0,(rgb&255)/255.0,value.Length==9?(rgb>>24)/255.0:1};}
    return value;
}
var roomIndex=new List<string>();var evidence=new List<object>();var codeNames=new HashSet<string>();
string roomFolder=Path.Combine(output,"rooms");Directory.CreateDirectory(roomFolder);
foreach(object room in List(Prop(Data,"Rooms")))
{
    string roomName=ResourceName(room);
    if(roomName.IndexOf("gameplay",StringComparison.OrdinalIgnoreCase)<0)continue;
    var layers=new List<object>();
    foreach(object layer in List(Prop(room,"Layers")))
    {
        string name=Str(Prop(layer,"LayerName")),filter=Str(Prop(layer,"EffectType"));
        if(filter.Length==0&&!name.StartsWith("FX_")&&name!="glow"&&name!="LBG")continue;
        var parameters=new Dictionary<string,object>();
        foreach(var group in List(Prop(layer,"EffectProperties")).GroupBy(p=>Str(Prop(p,"Name"))))
        {var vals=group.Select(Parameter).ToArray();parameters[group.Key]=vals.Length==1?vals[0]:vals.All(v=>v is double)?vals.Cast<double>().ToArray():(object)vals;}
        layers.Add(new{name,filter,depth=Convert.ToInt32(Prop(layer,"LayerDepth")??0),visible=Convert.ToBoolean(Prop(layer,"IsVisible")??true),enabled=Convert.ToBoolean(Prop(layer,"EffectEnabled")??true),parameters});
    }
    var instances=new List<object>();
    foreach(object inst in List(Prop(room,"GameObjects")))
    {
        string obj=ResourceName(Prop(inst,"ObjectDefinition"));
        if(obj.IndexOf("glow",StringComparison.OrdinalIgnoreCase)<0)continue;
        string creation=ResourceName(Prop(inst,"CreationCode")),pre=ResourceName(Prop(inst,"PreCreateCode"));
        if(creation.Length>0)codeNames.Add(creation);if(pre.Length>0)codeNames.Add(pre);
        instances.Add(new{objectName=obj,creation,pre});
    }
    string roomCode=ResourceName(Prop(room,"CreationCodeId"));if(roomCode.Length>0)codeNames.Add(roomCode);
    string file=Safe(roomName)+".fx.json";
    File.WriteAllText(Path.Combine(roomFolder,file),JsonSerializer.Serialize(new{format=1,room=roomName,layers},new JsonSerializerOptions{WriteIndented=true}));
    roomIndex.Add(file);evidence.Add(new{room=roomName,creation=roomCode,glowInstances=instances});
}
// Source from THIS game build resolves differences from an older decomp ZIP.
var codeIndex=new List<string>();string codeFolder=Path.Combine(output,"code");Directory.CreateDirectory(codeFolder);
var context=new GlobalDecompileContext(Data);
foreach(var code in Data.Code.Where(c=>c?.Name?.Content!=null&&c.ParentEntry==null))
{
    string name=code.Name.Content;
    bool selected=codeNames.Contains(name)||name.IndexOf("glow",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("custom_song",StringComparison.OrdinalIgnoreCase)>=0||name.StartsWith("gml_GlobalScript_start_song")||name=="gml_GlobalScript_LoadSong"||name=="gml_GlobalScript_LoadSongData"||name.StartsWith("gml_Object_obj_custom_gimmick_")||name=="gml_Object_cc_Step_1"||name=="gml_Object_cc_Create_0";
    if(!selected)continue;
    try
    {
        string text=new Underanalyzer.Decompiler.DecompileContext(context,code,new Underanalyzer.Decompiler.DecompileSettings()).DecompileToString();
        string file=Safe(name)+".gml";File.WriteAllText(Path.Combine(codeFolder,file),text);codeIndex.Add(file);
    }
    catch(Exception ex){missing.Add("Code: "+name+" : "+ex.Message);}
}
File.WriteAllText(Path.Combine(output,"runtime-evidence.json"),JsonSerializer.Serialize(new{shaderInventory,roomIndex,rooms=evidence,codeIndex},new JsonSerializerOptions{WriteIndented=true}));
// Dynamic fx_create defaults do not appear in static room dumps. Retain only
// matching embedded definitions, if this runner stores them in its string pool,
// for inspection; never substitute another room's heat-haze settings.
var definitions=Data.Strings.Where(s=>s?.Content!=null&&s.Content.Length<1048576&&((s.Content.Contains("g_Distort1Scale")&&s.Content.Contains("g_Distort2Scale"))||s.Content.Contains("g_GlowRadius"))).Select(s=>s.Content).Distinct().Take(64).ToArray();
File.WriteAllText(Path.Combine(output,"filter-definitions.json"),JsonSerializer.Serialize(definitions));
File.WriteAllText(Path.Combine(output,"gimmick-extras.json"),JsonSerializer.Serialize(new{version=1,exportRevision=2,sprites,shaders,missing},new JsonSerializerOptions{WriteIndented=true}));
File.WriteAllText(Path.Combine(output,"COMPLETE.txt"),"Read-only export finished. Inspect missing in gimmick-extras.json.\n");
ScriptMessage("Gimmick resources exported to:\n"+output+"\nMissing: "+string.Join(", ",missing));
