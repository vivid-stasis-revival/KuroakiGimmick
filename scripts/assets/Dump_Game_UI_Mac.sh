#!/bin/bash
# KuroakiGimmick game UI exporter for macOS. Bash 3.2 compatible.
# Official, pinned UTMT release (self-contained; no Homebrew/.NET SDK needed):
# https://github.com/UnderminersTeam/UndertaleModTool/releases/tag/0.9.2.0
# Usage: bash Dump_Game_UI_Mac.sh [VIVIDSTASIS.app | game.ios | game.unx | data.win] [--out DIR]
# Exports only shared UI sprites and font atlases. Does not save or modify game data.
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
KuroakiGimmick / macOS 玩法 UI / 字体 导出

直接运行，自动寻找游戏：
  bash ~/Downloads/Dump_Game_UI_Mac.sh

游戏在其它位置：
  bash ~/Downloads/Dump_Game_UI_Mac.sh "/你的路径/VIVIDSTASIS.app"

也可指定数据文件和输出目录：
  bash ~/Downloads/Dump_Game_UI_Mac.sh "/游戏/Contents/Resources/game.ios" --out ~/Desktop

自动下载并校验 UTMT CLI 0.9.2.0，缓存下载包，内嵌导出脚本。
默认输出到脚本旁 Assets/GameUI；--out 可指定 KuroakiGimmick.app 所在文件夹。
只读取游戏数据；不需要 sudo、Homebrew、Python 或另外安装 .NET。

仅导出原始通用玩法 UI 和字体；导出后按查看器 R 自动读取。
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
    cat > "$1" <<'KUROAKI_GAME_UI_CSX'
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
KUROAKI_GAME_UI_CSX
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
    SGV_RUN=$(mktemp -d "${sgv_out}/GameUI_$(date +%Y%m%d_%H%M%S).XXXXXX")
    sgv_embed_exporter "${SGV_RUN}/Dump_Game_UI.csx"
    sgv_message '开始读取游戏并导出玩法 UI / 字体…'
    sgv_message "日志：${SGV_RUN}/export.log"
    # Deliberately omit --output / --overwrite: never save the loaded game data.
    set +e
    KUROAKI_UI_OUTPUT="${SGV_RUN}" "${sgv_cli}" load "${SGV_DATA}" --scripts "${SGV_RUN}/Dump_Game_UI.csx" 2>&1 | tee "${SGV_RUN}/export.log"
    sgv_status=${PIPESTATUS[0]}
    set -e
    [[ "${sgv_status}" -eq 0 ]] || sgv_fail "UTMT CLI 退出码：${sgv_status}。请查看 export.log。"
    [[ -s "${SGV_RUN}/GameUI/game-ui.gameui.json" && -f "${SGV_RUN}/GameUI/COMPLETE.txt" ]] || \
        sgv_fail '没有得到完整导出。可能选错游戏、没有匹配图层或脚本执行失败；请把 export.log 发回来。'
    mkdir -p "${sgv_out}/Assets"
    local sgv_staged="${sgv_out}/Assets/GameUI.next.$$"
    cp -R "${SGV_RUN}/GameUI" "${sgv_staged}"
    if [[ -e "${sgv_out}/Assets/GameUI" ]]; then
        mv "${sgv_out}/Assets/GameUI" "${sgv_out}/Assets/GameUI.previous.$(date +%Y%m%d_%H%M%S).$$"
    fi
    mv "${sgv_staged}" "${sgv_out}/Assets/GameUI"
    sgv_message "UI 资源：${sgv_out}/Assets/GameUI"
    cat > "${SGV_RUN}/README.txt" <<'NOTES'
KuroakiGimmick / 原始玩法 UI 和字体
UI 安装在 --out 指定目录的 Assets/GameUI/，默认是项目根目录。导出的游戏资源不公开。
把 Assets 文件夹和 KuroakiGimmick.app 放在同一目录，重新打开歌曲或按 R 读取。
自定义位置也可以拖入 Assets/GameUI/game-ui.gameui.json 或用 --game-ui 指定。
仅包含通用 UI 精灵和字体，不包含歌曲、音频、曲绘或背景素材。
NOTES
    local sgv_result="${SGV_RUN}.zip"
    (cd "${SGV_RUN}" && zip -q -r "${sgv_result}" GameUI README.txt export.log)
    sgv_message ''
    sgv_message "导出完成：${sgv_result}"
    sgv_message '重新打开 KuroakiGimmick r9 的歌曲或按 R，自动读取这次导出的原 UI。游戏数据没有改动。'
    if command -v open >/dev/null 2>&1; then open -R "${sgv_result}" >/dev/null 2>&1 || true; fi
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then sgv_main "$@"; fi
