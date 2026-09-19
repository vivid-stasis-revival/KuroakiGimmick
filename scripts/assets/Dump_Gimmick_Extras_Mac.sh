#!/bin/bash
# KuroakiGimmick gimmick resources exporter for macOS. Bash 3.2 compatible.
# Official, pinned UTMT release (self-contained; no Homebrew/.NET SDK needed):
# https://github.com/UnderminersTeam/UndertaleModTool/releases/tag/0.9.2.0
# Usage: bash Dump_Gimmick_Extras_Mac.sh [VIVIDSTASIS.app | game.ios | game.unx | data.win] [--out DIR]
# Exports only checker tiles and glow shader. Does not save or modify game data.
set -euo pipefail
export LC_ALL=C
SGV_WORK=''
SGV_RUN=''
SGV_DATA=''

sgv_message() { printf '%s\n' "$*"; }
sgv_fail() { printf '\n错误：%s\n' "$*" >&2; exit 1; }
sgv_cleanup() {
    local sgv_status=$?
    trap - EXIT
    if [[ -n "${SGV_WORK}" && -d "${SGV_WORK}" ]]; then rm -rf -- "${SGV_WORK}"; fi
    if [[ "${sgv_status}" -ne 0 ]]; then
        printf '\n导出未完成，退出码 %s。\n' "${sgv_status}" >&2
        if [[ -n "${SGV_RUN}" ]]; then printf '输出和日志保留在：%s\n' "${SGV_RUN}" >&2; fi
    fi
    exit "${sgv_status}"
}

sgv_help() {
    cat <<'HELP'
KuroakiGimmick / macOS 棋盘 / glow / 动态 FX 定义 导出

直接运行，自动寻找游戏：
  bash ~/Downloads/Dump_Gimmick_Extras_Mac.sh

游戏在其它位置：
  bash ~/Downloads/Dump_Gimmick_Extras_Mac.sh "/你的路径/VIVIDSTASIS.app"

也可指定数据文件和输出目录：
  bash ~/Downloads/Dump_Gimmick_Extras_Mac.sh "/游戏/Contents/Resources/game.ios" --out ~/Desktop

自动下载并校验 UTMT CLI 0.9.2.0，缓存下载包，内嵌导出脚本。
默认输出到脚本旁 Assets/GimmickExtras；--out 可指定 KuroakiGimmick.app 所在文件夹。
只读取游戏数据；不需要 sudo、Homebrew、Python 或另外安装 .NET。

棋盘贴图导出后按查看器 R 自动读取；glow / FX 定义需发回核对后继续适配。
HELP
}

sgv_accept_data() {
    local sgv_path="$1" sgv_magic
    [[ -f "${sgv_path}" && -r "${sgv_path}" ]] || return 1
    sgv_magic=$(dd if="${sgv_path}" bs=4 count=1 2>/dev/null) || return 1
    [[ "${sgv_magic}" == FORM ]] || return 1
    SGV_DATA="$(cd "$(dirname "${sgv_path}")" && pwd)/$(basename "${sgv_path}")"
    return 0
}

sgv_find_data() {
    local sgv_root="$1" sgv_dir sgv_name sgv_file
    if sgv_accept_data "${sgv_root}"; then return 0; fi
    [[ -d "${sgv_root}" ]] || return 1
    for sgv_dir in "${sgv_root}/Contents/Resources" "${sgv_root}"; do
        for sgv_name in game.ios game.unx data.win game.win; do
            if sgv_accept_data "${sgv_dir}/${sgv_name}"; then return 0; fi
        done
    done
    while IFS= read -r -d '' sgv_file; do
        if sgv_accept_data "${sgv_file}"; then return 0; fi
    done < <(find "${sgv_root}" -type f \( -iname game.ios -o -iname game.unx -o -iname data.win -o -iname game.win \) -print0 2>/dev/null)
    return 1
}

sgv_locate_game() {
    local sgv_given="$1" sgv_root sgv_app
    if [[ -n "${sgv_given}" ]]; then
        sgv_find_data "${sgv_given}" || sgv_fail "指定路径内未找到可读的 GameMaker 数据：${sgv_given}"
        return
    fi
    for sgv_root in "/Applications/VIVIDSTASIS.app" "${HOME}/Applications/VIVIDSTASIS.app" \
        "${HOME}/Library/Application Support/Steam/steamapps/common/VIVIDSTASIS" \
        "${HOME}/Library/Application Support/Steam/steamapps/common/vividstasis"; do
        if sgv_find_data "${sgv_root}"; then return; fi
    done
    # Case variants / nested app folders; search only application and Steam roots.
    for sgv_root in /Applications "${HOME}/Applications" "${HOME}/Library/Application Support/Steam/steamapps/common"; do
        [[ -d "${sgv_root}" ]] || continue
        while IFS= read -r -d '' sgv_app; do
            if sgv_find_data "${sgv_app}"; then return; fi
        done < <(find "${sgv_root}" -type d -name '*.app' -prune -iname '*vividstasis*.app' -print0 2>/dev/null)
    done
    sgv_fail '没有找到 VIVIDSTASIS。请在命令最后加上 .app 或 game.ios 的完整路径，路径用双引号包住。'
}

sgv_choose_arch() {
    SGV_ARCH=$(uname -m)
    if [[ "$(sysctl -n hw.optional.arm64 2>/dev/null || true)" == 1 ]]; then SGV_ARCH=arm64; fi
    case "${SGV_ARCH}" in
        arm64)
            SGV_ASSET=UTMT_CLI_v0.9.2.0-macOS.zip
            SGV_SHA=03042fa77f93e9b7fcb06592b77fb8369876e6d6fff7680a63dc0fa2df5444ef
            ;;
        x86_64)
            # Official asset uses the x86 label for the Intel macOS build.
            SGV_ASSET=UTMT_CLI_v0.9.2.0-macOS-x86.zip
            SGV_SHA=8c210151736d828a6fa47bcf86e1365c20d1f2d587a0341274490e6026c09ee6
            ;;
        *) sgv_fail "不支持的芯片架构：${SGV_ARCH}" ;;
    esac
}

sgv_verified() {
    [[ -f "$1" ]] || return 1
    local sgv_digest
    sgv_digest=$(shasum -a 256 "$1") || return 1
    [[ "${sgv_digest%% *}" == "${SGV_SHA}" ]]
}

sgv_embed_exporter() {
    cat > "$1" <<'KUROAKI_EXTRAS_CSX'
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
KUROAKI_EXTRAS_CSX
}

sgv_main() {
    local sgv_input='' sgv_out sgv_command
    sgv_out="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
    while [[ $# -gt 0 ]]; do
        case "$1" in
            -h|--help) sgv_help; return 0 ;;
            --out)
                [[ $# -ge 2 && -n "$2" ]] || sgv_fail '--out 后面需要输出目录。'
                sgv_out="$2"; shift 2 ;;
            --)
                shift
                [[ $# -eq 1 && -z "${sgv_input}" ]] || sgv_fail '-- 后面只能有一个游戏路径。'
                sgv_input="$1"; shift ;;
            -*) sgv_fail "未知参数：$1（用 --help 查看用法）" ;;
            *)
                [[ -z "${sgv_input}" ]] || sgv_fail '只能指定一个游戏路径。'
                sgv_input="$1"; shift ;;
        esac
    done
    [[ "$(uname -s)" == Darwin ]] || sgv_fail '这个脚本用于 macOS。'
    for sgv_command in curl unzip zip shasum find mktemp file dd; do
        command -v "${sgv_command}" >/dev/null 2>&1 || sgv_fail "缺少命令：${sgv_command}"
    done
    sgv_locate_game "${sgv_input}"
    sgv_choose_arch
    trap sgv_cleanup EXIT
    trap 'exit 130' INT
    trap 'exit 143' TERM
    local sgv_cache="${SGV_UI_CACHE:-${HOME}/Library/Caches/KuroakiGimmick/UTMT-0.9.2.0}"
    mkdir -p "${sgv_cache}" "${sgv_out}"
    sgv_cache=$(cd "${sgv_cache}" && pwd)
    sgv_out=$(cd "${sgv_out}" && pwd)
    SGV_WORK=$(mktemp -d "${sgv_cache}/work.XXXXXX")
    local sgv_zip="${sgv_cache}/${SGV_ASSET}" sgv_url sgv_cli sgv_status sgv_binary
    sgv_message "游戏数据：${SGV_DATA}"
    sgv_message "芯片：${SGV_ARCH}；UTMT CLI：0.9.2.0"
    if sgv_verified "${sgv_zip}"; then
        sgv_message '下载包校验通过，复用缓存。'
    else
        sgv_url="https://github.com/UnderminersTeam/UndertaleModTool/releases/download/0.9.2.0/${SGV_ASSET}"
        sgv_message '下载官方 UTMT CLI…'
        curl --fail --location --show-error --progress-bar --proto '=https' --proto-redir '=https' \
            --connect-timeout 20 --max-time 600 --retry 2 --retry-delay 2 \
            "${sgv_url}" --output "${SGV_WORK}/download.zip"
        sgv_verified "${SGV_WORK}/download.zip" || sgv_fail '下载包 SHA-256 不匹配，未执行。请重试下载。'
        mv -f "${SGV_WORK}/download.zip" "${sgv_zip}"
    fi
    unzip -q "${sgv_zip}" -d "${SGV_WORK}/cli"
    sgv_cli="${SGV_WORK}/cli/UndertaleModCli"
    [[ -f "${sgv_cli}" ]] || sgv_fail '下载包中没有找到 UndertaleModCli。'
    chmod +x "${sgv_cli}"
    sgv_binary=$(file -b "${sgv_cli}")
    case "${SGV_ARCH}:${sgv_binary}" in
        arm64:*Mach-O*arm64*|x86_64:*Mach-O*x86_64*) ;;
        *) sgv_fail "CLI 架构不匹配：${sgv_binary}" ;;
    esac
    SGV_RUN=$(mktemp -d "${sgv_out}/GimmickExtras_$(date +%Y%m%d_%H%M%S).XXXXXX")
    sgv_embed_exporter "${SGV_RUN}/Dump_Gimmick_Extras.csx"
    sgv_message '开始读取游戏并导出棋盘 / glow / 动态 FX 定义…'
    sgv_message "日志：${SGV_RUN}/export.log"
    # Deliberately omit --output / --overwrite: never save the loaded game data.
    set +e
    KUROAKI_EXTRAS_OUTPUT="${SGV_RUN}" "${sgv_cli}" load "${SGV_DATA}" --scripts "${SGV_RUN}/Dump_Gimmick_Extras.csx" 2>&1 | tee "${SGV_RUN}/export.log"
    sgv_status=${PIPESTATUS[0]}
    set -e
    [[ "${sgv_status}" -eq 0 ]] || sgv_fail "UTMT CLI 退出码：${sgv_status}。请查看 export.log。"
    [[ -s "${SGV_RUN}/GimmickExtras/gimmick-extras.json" && -f "${SGV_RUN}/GimmickExtras/COMPLETE.txt" ]] || \
        sgv_fail '没有得到完整导出。可能选错游戏、没有匹配图层或脚本执行失败；请把 export.log 发回来。'
    mkdir -p "${sgv_out}/Assets"
    local sgv_staged="${sgv_out}/Assets/GimmickExtras.next.$$"
    cp -R "${SGV_RUN}/GimmickExtras" "${sgv_staged}"
    if [[ -e "${sgv_out}/Assets/GimmickExtras" ]]; then
        mv "${sgv_out}/Assets/GimmickExtras" "${sgv_out}/Assets/GimmickExtras.previous.$(date +%Y%m%d_%H%M%S).$$"
    fi
    mv "${sgv_staged}" "${sgv_out}/Assets/GimmickExtras"
    sgv_message "Gimmick 资源：${sgv_out}/Assets/GimmickExtras"
    cat > "${SGV_RUN}/README.txt" <<'NOTES'
KuroakiGimmick / 原始棋盘贴图和 glow shader
棋盘资源安装在 --out 指定目录的 Assets/GimmickExtras/，默认是项目根目录。导出的游戏资源不公开。
把 Assets 文件夹和 KuroakiGimmick.app 放在同一目录，重新打开歌曲或按 R 读取。
源码运行请把 Assets/GimmickExtras 复制到输出可执行文件旁的 Assets/ 下。
导出三帧棋盘、所有名称含 glow 的 shader、玩法房间 FX 与相关代码；不会导出歌曲、音频、曲绘或整张图集。
棋盘与动态 LBG 默认值可由 r12 直接读取；请把 ZIP 发回核对实际房间与 glow 实现。
NOTES
    local sgv_result="${SGV_RUN}.zip"
    (cd "${SGV_RUN}" && zip -q -r "${sgv_result}" GimmickExtras README.txt export.log)
    sgv_message ''
    sgv_message "导出完成：${sgv_result}"
    sgv_message '重新打开 KuroakiGimmick r12 的歌曲或按 R，自动读取这次导出的棋盘资源。游戏数据没有改动。'
    if command -v open >/dev/null 2>&1; then open -R "${sgv_result}" >/dev/null 2>&1 || true; fi
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then sgv_main "$@"; fi
