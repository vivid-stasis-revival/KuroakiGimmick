#!/bin/bash
# Shared by the NativeAOT publish entry points. Bash 3.2 compatible.
set -euo pipefail
KG_AOT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KG_AOT_DOTNET="${KUROAKI_DOTNET:-dotnet}"
KG_AOT_STAGE=''
kg_aot_cleanup() {
    if [ -n "${KG_AOT_STAGE}" ] && [ -d "${KG_AOT_STAGE}" ]; then
        rm -rf -- "${KG_AOT_STAGE}"
    fi
}
trap kg_aot_cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

kg_aot_publish() {
    local rid="$1" host version build_number stamp name dest package binary log asset item shared library project_arg output_arg lock_arg
    host="$(uname -s)"
    case "${rid}" in
        osx-*)
            [ "${host}" = Darwin ] || { echo 'macOS NativeAOT 必须在 macOS 上构建。' >&2; return 2; }
            ;;
        win-*)
            case "${host}" in
                MINGW*|MSYS*|CYGWIN*) ;;
                *) echo 'Windows NativeAOT 必须在 Windows 上构建（可使用 Git Bash）。' >&2; return 2 ;;
            esac
            ;;
        *) echo "不支持的 RID：${rid}" >&2; return 2 ;;
    esac
    command -v "${KG_AOT_DOTNET}" >/dev/null 2>&1 || { echo '需要 .NET 8 或更新的 SDK。' >&2; return 1; }
    command -v zip >/dev/null 2>&1 || { echo '需要 zip 命令。' >&2; return 1; }
    [ -f "${KG_AOT_ROOT}/KuroakiGimmick.csproj" ] || { echo '请把 scripts 放在完整源码目录内。' >&2; return 1; }
    project_arg="${KG_AOT_ROOT}/KuroakiGimmick.csproj"
    if [ "${rid#win-}" != "${rid}" ]; then
        command -v cygpath >/dev/null 2>&1 || { echo 'Windows Git Bash 需要 cygpath。' >&2; return 1; }
        project_arg="$(cygpath -w "${project_arg}")"
    fi
    for asset in \
        Assets/GameUI/game-ui.gameui.json \
        Assets/Fonts/cjk-editor.png Assets/Fonts/cjk-editor.json \
        Assets/Fonts/editor-help-sans.png Assets/Fonts/editor-help-sans.json \
        Assets/Fonts/editor-help-sans-bold.png Assets/Fonts/editor-help-sans-bold.json \
        Assets/GimmickExtras/scene_gameplay.runtime.fx.json \
        Assets/Documentation/vsm-reference.json; do
        [ -f "${KG_AOT_ROOT}/${asset}" ] || { echo "缺少发行资源：${asset}" >&2; return 1; }
    done
    if [ "${rid#osx-}" != "${rid}" ]; then
        [ -f "${KG_AOT_ROOT}/Assets/App/Kuroaki.icns" ] || { echo '缺少 macOS 应用图标。' >&2; return 1; }
    fi
    version="$("${KG_AOT_DOTNET}" msbuild "${project_arg}" -getProperty:Version -nologo | tr -d '\r\n')"
    build_number="$("${KG_AOT_DOTNET}" msbuild "${project_arg}" -getProperty:BuildNumber -nologo | tr -d '\r\n')"
    [ -n "${version}" ] && [ -n "${build_number}" ] || { echo '无法读取项目版本号。' >&2; return 1; }
    mkdir -p "${KG_AOT_ROOT}/dist"
    stamp="$(date '+%Y%m%d-%H%M%S')"
    name="KuroakiGimmick-v${version}-${rid}-NativeAOT-${stamp}"
    dest="${KG_AOT_ROOT}/dist/${name}"
    [ ! -e "${dest}" ] && [ ! -e "${dest}.zip" ] || { echo "输出已存在：${dest}" >&2; return 1; }
    KG_AOT_STAGE="$(mktemp -d "${KG_AOT_ROOT}/dist/.publish-aot-${rid}.XXXXXX")"
    package="${KG_AOT_STAGE}/${name}"
    log="${KG_AOT_ROOT}/dist/publish-aot-${rid}-${stamp}.log"
    case "${rid}" in
        osx-*) binary="${package}/KuroakiGimmick.app/Contents/MacOS" ;;
        win-*) binary="${package}" ;;
    esac
    output_arg="${binary}"
    lock_arg="${KG_AOT_STAGE}/publish.lock.json"
    if [ "${rid#win-}" != "${rid}" ]; then
        output_arg="$(cygpath -w "${output_arg}")"
        lock_arg="$(cygpath -w "${lock_arg}")"
    fi
    echo "NativeAOT publish ${rid} / Release"
    echo "日志：${log}"
    "${KG_AOT_DOTNET}" publish "${project_arg}" -c Release -r "${rid}" \
        --self-contained true --nologo -o "${output_arg}" \
        -p:PublishAot=true -p:PublishSingleFile=false \
        -p:DebugType=None -p:DebugSymbols=false \
        "-p:NuGetLockFilePath=${lock_arg}" 2>&1 | tee "${log}"
    [ ! -e "${binary}/KuroakiGimmick.deps.json" ] && [ ! -e "${binary}/KuroakiGimmick.runtimeconfig.json" ] \
        || { echo '发布结果包含 .NET 运行时配置文件，NativeAOT 未生效。' >&2; return 1; }
    case "${rid}" in
        osx-*)
            [ -x "${binary}/KuroakiGimmick" ] || { echo '发布结果缺少 macOS 原生可执行文件。' >&2; return 1; }
            for library in libSDL3.dylib libshaderc_shared.dylib libspirv-cross.dylib; do
                [ -f "${binary}/${library}" ] || { echo "发布结果缺少原生库：${library}" >&2; return 1; }
            done
            rm -rf -- "${binary}/KuroakiGimmick.dsym"
            mkdir -p "${package}/KuroakiGimmick.app/Contents/Resources"
            cp "${KG_AOT_ROOT}/Assets/App/Kuroaki.icns" "${package}/KuroakiGimmick.app/Contents/Resources/Kuroaki.icns"
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
            for shared in Integrations; do
                [ ! -d "${binary}/${shared}" ] || mv "${binary}/${shared}" "${package}/${shared}"
            done
            chmod +x "${binary}/KuroakiGimmick"
            ;;
        win-*)
            [ -f "${binary}/KuroakiGimmick.exe" ] || { echo '发布结果缺少 Windows 原生可执行文件。' >&2; return 1; }
            for library in SDL3.dll shaderc_shared.dll spirv-cross.dll; do
                [ -f "${binary}/${library}" ] || { echo "发布结果缺少原生库：${library}" >&2; return 1; }
            done
            ;;
    esac
    for asset in \
        Assets/GameUI/game-ui.gameui.json \
        Assets/Fonts/cjk-editor.png Assets/Fonts/cjk-editor.json \
        Assets/Fonts/editor-help-sans.png Assets/Fonts/editor-help-sans.json \
        Assets/Fonts/editor-help-sans-bold.png Assets/Fonts/editor-help-sans-bold.json \
        Assets/GimmickExtras/scene_gameplay.runtime.fx.json \
        Assets/Documentation/vsm-reference.json; do
        [ -f "${package}/${asset}" ] || { echo "发布结果缺少资源：${asset}" >&2; return 1; }
    done
    for item in README.md CHANGELOG.md THIRD_PARTY_NOTICES.md; do cp "${KG_AOT_ROOT}/${item}" "${package}/${item}"; done
    cp -R "${KG_AOT_ROOT}/ThirdParty" "${package}/ThirdParty"
    mkdir -p "${package}/docs"
    if [ -n "${KUROAKI_FFMPEG:-}" ]; then
        [ -f "${KUROAKI_FFMPEG}" ] || { echo 'KUROAKI_FFMPEG 文件不存在。' >&2; return 1; }
        case "${rid}" in
            win-*) cp "${KUROAKI_FFMPEG}" "${binary}/ffmpeg.exe" ;;
            *) cp "${KUROAKI_FFMPEG}" "${binary}/ffmpeg"; chmod +x "${binary}/ffmpeg" ;;
        esac
    fi
    if [ "${rid#osx-}" != "${rid}" ] && command -v codesign >/dev/null 2>&1; then
        codesign --force --deep --sign - "${package}/KuroakiGimmick.app"
    fi
    (cd "${KG_AOT_STAGE}" && COPYFILE_DISABLE=1 zip -q -r "${name}.zip" "${name}" -x '*/.DS_Store' '*/__MACOSX/*')
    mv "${package}" "${dest}"
    mv "${KG_AOT_STAGE}/${name}.zip" "${dest}.zip"
    kg_aot_cleanup; KG_AOT_STAGE=''
    echo "完成：${dest}.zip"
    echo '本地应用包含非公开的 vivid/stasis Assets，请勿公开上传目录或 ZIP。移动应用时保留完整目录。'
}
