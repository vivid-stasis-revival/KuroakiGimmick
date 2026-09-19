#!/bin/bash
# KuroakiGimmick room FX exporter for macOS. Bash 3.2 compatible.
# Official, pinned UTMT release (self-contained; no Homebrew/.NET SDK needed):
# https://github.com/UnderminersTeam/UndertaleModTool/releases/tag/0.9.2.0
# Usage: bash Dump_Room_FX_Mac.sh [VIVIDSTASIS.app | game.ios | game.unx | data.win] [--out DIR]
# Exports static room FX only. Does not save or modify game data.
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
KuroakiGimmick / macOS 房间 FX 导出

直接运行，自动寻找游戏：
  bash ~/Downloads/Dump_Room_FX_Mac.sh

游戏在其它位置：
  bash ~/Downloads/Dump_Room_FX_Mac.sh "/你的路径/VIVIDSTASIS.app"

也可指定数据文件和输出目录：
  bash ~/Downloads/Dump_Room_FX_Mac.sh "/游戏/Contents/Resources/game.ios" --out ~/Desktop

自动下载并校验 UTMT CLI 0.9.2.0，缓存下载包，内嵌导出脚本。
默认输出到 ~/Documents/KuroakiGimmick/FXExports，每次新建目录。
只读取游戏数据；不需要 sudo、Homebrew、Python 或另外安装 .NET。

运行时创建的图层（例如 custom LBG）不一定存在于静态房间数据中。
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
    cat > "$1" <<'SCARLET_ROOM_FX_CSX'
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
// Completion marker: CLI versions may return success after a script exception.
File.WriteAllText(Path.Combine(output,"COMPLETE.txt"),"Static room FX export completed.\n");
SCARLET_ROOM_FX_CSX
}

sgv_main() {
    local sgv_input='' sgv_out="${HOME}/Documents/KuroakiGimmick/FXExports" sgv_command
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
    local sgv_cache="${SGV_FX_CACHE:-${HOME}/Library/Caches/KuroakiGimmick/UTMT-0.9.2.0}"
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
    SGV_RUN=$(mktemp -d "${sgv_out}/RoomFX_$(date +%Y%m%d_%H%M%S).XXXXXX")
    sgv_embed_exporter "${SGV_RUN}/Dump_Room_FX.csx"
    sgv_message '开始读取游戏并导出房间 FX…'
    sgv_message "日志：${SGV_RUN}/export.log"
    # Deliberately omit --output / --overwrite: never save the loaded game data.
    set +e
    SCARLET_FX_OUTPUT="${SGV_RUN}" "${sgv_cli}" load "${SGV_DATA}" --scripts "${SGV_RUN}/Dump_Room_FX.csx" 2>&1 | tee "${SGV_RUN}/export.log"
    sgv_status=${PIPESTATUS[0]}
    set -e
    [[ "${sgv_status}" -eq 0 ]] || sgv_fail "UTMT CLI 退出码：${sgv_status}。请查看 export.log。"
    [[ -s "${SGV_RUN}/Scarlet_FX_profiles/INDEX.txt" && -f "${SGV_RUN}/Scarlet_FX_profiles/COMPLETE.txt" ]] || \
        sgv_fail '没有得到完整导出。可能选错游戏、没有匹配图层或脚本执行失败；请把 export.log 发回来。'
    cat > "${SGV_RUN}/README.txt" <<'NOTES'
KuroakiGimmick / 静态房间 FX 配置
Scarlet_FX_profiles/INDEX.txt 列出导出的房间。
在查看器中打开歌曲，再用 + FILES / RESOURCES 附加对应房间的 .fx.json。
也可复制为歌曲目录内的 gimmick-fx.json 自动关联。
不同房间参数可能不同，请选实际游玩房间。不能随便用 ASTELLION 房间代替。
本包仅有静态配置和日志，没有歌曲、音频、图集或背景贴图。
运行时创建或改写的图层（例如 custom LBG）可能需要额外的运行时数据。
NOTES
    local sgv_result="${SGV_RUN}.zip"
    (cd "${SGV_RUN}" && zip -q -r "${sgv_result}" Scarlet_FX_profiles README.txt export.log)
    sgv_message ''
    sgv_message "导出完成：${sgv_result}"
    sgv_message '把这个 ZIP 发回来即可。游戏数据没有改动。'
    if command -v open >/dev/null 2>&1; then open -R "${sgv_result}" >/dev/null 2>&1 || true; fi
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then sgv_main "$@"; fi
