using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using UndertaleModLib.Util;

// Read-only. Export only the note skin sprites, never song resources.
EnsureDataLoaded();
string output=Environment.GetEnvironmentVariable("KUROAKI_NOTE_OUTPUT");
if(string.IsNullOrWhiteSpace(output))output=PromptChooseDirectory();
if(string.IsNullOrWhiteSpace(output))throw new Exception("No output directory selected.");
output=Path.Combine(Path.GetFullPath(output),"NoteSkinFull");
string exactDir=Path.Combine(output,"NotesExact");
string metaDir=Path.Combine(output,"metadata");
Directory.CreateDirectory(exactDir);
Directory.CreateDirectory(metaDir);

// 四套皮肤的精灵表照抄 obj_custom_gimmick 的 InitSkinChange，那里的 singleSkins / bumperSkins 就是
// changeskin 这个 gimmick 直接赋给 obj_note_rendering.lane_sprites 的东西，取值 0-3 依次是
// 正常 / stopmotion / extendnova（纯黑）/ stargazers。
// 槽位含义由 obj_pressed_holds 的 Draw 事件钉死：lane_sprites[i][2] 画 body、[3] 画尾帽，
// 于是 single 的五个槽是 [chip, hold 头, hold body, hold 尾, chip 地雷]，bumper 的三个槽是
// [bumper, 判定 bumper, bumper 地雷]。
// null 表示原表里写的是 sp_empty：那一类音符在这套皮肤下就是不画，不能退回正常皮肤顶上。
string[][] singleSkins=new[]
{
    new[]{"sp_note_chip_normal","sp_note_chip_normal","sp_note_hold_normal","sp_note_chip_normal","sp_note_chip_mine_normal"},
    new[]{"sp_note_chip_stopmotion","sp_note_hold_start_stopmotion","sp_note_hold_stopmotion","sp_note_hold_end_stopmotion",null},
    new[]{"sp_note_chip_extendnova","sp_note_chip_extendnova","sp_note_hold_extendnova","sp_note_chip_extendnova","sp_note_chip_mine_normal"},
    new[]{"sp_note_chip_stargazers","sp_note_hold_start_stargazers","sp_note_hold_stargazers","sp_note_hold_end_stargazers",null}
};
string[][] bumperSkins=new[]
{
    new[]{"sp_note_bumper_normal","sp_note_bumper_timing_normal","sp_note_bumper_mine_normal"},
    new[]{"sp_note_bumper_stopmotion",null,null},
    new[]{"sp_note_bumper_extendnova","sp_note_bumper_timing_normal","sp_note_bumper_mine_normal"},
    new[]{"sp_note_bumper_stargazers","sp_note_bumper_timing_stargazers",null}
};
string[] skinNames=new[]{"normal","stopmotion","extendnova","stargazers"};

// chip 系精灵两帧对应轨道 0-1 / 2-3，bumper 系三帧对应 L / M / R，四套皮肤结构一致。
var mapping=new List<(int Skin,string Key,string Sprite,int Frame)>();
for(int skin=0;skin<skinNames.Length;skin++)
{
    var single=singleSkins[skin];
    var bumper=bumperSkins[skin];
    void Single(string prefix,string sprite)
    {
        mapping.Add((skin,prefix+"_L",sprite,0));
        mapping.Add((skin,prefix+"_R",sprite,1));
    }
    void Bumper(string prefix,string sprite)
    {
        mapping.Add((skin,prefix+"_L",sprite,0));
        mapping.Add((skin,prefix+"_M",sprite,1));
        mapping.Add((skin,prefix+"_R",sprite,2));
    }
    Single("chip",single[0]);
    Single("hold_head",single[1]);
    Single("hold_body",single[2]);
    Single("hold_end",single[3]);
    Single("chip_mine",single[4]);
    Bumper("bumper",bumper[0]);
    Bumper("judge_bumper",bumper[1]);
    Bumper("bumper_mine",bumper[2]);
}

var spriteMeta=new List<object>();
var frameMeta=new List<object>();
var files=new List<object>();
var empties=new List<string>[skinNames.Length];
for(int i=0;i<empties.Length;i++)empties[i]=new List<string>();
using(var worker=new TextureWorker())
{
    foreach(string name in mapping.Select(m=>m.Sprite).Where(s=>s!=null).Distinct())
    {
        var sprite=Data.Sprites.FirstOrDefault(s=>s?.Name?.Content==name);
        if(sprite==null||sprite.Textures.Count==0)throw new Exception("Missing original note sprite: "+name);
        spriteMeta.Add(new{name,width=(int)sprite.Width,height=(int)sprite.Height,origin_x=(int)sprite.OriginX,origin_y=(int)sprite.OriginY,
            frame_count=sprite.Textures.Count});
        for(int i=0;i<sprite.Textures.Count;i++)
        {
            var item=sprite.Textures[i].Texture;
            frameMeta.Add(new{sprite=name,frame=i,target=new{x=(int)item.TargetX,y=(int)item.TargetY,width=(int)item.TargetWidth,
                height=(int)item.TargetHeight},source=new{x=(int)item.SourceX,y=(int)item.SourceY,width=(int)item.SourceWidth,
                height=(int)item.SourceHeight},bounding=new{width=(int)item.BoundingWidth,height=(int)item.BoundingHeight}});
        }
    }
    foreach(var m in mapping)
    {
        int skin=m.Skin;
        string key=m.Key,name=m.Sprite;
        // 整张精灵在原表里就是 sp_empty，这一类音符在这套皮肤下不存在。
        if(name==null){empties[skin].Add(key);continue;}
        var sprite=Data.Sprites.First(s=>s?.Name?.Content==name);
        if(m.Frame>=sprite.Textures.Count)throw new Exception("Note sprite "+name+" has no frame "+m.Frame+".");
        var item=sprite.Textures[m.Frame].Texture;
        // GameMaker 把整帧全透明的子图压成 1x1 占位。stopmotion 和 stargazers 的 bumper 就没有 M 帧，
        // 第 1 帧正是这样一个 1x1 空图。照导会在原点留一个一像素的点，所以当成"不画"处理；
        // 精灵本身就只有 1x1 大的另说（那种不会走到这里，null 已经先挡掉了）。
        if(item.TargetWidth<=1&&item.TargetHeight<=1&&(sprite.Width>1||sprite.Height>1)){empties[skin].Add(key);continue;}
        string file=skin==0?key+".png":skinNames[skin]+"/"+key+".png";
        string path=Path.Combine(exactDir,file.Replace('/',Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        // exact-lanes v2 用 metadata 里的 Target 矩形把裁切放回 GameMaker 包围盒，
        // 所以这里导出的是未补边的原始裁切；width/height 必须等于 PNG 的真实像素尺寸。
        worker.ExportAsPNG(item,path,null,false);
        files.Add(new{file,skin,source_sprite=name,source_frame=m.Frame,width=(int)item.TargetWidth,height=(int)item.TargetHeight,
            bounding_size=new[]{(int)sprite.Width,(int)sprite.Height},origin=new[]{(int)sprite.OriginX,(int)sprite.OriginY}});
    }
}
// 正常皮肤必须是完整的：它是唯一被 NoteSkinProfile 强制校验的一套，缺一张都意味着上面的表抄错了。
if(empties[0].Count>0)throw new Exception("Default note skin must not have empty slots: "+string.Join(", ",empties[0]));
var options=new JsonSerializerOptions{WriteIndented=true};
File.WriteAllText(Path.Combine(metaDir,"sprites.json"),JsonSerializer.Serialize(spriteMeta,options));
File.WriteAllText(Path.Combine(metaDir,"frames.json"),JsonSerializer.Serialize(frameMeta,options));
File.WriteAllText(Path.Combine(exactDir,"manifest.json"),JsonSerializer.Serialize(new{format="vividstasis-default-note-skin-exact-lanes-v2",
    version=3,generated_by="Dump_Notes.csx",source_file=string.IsNullOrEmpty(FilePath)?"":Path.GetFileName(FilePath),
    note="Frame indices follow obj_note_rendering: chip lanes use 0 for lanes 0-1 and 1 for lanes 2-3, bumper lanes use lane - 4. "+
        "Skin 0 is the default and is always complete; skins 1-3 are the changeskin targets and list the keys they intentionally do not draw.",
    skins=Enumerable.Range(0,skinNames.Length).Select(i=>new{index=i,name=skinNames[i],empty=empties[i]}).ToArray(),
    files},options));
File.WriteAllText(Path.Combine(output,"COMPLETE.txt"),"Note skin export completed ("+skinNames.Length+" skins).\n");
ScriptMessage("Note skins exported to:\n"+output+"\nInstall with scripts/assets/install-note-dump.sh. Game data was not modified.");
