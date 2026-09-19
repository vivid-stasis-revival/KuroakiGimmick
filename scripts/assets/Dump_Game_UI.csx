using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using UndertaleModLib.Util;

// Read-only. Export only shared gameplay UI, never song resources.
EnsureDataLoaded();
string output=Environment.GetEnvironmentVariable("KUROAKI_UI_OUTPUT");
if(string.IsNullOrWhiteSpace(output))output=PromptChooseDirectory();
if(string.IsNullOrWhiteSpace(output))throw new Exception("No output directory selected.");
output=Path.Combine(Path.GetFullPath(output),"GameUI");
Directory.CreateDirectory(output);
var sprites=new Dictionary<string,object>();
var fonts=new Dictionary<string,object>();
var required=new[]{"sp_gameplayoverlay2024","sp_2024cc_score","sp_newdifficultyindicator","sp_newdifficultylevel","sp_newdifficultynumbers"};
// font_add_sprite 拿纹理页的裁剪矩形当比例字宽，所以当字模用的精灵要额外导出逐帧 bounds。
var boundsNeeded=new[]{"sp_combofont_newer"};
using(var worker=new TextureWorker())
{
    foreach(string name in required.Concat(new[]{"sp_fc_indicator", "sp_dialogue", "sp_dialogue_grads", "sp_namebox_new", "sp_judgements",
        "sp_judgements_2", "sp_combofont_newer"}))
    {
        var sprite=Data.Sprites.FirstOrDefault(s=>s?.Name?.Content==name);
        if(sprite==null||sprite.Textures.Count==0)
        {
            if(required.Contains(name))throw new Exception("Missing original UI sprite: "+name);
            continue;
        }
        var frames=new List<string>();
        var bounds=new List<int[]>();
        for(int i=0;i<sprite.Textures.Count;i++)
        {
            string file=name+"_"+i+".png";
            var item=sprite.Textures[i].Texture;
            worker.ExportAsPNG(item,Path.Combine(output,file),null,true);
            frames.Add(file);
            bounds.Add(new[]{(int)item.TargetX,(int)item.TargetY,(int)item.TargetWidth,(int)item.TargetHeight});
        }
        if(boundsNeeded.Contains(name))sprites[name]=new{width=sprite.Width,height=sprite.Height,originX=sprite.OriginX,originY=sprite.OriginY,frames,bounds};
        else sprites[name]=new{width=sprite.Width,height=sprite.Height,originX=sprite.OriginX,originY=sprite.OriginY,frames};
    }
    foreach(string name in new[]{"fnt_monacovs","fnt_credits","fnt_phosphor"})
    {
        var font=Data.Fonts.FirstOrDefault(f=>f?.Name?.Content==name);
        if(font==null||font.Texture==null)
        {
            if(name!="fnt_phosphor")throw new Exception("Missing original UI font: "+name);
            continue;
        }
        string file=name+".png";
        worker.ExportAsPNG(font.Texture,Path.Combine(output,file));
        fonts[name]=new{file,size=font.EmSize,lineHeight=font.LineHeight,ascenderOffset=font.AscenderOffset,scaleX=font.ScaleX,scaleY=font.ScaleY,
            glyphs=font.Glyphs.Select(g=>new{character=g.Character,x=g.SourceX,y=g.SourceY,w=g.SourceWidth,h=g.SourceHeight,advance=g.Shift,offset=g.Offset,
                kerning=g.Kerning.Select(k=>new{character=k.Character,shift=k.ShiftModifier}).ToArray()}).ToArray()};
    }
}
File.WriteAllText(Path.Combine(output,"game-ui.gameui.json"),JsonSerializer.Serialize(new{version=1,sprites,fonts},new JsonSerializerOptions{WriteIndented=false}));
File.WriteAllText(Path.Combine(output,"COMPLETE.txt"),"Original shared gameplay UI and font export completed.\n");
ScriptMessage("Original gameplay UI exported to:\n"+output+"\nReload KuroakiGimmick r9. Game data was not modified.");
