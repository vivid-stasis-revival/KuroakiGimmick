# Kuroaki/Gimmick → Switch / Horizon 实验移植

状态：**r5 a6 严格 Mono Full AOT 已通过实机谱面加载/播放，用户反馈平均约 25 FPS；音乐已补齐且实机出声，但随后 HUD 图片解码 OOM；正在准备 LLVM AOT。**

Full AOT 构建与最新实机记录见 [FULL-AOT.md](FULL-AOT.md)。

这是一份实验移植。第一版未能运行；r2 确认 Vulkan 内存分配失败，r3 修复后可播放谱面；r5 a6 已使用严格 Full AOT 实机播放。
现有桌面端保持默认构建；只有 `-p:KuroakiSwitch=true` 选择 Switch 分支。


## r4 音频与性能排查

用户确认 r3 的界面和谱面播放已工作，但卡顿、无声。用户提供的 nh/music.ogg（48 kHz/stereo/Vorbis/447 秒）
已由 Switch DLL 在主机解码成有效非静音 PCM。r4 修复 SDL 音频回调持锁等待、队列/启动与缓冲释放问题，
增加音频来源/设备/播放日志及每五秒的 input/update/draw/submit 耗时统计。
更新包 `dist/KuroakiGimmick-Switch-r4-update-20261011.zip`，含独立 `KG-Audio-Probe.nro`。
探针日志为 `audio-probe-r4.log`；通过 `bash platform/switch/build-probe.sh audio` 重建。
音频实际回调体的模拟测试在 ASan/UBSan 下通过，真实歌曲解码、回归及原生链接检查通过。
本版尚未实机验证；CPU 100% 的具体热点仍需 `[K/G perf]` 数据定位，不声称已解决性能。

## r3 原生内存压力修复

r2 实机确认 16 MiB Vulkan 分配失败，随后 1 MiB transfer buffer 也返回 VK_ERROR_OUT_OF_HOST_MEMORY。
r3 将 Mono 预留上限改为 1 GiB，其余给原生堆；Switch SDL 的分配页改为 4/8 MiB。
transfer buffer OOM 清理已完成 GPU 工作后只重试一次；终止失败帧后拒绝 Canvas 清理时的新批次。
增加 native malloc / Mono 使用统计，取消 Switch 的额外 shader-format 设备预探测。
更新包 `dist/KuroakiGimmick-Switch-r3-update-20261011.zip`，探针日志 `probe-r3.log`。
故障注入（含清理后重试及持续失败）、回归、shader 与静态链接检查通过。本版尚未实机验证，长谱面 Mono 峰值也待实测。

## r2 实机反馈与诊断更新

第一版主程序完成 Mono/素材/Vulkan 初始化，随后出现 transfer buffer 分配异常；清理提交可能掩盖更早的异常。
独立探针也黑屏退出且没有找到日志。r2 更新包为 `dist/KuroakiGimmick-Switch-r2-update-20261010.zip`（相对源码目录）。
r2 修复缓冲扩容的事务性和失败清理，SDL 记录分配大小、内存类型及 Vulkan 错误码；探针新增原生缓冲上传测试。
探针日志为 `/switch/KuroakiGimmick/probe-r2.log`。故障注入、既有回归、shader/ABI 及原生链接检查通过。
本版本未实机验证，不声称已修复启动。测试步骤见更新包中的 `README-r2-实机测试.txt`。

## 2026-10-10 本机原生构建补充

SD 测试包：`KuroakiGimmick-source/dist/KG-Switch-SD-20261010`（相对本包根目录）。
包含主 NRO、独立 Vulkan 探针、Mono .NET 9 BCL、用户完整 Assets、26 个 baked shader。
安装和实机测试步骤见产物中的 `README-实机测试.txt`，精确版本见 `BUILD-INFO.json`。
原有 CPU Regression 已全部通过；使用 devkitA64 对 28 个结构体大小及 165 个字段偏移的核对通过。
两个原生 ELF 无未解析符号、无动态库依赖；NRO/NACP/ASET 及 492 个原素材文件 hash 核对通过。
当前原生构建的修复以随包源码为准；根目录原始 `.patch` 保留为历史快照。

使用 `devkitpro/devkita64:20260219`（linux/amd64）、GCC 15.2.0 / libnx 4.12.0。
Mono SDK 使用 rel-3；Mesa 使用固定 commit 对应 Actions run 37623995855 的 unified SDK。
该 Mesa SDK 缺少 Rust 标准库，并多声明了没有实际符号引用的 libelf。
下载 `rust-std-nightly-aarch64-unknown-linux-gnu.tar.xz`（nightly 2026-10-07）并解压后，
在构建环境中执行下面命令补齐 SDK；脚本检查 Rust 编译器版本并审计 libelf 引用。

```bash
export PATH="$DEVKITPRO/devkitA64/bin:$PATH"
python3 platform/switch/tools/complete_mesa_sdk.py \
  --sdk "$DEVKITPRO/portlibs/switch" \
  --rust-libs /path/to/rust-std-aarch64-unknown-linux-gnu/lib/rustlib/aarch64-unknown-linux-gnu/lib
```

SDL 构建显式禁用了静态 try-compile 误判的 sysconf、sigaction、fdatasync、pthread_setname_np，
使用 SDL 内置 iconv 并关闭 process 后端。K/G 的本地 SDL pkg-config 目录通过 host `pkg-config`
读取，避免 devkitPro wrapper 清空 `PKG_CONFIG_LIBDIR`。绝对 native 源码/图标路径的 Makefile 问题已修复。
主 NRO 使用 K/G 图标及版本 0.1.4。打包脚本新增可选 `--probe` 参数。

## 方案

`hbmenu → K/G 专用 NRO → mono-nx .NET 9 interpreter → K/G DLL → 静态 SDL3 → SDL_GPU Vulkan → mesa-switch NVK → NWindow/VI`

保留现有 Canvas、SceneRenderer、谱面解析、时间轴、Viewer/Editor UI。
不把 C# 重写成 C++，也不把 SDL_GPU 换成另一个渲染器。

参考版本固定在 `versions.env`：

| 项目 | Commit | 用途 |
|---|---|---|
| KuroakiGimmick | `162f659eb6aaa262eda28587c13450b05661ac52` | 本补丁基线，17.7.1 main |
| exelix11/mono-nx | `285b277de16711344126cd90c2a21cfbd0d5d714` | .NET 9、静态 P/Invoke、NRO 主程序 |
| danfromtico/mesa-switch | `606441c90e459028a50fca352daebfc20211f890` | loaderless NVK Vulkan / VI WSI |
| SnepOMatic/SDL3-Switch | `b4c300c07924b4aea3930792cbc013d3d18374ea` | Switch 窗口、音频、键鼠、触摸基础移植 |

SDL3-Switch 分支名是 `switch-sdl-3.2.14`，但该 commit 的 CMake 项目版本为 **3.2.27**。
K/G 桌面依赖是 3.4.16；已对当前使用的结构布局和必需符号核对。
`SDL_GetGPUDeviceProperties` 在该 SDL 分支不存在，但 K/G 原有代码已捕获
`EntryPointNotFoundException`，这个可选诊断不影响设备创建。
`GPUMultisampleState.enable_alpha_to_coverage` 在旧版对应保留字节，K/G 留为零。

## 本次实现

- .NET 9 IL / Mono interpreter 构建，禁用桌面 RID、NativeAOT、single-file 和 trimming。
- 专用 native 主程序加载 `/switch/KuroakiGimmick/KuroakiGimmick.dll`。
- 自动生成并静态注册 108 个 SDL3 P/Invoke 符号及 `KGSwitch` 输入/路径接口。
- 为 SDL3-Switch 增加静态 Vulkan loader 和 `VK_NN_vi_surface`，把 `NWindow*` 交给 Mesa WSI。
- SDL 窗口允许没有 EGL surface，保留现有 OpenGL 路径；K/G 使用 Vulkan。
- 修复基础 Switch 触摸驱动的 down/up 和 finger 查找错误。
- 1280×720 初始窗口，使用既有 UiViewport 缩放；逻辑界面仍遵循 K/G 的最小布局。
- SD 上独立的 UserData、Exports、Temp、launch.args。
- 控制器转换成现有 SDL 键鼠事件，界面绘制光标；USB 键鼠和触摸使用 SDL 后端。
- 文件/保存对话框改为 Switch 软件键盘输入路径。
- 主机离线编译 SPIR-V，同时保存 uniform/sampler 布局；Switch 不调用 shaderc/SPIRV-Cross。
- 资源 SHA-256 使用纯托管实现，避免依赖 mono-nx 不支持的 OpenSSL；结果兼容桌面导出。
- 不依赖 Mono 或游戏资源的 Vulkan clear/present 探针。

## 构建条件

建议在 Linux / WSL 或 mono-nx 的开发容器里交叉构建；Switch 系统运行 Atmosphère + hbmenu。

1. .NET 9 SDK、Git、Python 3、CMake、Make。
2. devkitPro 的 devkitA64、libnx、Switch portlibs 和 pkg-config。
3. **匹配编译器版本**的 mono-nx SDK：把 `gather_sdk.sh` 生成的 SDK 解压到
   `dist/switch-deps/mono-nx/`，其中有 `dotnet_runtime/` 和 `icu/libnx/`。
   也可以按 mono-nx README 自行构建 runtime 和 ICU。其 SDK 不承诺跨工具链升级兼容。
4. mesa-switch 的 loaderless SDK 安装到 `$DEVKITPRO/portlibs/switch`，包含
   `libvulkan.a`、其静态依赖和 `vulkan.pc`；此处不是普通桌面 Vulkan loader。
   SDL3 基础移植仍编译 EGL/OpenGLES 辅助代码，因此使用 mesa-switch **unified SDK**。
5. mono-nx release 的 `sd_files` / .NET 9 BCL 和 ICU 数据。
6. 用户现有 K/G 的完整 `Assets`，以及可移动的谱面/资源包。

公开 K/G 仓库仅含应用图标、tips 和字体等可分发内容。
`Brand`、`Shaders`、`GameCommon`、`GameFX`、音符和游戏资源要从现有本地 K/G 配置提供。
本包不包含这些素材，也不自动下载。

## 构建顺序

在 K/G 根目录执行。若使用补丁，先在上表基线应用 `KG-Switch-MonoNX-Experimental.patch`。

```bash
bash platform/switch/prepare-deps.sh

# 在此安装上面的 mono-nx SDK 和 mesa-switch unified SDK。
export DEVKITPRO=/opt/devkitpro

# 先做 SDL/Vulkan 独立验证，不需要 Mono。
bash platform/switch/build-sdl.sh
bash platform/switch/build-probe.sh

# 再构建 K/G 专用 native runner 与托管程序。
bash platform/switch/build-native.sh
bash platform/switch/build-managed.sh
```

产物：

- `dist/switch-probe/KG-Vulkan-Probe.nro`
- `dist/switch-native/KuroakiGimmick.nro`
- `dist/switch-managed/KuroakiGimmick.dll` 及托管依赖

`KG_SWITCH_DEPS` 可指定另一处依赖目录；`KG_BUILD_JOBS` 控制 SDL 构建并行度。
prepare 脚本仅向依赖副本应用 patch，重复调用会检查已有 patch；遇到不匹配 commit 会停止。
没有 devkitA64/Mono SDK 时无法生成 NRO。随包的预编译托管 DLL **不能独立启动**。

## 离线 shader 编译

用桌面构建运行 baker；baker 不需要创建 SDL 窗口或 GPU，但需要主机 shaderc。

```bash
dotnet publish KuroakiGimmick.csproj -c Release \
  -p:KuroakiShaderBaker=true \
  -p:BaseIntermediateOutputPath="$PWD/dist/switch-baker-obj/" \
  -p:NuGetLockFilePath="$PWD/dist/switch-baker.lock.json" \
  -o dist/switch-baker

# 把完整 Assets 复制到 dist/switch-baker/Assets（保留应用自身资源）。
# 额外的歌曲/原生资源包目录也需要扫描。
dotnet dist/switch-baker/KuroakiGimmick.dll \
  --bake-switch-shaders dist/switch-shaders /path/to/song-resource-packs
```

baker 包含六个代码内 shader，并递归编译 Assets / 额外目录中的 `.frag` 与 `.fsh`。
生成 `.kgsp` 包含精确源码、阶段、SPIR-V、entry、uniform 偏移、sampler 顺序。
源码或 AdaptGml 规则变化必须重新 bake；缺少 cache 会给明确诊断，不执行主机 native 编译器。
任意 `#include` 或非自包含外部 shader 仍受现有 K/G shader 编译规则约束。

## SD 打包

```bash
python3 platform/switch/tools/package_sd.py \
  --managed dist/switch-managed \
  --nro dist/switch-native/KuroakiGimmick.nro \
  --assets /path/to/complete/Assets \
  --shaders dist/switch-shaders \
  --mono-sd /path/to/mono-nx/sd_files \
  --output dist/KG-Switch-SD
```

输出目录必须不存在，避免覆盖已有 SD 配置。将该目录的内容复制到 SD 根目录。
不会复制桌面 SDL/Shaderc native 库；不会改写通用 `/mono/config.ini`。
K/G 使用自己目录下的 `mono.ini`，共享 `/mono/` 的 BCL/ICU。

运行要通过 **hbmenu title takeover**，不要从相册的 applet 模式运行。
配置强制检查 full application；程序结束后沿用 mono-nx 的退出进程措施，避免重复启动崩溃。

`/switch/KuroakiGimmick/launch.args` **每行一个字面参数**，没有 shell 展开和引号解析。
默认离屏预览宽度 640。示例：

```text
/switch/KuroakiGimmick/Songs/demo
--render-width
640
--language
zh-CN
```

工程路径要用相对资源引用或 SD 上的实际路径；Mac/Windows 的绝对路径不会自动转换。

| 按键 | 行为 |
|---|---|
| 左摇杆 | 移动光标 |
| A | 鼠标左键，支持按住拖动 |
| X | 播放/暂停 |
| B | Esc，关闭当前 UI 层 |
| Y | 快捷键帮助 |
| Minus | F1 参考文档 |
| 方向键左右 | 现有帧步进快捷键 |
| Plus | 退出应用 |

复杂编辑建议接 USB 键鼠。OPEN/SAVE 的路径输入由软件键盘完成。

## 初始移植验证记录（原生构建前，2026-10-10）

完成的本地验证：

- Switch IL 构建：0 warning / 0 error。
- 主机 baker 构建：0 warning / 0 error。
- 6 个内置 shader 实际调用主机 shaderc 编译成 SPIR-V，缓存读取通过。
- 截断 shader pack、缺失 shader pack 拒绝检查通过。
- C 编译器核对 28 个结构体大小、165 个字段偏移；64 位主机 ABI。
- 108 个 SDL3 静态导出表对真实 SDL 头文件通过 C 语法检查。
- 纯托管 SHA-256 与 .NET BCL 对照：空数据、block/padding 边界、1 MB 数据、stream、37 字节分块。
- Shell/Python 语法、patch 正反向检查通过。
- 既有 Regression 启动后通过部分导出检查，后续因公开仓库没有内置对象/完整 Assets 中止；不能记为整套通过。

**初始记录尚未完成（原生构建已在上文补充）：** devkitA64 原生编译/链接、NRO 启动、Horizon Mono 执行、实际 NVK shader/presentation、
音频、触摸、软件键盘、dock/undock、长谱面内存与性能、完整资源包 bake。
主机 ABI 核对不等于 ARM64/Horizon 执行验证。

先将 probe NRO 放入 `/switch/KuroakiGimmick/` 启动。
正常应出现青蓝色的渐变清屏，Plus 可退出；日志在同目录 `probe.log`。
探针通过只证明 SDL_GPU + Mesa VI surface/呈现链，不能证明 Mono 或 K/G shader 可用。
再启动 K/G，日志：`UserData/mono.log`、`UserData/viewer.log`。

## 当前边界

- 预览音频目前仅 Ogg/Vorbis；MP3/FLAC/WAV 等 FFmpeg 子进程路径关闭。
- MP4 导出关闭；截图、工程/谱面导出仍保留，但需要实机检查文件系统行为。
- Switch 单原生窗口，LIVE WINDOWS 关闭；虚拟桌面/Proxy 预览继续保留。
- 手柄只提供基础键鼠映射；不声称所有 Editor 手势和快捷键都可用。
- CPU-intensive parsing、字体栅格化、音频解码仍走 interpreter；性能没有实测。
- Mesa NVK 与 SDL_GPU 的 required feature/device limits 是否满足，最终由探针和实机 SDL 日志确认。
- mono-nx 自身是实验项目；BCL/文件系统/线程等个别行为仍可能出现兼容性问题。

## 许可与来源

K/G 沿用仓库原有许可。新增 native bridge 标注 MIT；现有 K/G 修改沿用原项目的适用条件；
SDL3-Switch 补丁标明为 altered source，依 SDL zlib 许可，原有文件声明保留。
mono-nx 和 Mesa 沿用其仓库许可。这里没有向 SDL 上游提交任何代码或 PR。

- https://github.com/exelix11/mono-nx
- https://github.com/danfromtico/mesa-switch
- https://github.com/SnepOMatic/SDL3-Switch
- https://github.com/vivid-stasis-revival/KuroakiGimmick
