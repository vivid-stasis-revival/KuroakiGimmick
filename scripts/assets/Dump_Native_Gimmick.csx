// Read-only UTMT 0.9.2 resource exporter driven by an object definition.
// Set KUROAKI_GIMMICK_DEFINITION and KUROAKI_GIMMICK_OUTPUT (a new directory).
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UndertaleModLib.Util;
using UndertaleModLib.Decompiler;
EnsureDataLoaded();
string definitionPath=Environment.GetEnvironmentVariable("KUROAKI_GIMMICK_DEFINITION");
string output=Environment.GetEnvironmentVariable("KUROAKI_GIMMICK_OUTPUT");
if(string.IsNullOrWhiteSpace(definitionPath)||string.IsNullOrWhiteSpace(output))throw new Exception("Set KUROAKI_GIMMICK_DEFINITION and KUROAKI_GIMMICK_OUTPUT.");
var definition=JsonNode.Parse(File.ReadAllText(definitionPath)).AsObject();
string objectName=definition["objectName"].GetValue<string>();
if(!Data.GameObjects.Any(o=>o?.Name?.Content==objectName))throw new Exception("Game object does not exist: "+objectName);
output=Path.GetFullPath(output);
if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new Exception("Export directory must be empty.");
Directory.CreateDirectory(output);
string ResourceKey(string key)=>Regex.IsMatch(key,@"^[A-Za-z0-9_]+$")?key:throw new Exception("Invalid resource key: "+key);
using(var worker=new TextureWorker())foreach(string key in definition["sprites"].AsObject().Select(p=>p.Key).ToArray())
{
    ResourceKey(key);var sprite=Data.Sprites.FirstOrDefault(s=>s?.Name?.Content==key)??throw new Exception("Missing sprite "+key);
    Directory.CreateDirectory(Path.Combine(output,"sprites",key));var frames=new List<string>();
    for(int i=0;i<sprite.Textures.Count;i++)
    {
        string relative="sprites/"+key+"/"+i.ToString("D4")+".png";
        worker.ExportAsPNG(sprite.Textures[i].Texture,Path.Combine(output,relative),null,true);frames.Add(relative);
    }
    definition["sprites"][key]=JsonSerializer.SerializeToNode(new{width=sprite.Width,height=sprite.Height,originX=sprite.OriginX,originY=sprite.OriginY,frames});
}
Directory.CreateDirectory(Path.Combine(output,"shaders"));
foreach(string key in definition["shaders"].AsObject().Select(p=>p.Key).ToArray())
{
    ResourceKey(key);var shader=Data.Shaders.FirstOrDefault(s=>s?.Name?.Content==key)??throw new Exception("Missing shader "+key);
    string source=shader.GLSL_Fragment?.Content;if(string.IsNullOrWhiteSpace(source))source=shader.GLSL_ES_Fragment?.Content;
    if(string.IsNullOrWhiteSpace(source))throw new Exception("No GLSL source for "+key);
    string relative="shaders/"+key+".frag";File.WriteAllText(Path.Combine(output,relative),source);definition["shaders"][key]=relative;
}
object Prop(object value,string name)=>value==null?null:value.GetType().GetProperty(name)?.GetValue(value);
string Str(object value)=>value==null?"":value as string??Prop(value,"Content") as string??value.ToString();
IEnumerable<object> List(object value)=>value is IEnumerable list?list.Cast<object>().Where(x=>x!=null):Enumerable.Empty<object>();
object Parameter(object property)
{
    string value=Str(Prop(property,"Value"));int kind=Convert.ToInt32(Prop(property,"Kind")??0);
    if(kind==0&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n))return n;
    if(kind==1&&value.StartsWith("#")&&(value.Length==7||value.Length==9))
    {uint rgb=uint.Parse(value.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);return new double[]{((rgb>>16)&255)/255.0,((rgb>>8)&255)/255.0,(rgb&255)/255.0,value.Length==9?(rgb>>24)/255.0:1};}
    return value;
}
string roomName=definition["sourceRoom"]?.GetValue<string>();
if(!string.IsNullOrWhiteSpace(roomName))
{
    var room=Data.Rooms.FirstOrDefault(r=>r?.Name?.Content==roomName)??throw new Exception("Missing source room "+roomName);
    var layers=new List<object>();
    foreach(var layer in List(Prop(room,"Layers")))
    {
        var parameters=new Dictionary<string,object>();
        foreach(var group in List(Prop(layer,"EffectProperties")).GroupBy(p=>Str(Prop(p,"Name"))))
        {var values=group.Select(Parameter).ToArray();parameters[group.Key]=values.Length==1?values[0]:values.All(v=>v is double)?values.Cast<double>().ToArray():(object)values;}
        layers.Add(new{name=Str(Prop(layer,"LayerName")),filter=Str(Prop(layer,"EffectType")),depth=Prop(layer,"LayerDepth"),visible=Prop(layer,"IsVisible"),enabled=Prop(layer,"EffectEnabled"),parameters});
    }
    File.WriteAllText(Path.Combine(output,"room.fx.json"),JsonSerializer.Serialize(new{format=1,room=roomName,layers},new JsonSerializerOptions{WriteIndented=true}));
}
var objects=new HashSet<string>{objectName};
if(definition["referenceObjects"] is JsonArray extraObjects)foreach(var item in extraObjects)objects.Add(item.GetValue<string>());
var codeNames=new HashSet<string>();
if(definition["referenceCode"] is JsonArray extraCode)foreach(var item in extraCode)codeNames.Add(item.GetValue<string>());
if(!string.IsNullOrWhiteSpace(roomName))codeNames.Add("gml_Room_"+roomName+"_Create");
var roots=new HashSet<string>();
foreach(var code in Data.Code.Where(c=>c?.Name?.Content!=null))
{
    string name=code.Name.Content;
    if(!codeNames.Contains(name)&&!objects.Any(o=>name.StartsWith("gml_Object_"+o+"_")))continue;
    var root=code;while(root.ParentEntry!=null)root=root.ParentEntry;roots.Add(root.Name.Content);
}
Directory.CreateDirectory(Path.Combine(output,"reference"));var context=new GlobalDecompileContext(Data);
foreach(var code in Data.Code.Where(c=>c?.Name?.Content!=null&&roots.Contains(c.Name.Content)))
    File.WriteAllText(Path.Combine(output,"reference",Regex.Replace(code.Name.Content,@"[^A-Za-z0-9_-]","_")+".gml"),new Underanalyzer.Decompiler.DecompileContext(context,code,new Underanalyzer.Decompiler.DecompileSettings()).DecompileToString());
File.WriteAllText(Path.Combine(output,"manifest.json"),definition.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
ScriptMessage("Object resources exported read-only for "+objectName+" to "+output);
