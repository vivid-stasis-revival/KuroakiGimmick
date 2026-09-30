#!/bin/bash
# Shared by publish-mac.sh / publish-win.sh. Bash 3.2 (stock macOS) compatible.
set -euo pipefail
KG_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KG_DOTNET="${KUROAKI_DOTNET:-dotnet}"
KG_STAGE=''
kg_cleanup() { if [ -n "${KG_STAGE}" ] && [ -d "${KG_STAGE}" ]; then rm -rf -- "${KG_STAGE}"; fi; }
trap kg_cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
kg_publish() {
    local rid="$1" version build_number name stamp package binary dest log single_file font_asset reference_asset macos_executable
    command -v "${KG_DOTNET}" >/dev/null 2>&1 || { echo '请先安装 .NET 8 SDK，并让 dotnet 位于 PATH。' >&2; return 1; }
    command -v zip >/dev/null 2>&1 || { echo '需要 zip 命令。' >&2; return 1; }
    [ -f "${KG_ROOT}/KuroakiGimmick.csproj" ] || { echo '请把 scripts 放在完整源码目录内。' >&2; return 1; }
    [ -f "${KG_ROOT}/Assets/GameUI/game-ui.gameui.json" ] || { echo '缺少 Assets/GameUI，请使用完整源码包。' >&2; return 1; }
    [ -f "${KG_ROOT}/Assets/Fonts/cjk-editor.png" ] && [ -f "${KG_ROOT}/Assets/Fonts/cjk-editor.json" ] || { echo '缺少 Assets/Fonts/cjk-editor 字体图集。' >&2; return 1; }
    for font_asset in editor-help-sans.png editor-help-sans.json editor-help-sans-bold.png editor-help-sans-bold.json; do
        [ -f "${KG_ROOT}/Assets/Fonts/${font_asset}" ] || { echo "缺少说明卡字体资源：${font_asset}。请使用完整源码包。" >&2; return 1; }
    done
    for reference_asset in vsm-reference.json; do
        [ -f "${KG_ROOT}/Assets/Documentation/${reference_asset}" ] || { echo "缺少语法手册：${reference_asset}。请使用完整源码包。" >&2; return 1; }
    done
    version="$("${KG_DOTNET}" msbuild "${KG_ROOT}/KuroakiGimmick.csproj" -getProperty:Version -nologo | tr -d '\r\n')"
    build_number="$("${KG_DOTNET}" msbuild "${KG_ROOT}/KuroakiGimmick.csproj" -getProperty:BuildNumber -nologo | tr -d '\r\n')"
    [ -n "${version}" ] && [ -n "${build_number}" ] || { echo '无法读取项目版本号。' >&2; return 1; }
    mkdir -p "${KG_ROOT}/dist"
    stamp="$(date '+%Y%m%d-%H%M%S')"
    name="KuroakiGimmick-v${version}-${rid}-${build_number}-${stamp}"
    dest="${KG_ROOT}/dist/${name}"
    [ ! -e "${dest}" ] && [ ! -e "${dest}.zip" ] || { echo "输出已存在：${dest}" >&2; return 1; }
    KG_STAGE="$(mktemp -d "${KG_ROOT}/dist/.publish-${rid}.XXXXXX")"
    package="${KG_STAGE}/${name}"
    log="${KG_ROOT}/dist/publish-${rid}-${stamp}.log"
    case "${rid}" in
        osx-*) binary="${package}/KuroakiGimmick.app/Contents/MacOS"; single_file=true ;;
        win-*) binary="${package}"; single_file=true ;;
        *) echo "不支持的 RID：${rid}" >&2; return 2 ;;
    esac
    echo "Publish ${rid} / Release / self-contained / single-file=${single_file}"
    echo "日志：${log}"
    "${KG_DOTNET}" publish "${KG_ROOT}/KuroakiGimmick.csproj" -c Release -r "${rid}" \
        --self-contained true --nologo -o "${binary}" \
        -p:UseAppHost=true "-p:PublishSingleFile=${single_file}" \
        "-p:IncludeNativeLibrariesForSelfExtract=${single_file}" -p:PublishTrimmed=false \
        -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false \
        "-p:NuGetLockFilePath=${KG_STAGE}/publish.lock.json" 2>&1 | tee "${log}"
    case "${rid}" in
        osx-*)
            [ -f "${binary}/KuroakiGimmick" ] || { echo '发布结果缺少 macOS apphost。' >&2; return 1; }
            # 单文件包必须只有一个可执行文件：散落的 .dylib / .deps.json 说明
            # PublishSingleFile 没生效，那正是这一步要拦住的回归。
            macos_executable="$(find "${binary}" -maxdepth 1 -type f -perm -u+x | wc -l | tr -d ' ')"
            [ "${macos_executable}" = '1' ] || { echo "macOS 单文件包应只有 1 个可执行文件，实际 ${macos_executable} 个。" >&2; return 1; }
            mkdir -p "${package}/KuroakiGimmick.app/Contents/Resources"
            cp "${KG_ROOT}/Assets/App/Kuroaki.icns" "${package}/KuroakiGimmick.app/Contents/Resources/Kuroaki.icns"
            cat > "${package}/KuroakiGimmick.app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>KuroakiGimmick</string>
<key>CFBundleDisplayName</key><string>KuroakiGimmick</string>
<key>CFBundleIdentifier</key><string>local.kuroaki.gimmick</string>
<key>CFBundleExecutable</key><string>KuroakiGimmick</string>
<key>CFBundleIconFile</key><string>Kuroaki.icns</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>${version}</string>
<key>CFBundleVersion</key><string>${build_number}</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
            mv "${binary}/Assets" "${package}/Assets"
            for shared in Samples Integrations; do
                [ ! -d "${binary}/${shared}" ] || mv "${binary}/${shared}" "${package}/${shared}"
            done
            # 真实 dotnet publish 已经给出 0755，这一步只是保证 zip 里的执行位稳定。
            chmod +x "${binary}/KuroakiGimmick"
            ;;
        win-*)
            [ -f "${binary}/KuroakiGimmick.exe" ] || { echo '发布结果缺少 Windows 单文件程序。' >&2; return 1; }
            ;;
    esac
    [ -f "${package}/Assets/GimmickExtras/scene_gameplay.runtime.fx.json" ] || { echo '发布结果缺少 GimmickExtras。' >&2; return 1; }
    [ -f "${package}/Assets/Fonts/cjk-editor.png" ] && [ -f "${package}/Assets/Fonts/cjk-editor.json" ] || { echo '发布结果缺少 CJK 编辑器字体图集。' >&2; return 1; }
    for font_asset in editor-help-sans.png editor-help-sans.json editor-help-sans-bold.png editor-help-sans-bold.json; do
        [ -f "${package}/Assets/Fonts/${font_asset}" ] || { echo "发布结果缺少说明卡字体资源：${font_asset}" >&2; return 1; }
    done
    for reference_asset in vsm-reference.json; do
        [ -f "${package}/Assets/Documentation/${reference_asset}" ] || { echo "发布结果缺少语法手册：${reference_asset}" >&2; return 1; }
    done
    for item in README.md CHANGELOG.md THIRD_PARTY_NOTICES.md; do cp "${KG_ROOT}/${item}" "${package}/${item}"; done
    cp -R "${KG_ROOT}/ThirdParty" "${package}/ThirdParty"
    mkdir -p "${package}/docs"
    # FFmpeg is optional for preview, required for video export. If explicitly
    # supplied, the caller must provide a binary for the TARGET architecture.
    if [ -n "${KUROAKI_FFMPEG:-}" ]; then
        [ -f "${KUROAKI_FFMPEG}" ] || { echo 'KUROAKI_FFMPEG 文件不存在。' >&2; return 1; }
        case "${rid}" in win-*) cp "${KUROAKI_FFMPEG}" "${binary}/ffmpeg.exe" ;; *) cp "${KUROAKI_FFMPEG}" "${binary}/ffmpeg"; chmod +x "${binary}/ffmpeg" ;; esac
    fi
    # Sign after all executable content (including optional FFmpeg) is copied.
    if [ "${rid#osx-}" != "${rid}" ] && [ "$(uname -s)" = Darwin ] && command -v codesign >/dev/null 2>&1; then
        codesign --force --deep --sign - "${package}/KuroakiGimmick.app"
    fi
    (cd "${KG_STAGE}" && COPYFILE_DISABLE=1 zip -q -r "${name}.zip" "${name}" -x '*/.DS_Store' '*/__MACOSX/*')
    mv "${package}" "${dest}"
    mv "${KG_STAGE}/${name}.zip" "${dest}.zip"
    kg_cleanup; KG_STAGE=''
    echo "完成：${dest}.zip"
    echo '本地应用包含非公开的 vivid/stasis Assets，请勿公开上传目录或 ZIP。移动应用时保留完整目录。'
    echo '跨平台 publish 只构建，不会尝试运行目标平台程序。'
}
