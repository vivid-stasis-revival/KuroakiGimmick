# 移动端与 Horizon 适配状态

桌面应用使用 `net8.0`。交接包中的 .NET 10 iOS/Android 工程、UIKit/SDL 入口和 Switch 原生入口现已接入。Windows、macOS、Linux、Android 与 iOS 已在 GitHub CI 完成构建；设备验证仍待完成。Switch CI 已接入 NroForge，须以完整 K/G 构建结果及实机测试分别判断可用性。

## 已包含的接口

- `KG_MOBILE`：应用私有目录、文件选择回调、ZIP 歌曲导入、后台暂停、预编译着色器加载，以及单帧更新入口。
- `KG_IOS`（同时需要 `KG_MOBILE`）：静态链接 SDL 的 `__Internal` 导入、iOS 回调标记，以及由 UIKit 驱动的帧循环接口。
- `KG_SWITCH`：Horizon/mono-nx 路径策略、`KGSwitch` 输入与路径选择接口、纯托管 SHA-256、音频缓冲和帧耗时记录。
- 移动端和 Switch 禁用依赖桌面 FFmpeg 进程的视频导出。

这些分支保留桌面版现有触摸逻辑、音频供给线程、GPU 帧背压和实例化绘制。条件编译仅检查 C# 源码，不等同于平台链接或设备验证。

## 着色器预编译

使用桌面构建执行，输出目录由调用者指定：

```sh
dotnet build KuroakiGimmick.csproj -c Release -p:PublishAot=false
dotnet bin/Release/net8.0/KuroakiGimmick.dll --bake-mobile-shaders ios /path/to/ios/MobileShaders
dotnet bin/Release/net8.0/KuroakiGimmick.dll --bake-mobile-shaders android /path/to/android/MobileShaders
dotnet bin/Release/net8.0/KuroakiGimmick.dll --bake-switch-shaders /path/to/switch/SwitchShaders
```

输出目录后可附加歌曲资源目录；工具会扫描 `.frag` 和 `.fsh`。两个移动平台应使用不同输出目录。运行时需要与对应平台匹配的着色器包；修改 GLSL 后须重新生成。预编译集合包含当前版本的音符实例化和代理辅助线着色器。

## 工程与待验证项

- iOS/iPad Simulator：`platform/mobile/ios` 现在包含目标工程、UIKit 入口及动画帧回调，支持 `--simulator` 构建。交接版本要求 Xcode 26.2；本机仍是 Xcode 16.4，未完成平台构建与运行。
- Android：`platform/mobile/android` 包含 SDL Activity、文件选择器及资源 ZIP 安装入口，CI 使用仓库 Secrets 签名。GitHub CI 已完成 APK 构建及签名验证，设备运行仍待验证。
- Switch/Horizon：`platform/switch` 包含原生输入/路径实现及构建脚本。CI 使用固定版本 NroForge 在 GitHub Ubuntu x64 runner 上构建，不再依赖手动提供交接包的 `dist/`。原交接脚本保留；NRO 构建与实机运行须分别验证。

六平台 CI 配置与依赖见 [CI_PLATFORMS.md](CI_PLATFORMS.md)，素材安装见 [EXTERNAL_ASSETS.md](EXTERNAL_ASSETS.md)。

## 素材与版本控制

外部素材放在本地 `Assets/`，沿用仓库忽略规则。不要使用 `git add -f Assets`。本次上传资源没有替换仓库已有的 `Assets/tips.json`；现有 App 图标保持原样。外部压缩包、生成的着色器和测试产物不应纳入源码提交。
