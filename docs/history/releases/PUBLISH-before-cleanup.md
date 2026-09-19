> 历史文档：以下内容来自输入源码及其既有修订，不能作为 r19 的编译、运行或画面验收结果。当前状态见 [VALIDATION_R19.md](VALIDATION_R19.md)，当前结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

# macOS / Windows 发布

在**完整源码根目录**运行。需要 .NET 8 SDK；首次构建会通过 NuGet 恢复项目中固定版本的依赖。目标包自带 .NET 运行时与 SDL3，使用者不用安装 SDK。使用常规 `dotnet publish --self-contained true -r <RID>`，不裁剪、不做 NativeAOT，避免跨平台执行限制与反射资源被误删。参考 [Microsoft dotnet publish 文档](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish)。

## 在 Mac 上构建两个平台

```bash
bash scripts/publish-mac.sh
bash scripts/publish-win.sh
```

第一条默认当前 CPU，生成 `.app`；第二条默认 win-x64，生成 `.exe`。**Windows 包可以在 Mac 上构建，不会尝试在 Mac 上启动 exe。**

显式选择架构：

```bash
bash scripts/publish-mac.sh arm64
bash scripts/publish-mac.sh x64
bash scripts/publish-mac.sh all
bash scripts/publish-win.sh arm64
bash scripts/publish-win.sh all
```

`all` 分别生成两个独立架构包，不是 Universal 合并包。Windows ARM64 包须在相应目标机器验证。

## 在 Windows 上构建

PowerShell：

```powershell
./scripts/publish-windows.ps1
./scripts/publish-windows.ps1 -Arch arm64
```

旧的 `build-mac.sh` / `build-windows.ps1` 转发到这套发布流程。脚本不会改系统执行策略。

## 输出

`dist/KuroakiGimmick-v0.1.0-<RID>-r15-<时间戳>/` 与同名 ZIP。每次用独立暂存目录，构建失败不会把半成品标成发布包，日志保留在 `dist/publish-<RID>-<时间戳>.log`，其他已完成的包不受影响。

- macOS：顶层 `KuroakiGimmick.app` 与 **`Assets` 并列**；字体、Shader、音符、UI、GimmickExtras 等素材统一放在这个目录。原生库、应用程序集和 Samples 在 `.app` 内，应用包图标另放在标准的 `Contents/Resources` 路径。
- Windows：应用程序集、.NET 运行时和 SDL3 合并为单个 `KuroakiGimmick.exe`，原生库由 .NET 在启动时自动解压加载；**`Assets` 和 `Samples` 保持外置**。
- macOS / Windows 发布目录和 ZIP 均不包含源码中的 `docs` 文件夹；保留 README、许可证和第三方声明。
- 请分发**整个 ZIP / 目录**。只复制 `.app` 或 `.exe` 会丢失运行所需素材。源码和新发布包统一使用 `Assets`，不再生成小写 `asset` 目录。
- 不随包附带 ASTELLION 或 Scarlet Beat 的歌曲、曲绘、音乐；谱面由使用者加载。
- macOS 上若有系统 `codesign`，会对完成的 `.app` 做本地 ad-hoc 签名；没有开发者证书签名或 notarization。

视频导出仍需要 FFmpeg；Ogg/Vorbis 预览无需 FFmpeg。在目标机器安装 FFmpeg，或在发布前设置 `KUROAKI_FFMPEG` 为**目标系统与目标架构**的可执行文件。脚本会将其放到应用可查找的位置；分发 FFmpeg 时同时满足该构建的许可证要求。`all` 模式的两个目标请分别发布和指定对应二进制。可用 `KUROAKI_DOTNET` 指定 dotnet 可执行文件绝对路径。

## 本次验证

Bash 脚本通过语法检查；使用模拟 dotnet publisher 验证四个 RID 的发布参数、输出路径、空格路径、统一的 `Assets` 布局、单文件布局、`docs` 排除、ZIP 执行位、失败清理和日志保留，共 28 项。

本次在 macOS 使用真实 SDK 完成 Bash 的 `win-x64` / `osx-arm64` 发布，以及 PowerShell 的 `win-arm64` 发布，并检查发布目录和 ZIP。Windows 包只有一个应用 `.exe`，没有外置 DLL 或运行时 JSON；两个平台均无 `docs`。Windows 程序尚未在目标系统启动验证，PowerShell 脚本此次运行于 macOS。

合并后的 118 个素材文件与原文件哈希一致。macOS 发布目录复制到带空格的临时路径后，从无关工作目录运行通过 128 项自测，并完成 GameUI 自动发现和实际 SDL/OpenGL 界面渲染，确认 `.app` 可使用旁边唯一的 `Assets` 目录。
