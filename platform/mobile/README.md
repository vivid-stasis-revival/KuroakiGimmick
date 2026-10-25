# Kuroaki/Gimmick 移动端实验版 r1

Android：ARM64、Android 8.0/API 26 起，SDL3 GPU/Vulkan，.NET Mono JIT。
iOS：ARM64、iOS 15 起，SDL3 GPU/Metal，.NET Mono AOT。IPA 为未签名真机包，需要自行签名侧载。
支持启动界面、现有 Viewer/Editor、横屏触摸点击/拖动、文件导入、工程保存和图片/谱面导出。
Android 使用 SDL 主循环；iOS 把绘制交给 UIKit 的逐帧回调。
完整桌面界面按屏幕缩放；复杂编辑建议用平板和外接键鼠。

## 安装与文件

- Android 安装 `KuroakiGimmick-Android-arm64-r1.apk`。CI 使用仓库配置的 Android 签名 Secrets。
- iOS 对 `KuroakiGimmick-iOS-arm64-r1-unsigned.ipa` 自行签名并侧载。
- 优先把歌曲目录整体压成 ZIP。Android 和 iOS 均通过 OPEN 的系统文件选择器导入。ZIP 内保留谱面、`info.json`、OGG、图片和工程的相对目录。
- 两平台均可以通过 OPEN FOLDER 选择歌曲文件夹，或通过 OPEN 同时选择多个关联文件。单选谱面不会自动取得它旁边未选中的音频/图片。
- 两平台均将选中的文件复制到应用目录，不改写原始文件；同批关联文件保留文件名，同名冲突会提示，不会覆盖。每次 ZIP 导入建立独立歌曲目录。
- 保存对话框输入文件名，结果写入应用的 `Exports`。
- iOS：文件 App → 我的 iPhone/iPad → Kuroaki/Gimmick，可访问 `Songs`、`Imports`、`Exports`、`UserData`。
- Android：系统文件选择器侧栏 → Kuroaki/Gimmick，可复制或分享导出文件、日志；应用目录通过只读 DocumentsProvider 暴露。
- 日志在 `UserData/mobile.log` 和 `UserData/viewer.log`。

## 当前边界

预览音频支持 Ogg/Vorbis。MP3/FLAC/M4A 等依赖桌面 FFmpeg 的路径和 MP4 导出未启用。
LIVE 多窗口改用现有虚拟桌面预览。沿用当前分支的单指编辑与双指手势逻辑；未新增手机专用布局或全部键盘快捷键的虚拟按键。手势仍需平台复验。
进入后台会暂停播放并停止 GPU 提交；回到前台后手动继续播放。
公开 CI 包不包含游戏素材，首次启动请安装独立资源包，见 `INSTALL-ASSETS.md` / `docs/EXTERNAL_ASSETS.md`。随包仅包含公共内置 shader。自定义歌曲新增 shader 时，先在桌面 bake 对应平台的缓存，再和歌曲一起打包导入。
实机的音频、性能、长谱面内存以及各厂商 Vulkan 驱动仍需测试；模拟器结果不等于实机结果。

## 构建

在仓库根目录执行。需要 Mac、Xcode 26.2、Python 3.12+、CMake、Ninja、Java 21、Android SDK 36、NDK 29.0.14206865，以及用于 shader baker 的 .NET 9 runtime。
本项目使用 SDL 3.4.18 官方源码，下载时核对 SHA-256。只为 iOS 模拟器修正 GPU family 检查，补丁保存在 `patches/SDL3-ios-simulator-metal.patch`；真机仍使用原检查。

```bash
bash platform/mobile/setup-dotnet.sh
sdkmanager --sdk_root="$HOME/Library/Android/sdk" 'platforms;android-36' 'build-tools;36.1.0' 'ndk;29.0.14206865'

python3 platform/mobile/build.py both --external-assets --output dist/KG-Mobile-r1-new
# 使用另一份完整素材或歌曲的额外 shader：
python3 platform/mobile/build.py both --assets /path/to/Assets \
  --shader-root /path/to/Song --output dist/KG-Mobile-r1-with-song-shaders
# 模拟器 .app（仅本机 ad-hoc 签名，独立于未签名真机 IPA）：
python3 platform/mobile/build.py ios --simulator --output dist/KG-Mobile-simulator-new
```

输出目录必须不存在。`--dotnet`、`--baker-dotnet`、`--android-sdk`、`--ndk`、`--java` 可覆盖工具路径。
构建同时生成安装包、SHA-256/大小记录和这份说明。公开仓库不含全部游戏素材，必须提供自己的完整 Assets。
移动端各有独立 csproj，桌面和 `KuroakiSwitch=true` 构建保持原入口。

## 自定义 shader

```bash
dotnet dist/mobile-baker/KuroakiGimmick.dll --bake-mobile-shaders android /path/to/Song/MobileShaders/android /path/to/Song
dotnet dist/mobile-baker/KuroakiGimmick.dll --bake-mobile-shaders ios /path/to/Song/MobileShaders/ios /path/to/Song
```

导入会挑选本平台的 `.kgms` 放入可写缓存。缓存校验格式、原始 shader 源码、uniform/sampler 布局和 payload；缺失或过期缓存报错，手机不调用桌面 shaderc/SPIRV-Cross。

## 验证

```bash
dotnet run --project platform/mobile/tests/MobileChecks.csproj -c Release -p:KuroakiExternalAssets=true -- "$PWD/dist/mobile-assets" "$PWD/Assets"
```

MobileChecks 检查 ZIP 相对资源路径、ZIP 路径穿越拒绝/失败清理、保存目录限制、启动参数以及两平台 shader 包的载入、格式拒绝和损坏拒绝。
具体整包测试记录见交付目录的 `VALIDATION.md`；CPU 测试与编译通过不代表全部手机功能已验证。

## 依赖与许可

沿用 K/G 的许可和 `THIRD_PARTY_NOTICES.md`；SDL3 使用 zlib 许可，原始版权头保留。
参考：[SDL Android](https://wiki.libsdl.org/SDL3/README-android)、[SDL iOS](https://wiki.libsdl.org/SDL3/README-ios)、[匹配 Xcode 26.2 的 .NET workload](https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode26.2-10191)。
