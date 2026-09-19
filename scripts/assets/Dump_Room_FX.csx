// Read-only UTMT script. Open the original game data first.
// CLI users can set SCARLET_FX_OUTPUT to avoid a directory picker.
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

EnsureDataLoaded();
string output=Environment.GetEnvironmentVariable("SCARLET_FX_OUTPUT");
if(string.IsNullOrWhiteSpace(output))output=PromptChooseDirectory();
if(string.IsNullOrWhiteSpace(output))throw new Exception("No output directory selected.");
output=Path.Combine(Path.GetFullPath(output),"Scarlet_FX_profiles");
Directory.CreateDirectory(output);

// Reflection keeps the exporter independent of UndertaleModLib model versions.
object Prop(object value,string name) => value==null?null:value.GetType().GetProperty(name)?.GetValue(value);
string Str(object value)
{
    if(value==null)return "";
    return value as string??Prop(value,"Content") as string??value.ToString();
}
IEnumerable<object> List(object value) => value is IEnumerable list?list.Cast<object>():Enumerable.Empty<object>();
object Parameter(object property)
{
    string value=Str(Prop(property,"Value"));int kind=Convert.ToInt32(Prop(property,"Kind")??0);
    if(kind==0&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double number))
    {
        if(double.IsNaN(number)||double.IsInfinity(number))throw new Exception("Non-finite FX parameter.");
        return number;
    }
    if(kind==1&&value.StartsWith("#")&&(value.Length==7||value.Length==9))
    {
        uint packed=uint.Parse(value.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        return new double[]{((packed>>16)&255)/255.0,((packed>>8)&255)/255.0,(packed&255)/255.0,value.Length==9?(packed>>24)/255.0:1};
    }
    return value; // Sampler names stay names; the viewer resolves local textures.
}
var supportedNames=new HashSet<string>{"FX_chroma","FX_contrast","FX_red","FX_hue","LBG"};
var index=new List<string>();
foreach(object room in List(Prop(Data,"Rooms")))
{
    string name=Str(Prop(room,"Name"));var layers=new List<object>();
    foreach(object layer in List(Prop(room,"Layers")))
    {
        string layerName=Str(Prop(layer,"LayerName")),filter=Str(Prop(layer,"EffectType"));
        if(!supportedNames.Contains(layerName)||filter.Length==0)continue;
        var parameters=new Dictionary<string,object>();
        foreach(var group in List(Prop(layer,"EffectProperties")).GroupBy(p=>Str(Prop(p,"Name"))))
        {
            var values=group.Select(Parameter).ToArray();
            parameters[group.Key]=values.Length==1?values[0]:values.All(v=>v is double)?values.Cast<double>().ToArray():(object)values;
        }
        layers.Add(new{name=layerName,filter,depth=Convert.ToInt32(Prop(layer,"LayerDepth")??0),visible=Convert.ToBoolean(Prop(layer,"IsVisible")??true),enabled=Convert.ToBoolean(Prop(layer,"EffectEnabled")??true),parameters});
    }
    if(layers.Count==0)continue;
    string safe=new string(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray());
    string file=safe+".fx.json";
    File.WriteAllText(Path.Combine(output,file),JsonSerializer.Serialize(new{format=1,room=name,layers},new JsonSerializerOptions{WriteIndented=true}));
    index.Add(file+" : "+layers.Count+" FX layers");
}
File.WriteAllLines(Path.Combine(output,"INDEX.txt"),index);
if(index.Count==0)throw new Exception("No supported room FX layers found. Original game data was not modified.");
ScriptMessage("Exported "+index.Count+" room FX profiles to:\n"+output+"\nAttach the matching .fx.json in KuroakiGimmick. No game data was modified.");
