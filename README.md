# KuroakiGimmick

<img src="https://raw.githubusercontent.com/vivid-stasis-revival/KuroakiGimmick/main/Assets/App/Kuroaki.png" width="200" alt="KuroakiGimmick Logo">

**为《vivid/stasis》制作的谱面演出预览与编辑工具。**

在同一条时间轴上编排 gimmick、图片、字幕、场景效果和窗口运动，并在歌曲场景中检查实际表现。

![Version](https://img.shields.io/badge/version-0.1.5_%2F_18.3-94cddd?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-8-b5a3e8?style=flat-square)
![Renderer](https://img.shields.io/badge/renderer-SDL3_GPU-94cddd?style=flat-square)
![Engine](https://img.shields.io/badge/engine-C%23_%2B_Rust-b5a3e8?style=flat-square)
![Language](https://img.shields.io/badge/UI-中文_%2F_English-e8a4bd?style=flat-square)

[下载正式版](https://github.com/vivid-stasis-revival/KuroakiGimmick/releases/latest) · [全部版本与 Nightly](https://github.com/vivid-stasis-revival/KuroakiGimmick/releases) · [更新日志](CHANGELOG.md) · [问题反馈](https://github.com/vivid-stasis-revival/KuroakiGimmick/issues)

---

> **重要：** 本仓库只提供 KuroakiGimmick 的源码、应用资源及允许公开分发的第三方组件。
>
> 《vivid/stasis》的游戏素材不会随仓库或发行包分发。完整场景预览及部分功能需要自行准备合法取得、与程序版本兼容的本地游戏资源。
>
> 下载到程序不代表已经得到了运行所需的全部素材。这两件事确实很容易被混为一谈。

## 项目定位

**KuroakiGimmick** 主要解决的是《vivid/stasis》的**谱面演出制作、预览与验证**。

它将音符、HUD、VSM gimmick、图片、字幕、Custom Proxy 和窗口事件放进同一套时间轴与歌曲场景中。

与其每改一个参数就进游戏重新检查，不如先在编辑器里定位、预览、调整，再把结果导出去。

项目并不是完整的音符制谱器，也不打算重新实现整个 GameMaker Runtime。对于无法完整模拟的原生行为，程序会尽量提供兼容实现及诊断信息；真正的结果仍然以游戏本体为准。

这一点很重要。

**能把场景渲染出来，不代表每一种 gimmick 都已经正确还原。**

即使同一个 Shader 在两边看起来完全一致，也不代表对象生命周期、事件执行顺序、内部状态和游戏回调都已经完整复现。

我们会继续尝试修正这些差异。

但在那之前，**游戏本体说了算。**

## 功能

| 功能 | 支持内容 |
| --- | --- |
| **谱面与演出预览** | 读取 VSB、VSC、VSM、VSP、SGV 及相关图片、音频和字幕；在 GAME SCENE 中预览 Note、Lane、HUD、判定与演出。 |
| **时间轴编辑** | 编辑事件时间、持续时间、From / To、Easing 和 Proxy；支持吸附、循环试听、时间标记、批量操作、Loop 拆分／合并、轨道排序、撤销与重做。 |
| **图片动画** | IMAGE CANVAS 编辑初始姿态、位置、缩放、旋转、不透明度、关键帧、运动路径与资源替换。 |
| **文字与字幕** | TEXT CANVAS 编辑多行文字、排版、RGB 颜色、对齐、换行宽度及 MOVE / FADE 动画；支持内容时间轴可视化。 |
| **直接场景编辑** | 在 GAME SCENE 双击图片或文本对象并拖动修改 X/Y；提供 Proxy 网格、选择与独立预览。 |
| **原生 gimmick** | 已适配的原生对象、特殊场景、Room FX、Shader、粒子及 Custom / ExtCustom gimmick。 |
| **配置驱动对象系统** | v2 gimmick 定义、可复用行为组件、信号路由、生命周期控制、函数程序和合成管线。 |
| **窗口演出** | 虚拟桌面、真实辅助窗口、部分 ExtCustomGimmick 窗口事件、Proxy 内容绑定与多窗口预览。 |
| **Custom Episode** | 将 `custom_episode` 剧情数据展开到时间轴，配合播放、定位、截图和视频输出进行检查。 |
| **触屏操作** | 单指编辑、双指平移／缩放、可取消的触控操作及防误触处理。 |
| **工程与导出** | 保存 SGV；导出 VSM、VSM + cgmk config、VSP + assets、Chart Folder、Info Card 或 MP4。 |
| **工作区与工具** | 中文／English、界面设置、F1 离线参数文档、轨道说明、跨实例复制，以及命令行检查与渲染。 |
| **Optimization Engine** | Rust 负责部分解析、BPM、音符运动、音频解码及 Timeline 批量计算；保留 C# 兼容路径和诊断开关。 |

KuroakiGimmick 的重点是**演出**，而不只是把 VSM 文本换一种方式显示出来。

预览、截图和视频输出尽可能共用主要场景渲染流程，并对反向定位、复杂回调和特定原生行为提供专门的兼容实现。

当然，这也不意味着游戏运行时的所有状态都能被完整复制。

尤其是那些需要真正创建操作系统窗口，或者依赖原版游戏内部对象的东西。

有些行为确实没有那么容易模拟。

## 下载与平台支持

当前版本为 **v0.1.5 / Build 18.3**，构建序号为 `1830`。

[前往 GitHub Releases](https://github.com/vivid-stasis-revival/KuroakiGimmick/releases)

### 平台支持

| 平台 | 图形后端 | 当前状态 |
| --- | --- | --- |
| **Windows x64** | Direct3D 12 / SDL3 GPU | 桌面版本，需要可用的 D3D12 设备 |
| **macOS arm64** | Metal / SDL3 GPU | 桌面版本，已完成实际 Metal GPU 回归验证 |
| **Linux x64** | Vulkan / SDL3 GPU | 已完成 CI 构建，实际设备与驱动兼容性仍需验证 |
| **Android arm64** | 移动端图形后端 | 实验性支持，提供已签名 APK |
| **iOS / iPadOS arm64** | 移动端图形后端 | 实验性支持，提供未签名 IPA |
| **Nintendo Switch** | Horizon 平台适配 | 实验性支持，提供 NRO |

六个平台均已纳入 GitHub Actions 构建与发布流程。

但这里需要明确区分：

**能编译、能打包，以及能在真实设备上正常运行，是三件不同的事。**

Windows 和 macOS 已有桌面端运行与回归验证。Linux 已纳入构建流程，但实际 Vulkan 驱动组合仍需验证。

Android、iOS/iPadOS 和 Nintendo Switch 的适配还处于实验阶段。相关平台的实际设备运行、图形兼容性和部分输入行为仍需要更多测试。

尤其是触屏。

合成 SDL 触控事件通过了，不代表随便找一台 iPad 就能保证所有手势都正常工作。

编译器对此没有发言权。

### Custom Gimmicks 1.14 自定义 MPF

支持读取歌曲目录下的 `cmpf/GLOBAL.<脚本名>.vsmpf` 和 `<难度>.<脚本名>.vsmpf`；同名时难度版本优先。VSM 末尾的 `mpf` 区间会执行脚本的 `::Init::`、`::Main::`、`::Finish::`，支持变量、数组、条件、循环，以及直接/定时 mod 写入和补间。保存工程会保留脚本源目录与难度；Chart Folder 导出包含引用的脚本，单独导出 VSM 不包含脚本文件。

脚本按固定 **60 Hz 预览时钟**预计算，拖动、倒放查看和并行导出复用同一结果。原游戏帧率可能不同，因此依赖“每帧加一”的脚本可能有差异。随机数使用脚本共享的确定性预览序列，不保证与 GameMaker 一致。判定/连击查询返回零值并提示离线基线；修改游戏判定、计分，以及依赖原游戏实例的 `call_callback_mod` 调用尚不模拟，会明确报告错误。脚本的解析深度、循环、累计内存和输出量有预览上限；不会执行任意文件操作或宿主代码。

时间轴左侧的 **源码 → MPF** 可切换并直接编辑实际脚本；使用 **保存 MPF 并预览** 或 **Ctrl/Cmd+S** 保存当前文件并重建预览。输入时保留草稿，Ctrl/Cmd+Z/Y 撤销/重做草稿。保存前校验语法和外部文件冲突，旧版本备份到应用数据目录。VSM 已引用但缺少文件的脚本会提供新建草稿，只有显式保存才创建文件。MPF 文件保存独立于 VSM/VSP 的“应用源码”。

兼容检查：`dotnet run -c Release -- --mpf-test`。测试使用合成脚本，不携带私有游戏代码。

### 正式版与 Nightly

正式版本以对应 Release 的构建提交为准。

Nightly 用于提供最近的功能、兼容性改进和 Bug 修复。

18.3 接入 Custom Gimmicks 1.14 自定义 MPF，修正脚本时序与补间竞争，保留保存/移动/导出时的脚本关联，并新增 MPF 源码直接编辑、保存和预览更新。

18.2.2 新增 Proxy 右键删除、默认开启且可在 Editor 设置关闭的重叠片段子行、复制当前行和剪切（Ctrl/Cmd+X）；自动备份与历史归档移至应用数据目录，并支持旧历史校验迁移。

18.2.1 汇总了 18.2 之后的修复和编辑功能，包括 fish/glow 渲染、共享图片资源、即时流速与时间轴偏移、GMK 分组和 VSM/VSP 源码编辑、桌面谱面大小设置，以及多线程视频导出。

发布产物在六个平台构建全部通过后由 GitHub Actions 自动上传；构建状态与下载文件以 Releases 页面为准。

相关版本：

- [v0.1.5 / 18.3](https://github.com/vivid-stasis-revival/KuroakiGimmick/releases/tag/v0.1.5-18.3)
- [v0.1.5 / 18.2.1](https://github.com/vivid-stasis-revival/KuroakiGimmick/releases/tag/v0.1.5-18.2.1)
- [全部版本与 Nightly](https://github.com/vivid-stasis-revival/KuroakiGimmick/releases)

程序版本、构建号和提交 SHA 应分别检查。

因为版本号一样，不代表二进制内容也一样。

## 快速开始

### 环境要求

| 项目 | 要求 |
| --- | --- |
| .NET | 桌面源码构建使用 .NET 8 SDK |
| Rust | stable 工具链，用于 Optimization Engine 和正式发行构建 |
| Windows | Direct3D 12 / SDL3 GPU |
| macOS | Metal / SDL3 GPU |
| Linux | Vulkan / SDL3 GPU |
| 游戏素材 | 用户自行准备本地 `Assets/`；仓库内 `Assets/App/` 仅包含应用自身资源 |
| 音频 | Symphonia 及兼容的 NVorbis 音频路径 |
| 视频导出 | FFmpeg，可通过 `PATH` 或 `KUROAKI_FFMPEG` 指定 |

首次恢复 NuGet 包需要网络连接。

**编译通过不等于运行通过。**

这不是文字游戏，尤其在 GPU 驱动开始闹脾气的时候。

### 安装桌面发行包

1. 从 [Releases](https://github.com/vivid-stasis-revival/KuroakiGimmick/releases) 下载对应系统与架构的发行包。
2. 将 ZIP 完整解压，不要只取出可执行文件。
3. 将自行准备的完整 `Assets/` 目录放在程序可执行文件旁。
4. 启动 KuroakiGimmick，打开谱面文件或已有 SGV 工程。

NativeAOT 发行包包含可执行程序、SDL3、Shader 相关原生依赖与对应架构的 Rust Optimization Engine。

这些文件属于同一份发行包。

如果把其中一个可执行文件单独复制出去，然后发现程序启动不了，那不一定是程序决定罢工了。

它可能只是找不到自己的依赖。

### 移动端与 Switch 资源安装

移动端与 Switch 使用独立安装的游戏资源包。

普通桌面 `Assets.zip` 不能保证直接在其它平台使用，因为它们还需要针对对应图形后端预编译的 Shader。

**Android**

首次启动时通过系统文件选择器导入为 Android 准备的资源 ZIP。

程序会验证资源并安装到应用私有的 `Documents/Assets` 目录。替换资源时，原有资源会保留为备份。

**iOS / iPadOS**

发行包中的 IPA 未签名，需要用户自行完成签名和安装。

安装后，通过系统“文件”应用将 iOS 资源包的 `Assets` 文件夹复制到：

`我的 iPhone / iPad → Kuroaki/Gimmick`

返回程序后重新检查资源。

**Nintendo Switch**

将资源包安装到：

`sdmc:/switch/KuroakiGimmick/Assets`

同时将发行包附带的 `mono` 目录复制到 SD 卡根目录，并使用完整应用模式运行 NRO。

Switch 资源包需要包含对应的 `SwitchShaders`。

### 生成平台资源包

仓库提供脚本，用于从用户已有的本地资源目录生成不同平台需要的资源包。

```bash
python3 scripts/prepare_resource_pack.py android \
  --assets /private/Assets \
  --output /private/Kuroaki-Android-Assets.zip

python3 scripts/prepare_resource_pack.py ios \
  --assets /private/Assets \
  --output /private/Kuroaki-iOS-Assets.zip

python3 scripts/prepare_resource_pack.py switch \
  --assets /private/Assets \
  --output /private/Kuroaki-Switch-Assets.zip
```

资源准备需要相应的桌面 Shader 编译依赖及 .NET 9 SDK／运行时。

如果自制歌曲使用额外 Shader，可以通过 `--shader-root` 指定歌曲资源目录。

Android 与 iOS 使用不同的 Shader 输出格式，不能随意混用。Shader 源码变化后也需要重新生成对应资源。

这些资源包只在本地生成，不会由脚本自动上传。

详细说明见 [EXTERNAL_ASSETS.md](docs/EXTERNAL_ASSETS.md)。

### 从源码编译

在仓库根目录执行：

macOS / Linux：

```bash
dotnet restore KuroakiGimmick.csproj --locked-mode
bash scripts/build-rustbridge.sh
```

Windows PowerShell：

```powershell
dotnet restore KuroakiGimmick.csproj --locked-mode
pwsh -File scripts/build-rustbridge.ps1
```

构建脚本会编译托管程序和 Rust 原生库，并执行相关不需要 SDL 窗口的自测。

如果只需要 C# 兼容路径，也可以单独构建托管程序：

```bash
dotnet build KuroakiGimmick.csproj -c Release
```

没有匹配的 Rust 原生库时，默认自动模式会记录原因并尝试回退到兼容路径。

要求必须启用 Rust 的模式则不会悄悄回退。

### 启动

```bash
dotnet run --project KuroakiGimmick.csproj -c Release --no-build
```

打开指定 SGV 工程：

```bash
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- \
  --editor /path/to/project.sgv.json
```

macOS：

```bash
bash scripts/run-editor.command /path/to/project.sgv.json
```

Windows PowerShell：

```powershell
.\scripts\run-editor.ps1 -Project 'C:\Charts\project.sgv.json'
```

界面语言可在 SETTINGS / 设置 → Language 中选择跟随系统、中文或 English。

语言设置只影响应用界面，不会改写工程中的字幕内容、谱面标识符或原始数据。

## 编辑与导出

### 基本工作流

1. **打开谱面或工程**：通过 OPEN CHART / VSM 导入 VSB、VSC、VSM、SGV 或歌曲目录，也可以拖入对应文件。需要额外素材时使用 `+ FILES / RESOURCES`。
2. **编辑演出**：按 `Tab` 在 Viewer / Editor 之间切换；在时间轴中修改事件，并使用 IMAGES、TEXT、SCENE 检查图片、字幕和实际歌曲场景。
3. **保存工程**：使用 SAVE / SAVE AS 保存 `.sgv.json`。SGV 可以引用外部资源，移动工程时请一并保留对应资源及相对路径。
4. **检查并导出**：先用 REVIEW FILES 查看输出文件与警告，再执行 EXPORT COPY。默认不覆盖既有导出文件。

### 时间轴

时间轴支持编辑事件的起始时间、持续时间、From / To、Easing 与 Proxy。

提供吸附、复制、粘贴、批量操作、标记及循环试听等功能。

图片与文字轨道支持分组显示，展开后可以检查相应的原始通道。

轨道显示顺序可以通过左侧名称或拖动手柄调整，也支持靠近边缘时自动滚动。

一次完整拖动对应一次 Undo。

**显示顺序不会影响实际 VSM 事件执行顺序。**

也就是说，把一个轨道拖到最上面并不会让里面的 gimmick 获得神秘的执行优先权。

### 图片编辑

IMAGE CANVAS 支持：

- 编辑初始位置、缩放、旋转和不透明度。
- 在当前时间点创建或修改关键帧。
- 设置动画起点与终点。
- 编辑运动路径和连续动画。
- 替换图片资源。
- 检查事件对最终场景的影响。

`INITIAL` 用于编辑初始状态，`KEY HERE` 用于在当前拍写入关键帧。

`START / END` 与 `CONTINUE` 用于调整动画端点及路径。

在 GAME SCENE 中，也可以直接双击并拖动图片对象修改 X/Y。

### 文字与字幕

TEXT CANVAS 支持多行文字、RGB 颜色、对齐方式、行距、换行宽度及 MOVE / FADE 动画。

文字内容和视觉动画分别编辑。

在包含最近字幕改进的 Nightly 中：

- 文字轨道显示字幕内容及对应的生效区间。
- 可以点击内容区间直接定位与选中字幕。
- 内容区间和动画片段分别占据时间轴轨道的不同区域。
- Inline 输入支持使用 `{n}` 表示实际换行。
- 粘贴多行内容时保留换行语义，而不是直接把换行全部改成空格。

这样可以更清楚地判断字幕究竟在什么时候切换内容。

毕竟一个文本轨道如果只能看到 MOVE 和 FADE，却看不出文字是什么时候变的，编辑起来确实有点反人类。

### GAME SCENE 直接编辑

在 GAME SCENE 中可以双击并拖动图片或文本对象，修改其当前时间点的 X/Y。

拖动结束后作为一次编辑记录提交，支持撤销。

编辑手柄不受场景后处理效果影响。

但对于动态求值、冲突状态或不安全的变换，仍然需要通过原始事件或 Inspector 进行调整。

编辑器不会为了方便拖动，就假装所有动态值都可以随便改写。

### Proxy 辅助预览

选中 P 编号或对应 `pr* / sh*` 事件后，编辑器可以显示 Proxy 的逻辑网格和轨道参考区域。

支持：

- 显示逻辑取景框。
- 显示包含背景及 UI 的完整取景区域。
- 查看裁剪、遮盖与边界。
- 通过双击选择目标 Proxy。
- 使用 `Shift + 双击` 穿过图片／文本层选择 Proxy。
- 在多个 Proxy 之间切换。
- 暂时隔离一个 Proxy，检查其实际合成画面。

隐藏的 Proxy 不参与常规可见命中。

「只看指定 Proxy」是编辑器的辅助显示状态，不会修改谱面数据、真实窗口输出或视频导出。

### 触屏操作

18.2 引入 SDL3 原生触摸协调逻辑。

单指可以进行鼠标式编辑，双指可以进行时间轴与画布的平移、缩放。

为了避免误操作，触控编辑加入了取消机制。

例如，在单指双击时间轴准备插入事件的过程中，如果第二根手指进入，尚未提交的操作会被取消。

移动、焦点丢失、SDL 取消事件，以及工程发生变化时，也会阻止过期触控操作被错误提交。

触屏双击延迟到抬指后提交，普通物理鼠标双击则仍保持原有的即时操作。

对应测试覆盖了不同抬指顺序、触控取消、文档变化、撤销历史和鼠标事件交错等情况。

不过这些测试使用合成 SDL 事件。

真实设备上的手势兼容性还需要继续验证。

### Custom Episode

支持将 `custom_episode` 的剧情数据展开到时间轴中进行预览。

部分原生剧情演出还提供专门的 CG、字幕、粒子及状态求值实现。

播放、反向定位、截图与视频导出尽可能使用一致的场景求值方式。

需要特殊游戏状态、交互或尚未实现的剧情步骤，会提供相应诊断信息。

并非所有原版剧情交互都会由编辑器自动执行。

### 导出格式

| 模式 | 输出 |
| --- | --- |
| **VSM** | 当前演出文本 |
| **VSM + cgmk config** | VSM 及其窗口配置 |
| **VSP + assets** | VSP 与实际引用的图片资源 |
| **Chart Folder** | 当前难度的谱面演出、VSP、图片、字幕等资源及可重新打开的预览工程 |
| **Info Card** | 当前难度的 16:9 乐曲信息卡片 |
| **Video** | H.264 / AAC MP4，录制歌曲场景 |

**VSP + assets** 只收集 VSP 真正引用的图片，尽可能保留原始文件名并改写为可移动的相对路径。

它不会把 VSM、音频和整个歌曲目录一起塞进去。

**Chart Folder** 只收集当前难度真正需要的文件，不复制其它难度、未引用素材或 `.kuroaki` 备份。

自动备份和历史归档保存在应用数据目录的 `KuroakiGimmick/ProjectHistory/` 下，按工程绝对路径隔离；进入编辑或保存旧工程时，会复制并校验该工程的旧备份，成功后移除谱面目录内对应的备份。迁移失败会保留原数据，之后可重试。

`.kuroaki/projects/` 是工程引用的工作文件，仍随工程保留；`.kuroaki/tmp/` 用于保存事务恢复，不会整体搬走。分享谱面时，建议通过 **EXPORT → Chart Folder** 导出到全新目录，再压缩导出结果。覆盖导出到已有目录不会清理其中不相关的文件。

**Video** 输出普通歌曲场景，不是操作系统屏幕录像。

桌面端可在“设置 → 导出 → 渲染线程”选择 1 到 CPU 逻辑核心数减 2 个线程（上限最低为 1），默认 1；命令行可用 `--render-workers 2` 覆盖设置。各线程独立渲染，帧按顺序送入 FFmpeg；未保存的编辑也会包含在导出中。更多线程会增加显存占用，实际加速取决于 GPU、场景与编码瓶颈，带历史反馈的效果还需重放前序状态。

真实窗口、虚拟桌面和多窗口演出的完整行为仍需要在目标系统中检查。

移动端和 Switch 适配不提供依赖桌面 FFmpeg 进程的视频导出。

设置中的 Overwrite exports / 允许导出覆盖文件默认为关闭。

开启后同名文件可以被替换；Chart Folder 按文件合并，不会因此删除其它未导出的文件。

命令行可使用 `--overwrite` 单次允许覆盖。

### 常用快捷键

| 快捷键 | 操作 |
| --- | --- |
| `Tab` | Viewer / Editor 切换 |
| `Space` | 播放 / 暂停 |
| `Ctrl/⌘ + O` | 打开 |
| `Ctrl/⌘ + S` | 保存 |
| `Ctrl/⌘ + Shift + S` | 另存为 |
| `Ctrl/⌘ + Z` | 撤销 |
| `Ctrl/⌘ + Shift + Z` | 重做 |
| `Ctrl/⌘ + C / V` | 复制／粘贴选择集 |
| `E` / `Shift + E` | 创建或激活时间标记／取消固定标记 |
| `A` / `B` / `L` | 设置循环起点／终点／开关循环 |
| `F1` | 打开离线参数文档 |
| 长按 `W` | 查看轨道详细说明 |
| `Esc` | 取消当前操作／关闭对应预览 |

## 原生 gimmick 与配置驱动对象系统

原生 gimmick 的实现并不只是将 VSM 中的事件名映射到一个固定效果。

部分原版场景还涉及独立对象、特殊渲染顺序、生命周期、粒子状态、Shader 管线、剧情状态以及游戏内部回调。

为了尽可能还原这些行为，KuroakiGimmick 提供了专门的原生场景兼容实现。

从 18.1 系列开始，原生对象系统进行了进一步重构。

### v2 对象定义

新的配置驱动系统支持通过定义文件组合已实现的行为：

- **Behavior Components**：复用不同的原生行为组件。
- **Signal Routes**：将 VSM 事件映射到对象信号。
- **Lifecycle Rules**：控制对象及效果的创建、关闭和持续状态。
- **Function Programs**：通过配置描述受支持的函数操作。
- **Context Patches**：为对象行为提供上下文配置。
- **Composite Passes**：组合有序的 Shader 和合成流程。

这使得部分原生行为不再必须依赖写死的对象名称和专用 C# 分支。

当然，这不是一台可以用 JSON 编写任意 GameMaker 游戏的虚拟机。

配置只能使用引擎已经实现并验证过的组件、参数与行为。

### 配置示例

仓库提供：

[examples/generic-gimmick](examples/generic-gimmick/README.md)

该示例包含一个使用灰度 Shader 的配置驱动 gimmick。

通过 `gimmick-object.json` 描述对象、合成效果和信号，再由 VSM 事件控制效果的启用与关闭。

可以直接打开示例 SGV 工程检查运行结果。

这个示例不要求为自定义对象名称额外注册一份 C# 实现。

### 兼容性

v2 定义保留对旧版对象定义的兼容支持。

相关迁移已针对现有原生配置执行回归验证。

但旧程序不一定能够识别新版素材包。

尤其是 18.1.x 引入的 v2 定义，不能直接交给缺少对应对象系统的早期构建加载。

将定义里的 `"version": 2` 改成 `"version": 1` 并不会神奇地完成迁移。

如果这样就能解决兼容性问题，那整个软件工程行业大概会轻松不少。

## Optimization Engine

从 v0.1.5 / Build 18.0 起，KuroakiGimmick 使用 Rust 承担部分性能敏感的计算。

当前 Optimization Engine 的原生 crate 版本为 `0.3.0-alpha.0`。

主要分工如下：

**Rust**

- VSC / VSB / VSM 解析。
- BPM 数据构建。
- Note Motion 计算。
- Symphonia 音频解码。
- Timeline 基础轨道批量求值。
- 部分缓存和数据转换逻辑。

**C#**

- 编辑器与工程状态。
- 事件语义和特殊回调。
- 逐帧绑定与兼容逻辑。
- SDL 输入和音频播放。
- GPU 渲染与场景合成。
- UI、导出与平台集成。

两条路径提供相应的兼容回退及一致性验证机制。

Rust 负责了一部分计算，不代表所有逻辑都已经转移到 Rust。

### 引擎设置

可以在 SETTINGS → Performance 中调整 Optimization Engine、音频路径、并行加载及缓存相关选项。

重新打开谱面后，部分设置才会生效。

环境变量的优先级高于应用设置。

| 变量 | 用途 |
| --- | --- |
| `KG_RUST_ENGINE=auto` | 自动选择 Rust 优化路径，必要时回退 |
| `KG_RUST_ENGINE=off` | 禁用相应 Rust 路径 |
| `KG_RUST_ENGINE=verify` | 对比 Rust 与 C# 的结果 |
| `KG_RUST_ENGINE=required` | 要求 Rust 路径必须可用 |
| `KG_RUST_BRIDGE` | 单独控制音符运动计算 |
| `KG_AUDIO_ENGINE=optimization` | 使用 Optimization 音频路径 |
| `KG_AUDIO_ENGINE=nvorbis` | 使用 NVorbis 兼容路径 |
| `KG_RUST_AUDIO_REQUIRED=1` | Rust 音频不可用时直接失败 |

`verify` 是一致性检查模式，不适合直接当作性能基准。

### 性能优化

近期的性能优化包括：

- 大谱面 Note 和 VSM 时间轴查询索引。
- 避免重复扫描整首谱面的缓存机制。
- Note 与部分粒子的 GPU Instancing。
- 图像和 NoteSkin 的 Atlas／实例化渲染。
- GPU 上传缓冲复用与帧背压。
- 独立音频供给线程。
- 视频导出的异步 GPU 读取及后台编码。
- Rust/C# 计算路径的批量数据处理。

这些优化主要针对高密度谱面、复杂演出及大规模事件造成的 CPU、内存和 GPU 压力。

但性能问题并没有因此全部消失。

大量粒子、极端 Note 密度、GPU 驱动等待和复杂场景后处理仍然可能导致明显卡顿。

尤其是某些压力测试谱面。

它们看起来已经不太像是为了让人类正常游玩而设计的了。

具体测试环境、实测结果和未解决的问题见 [PERFORMANCE.md](PERFORMANCE.md)。

## 命令行与诊断

程序提供用于谱面检查、截图、信息卡片、视频渲染以及回归测试的命令行入口。

### 查看帮助

```bash
dotnet run -c Release --no-build -- --help
```

### 谱面检查

```bash
dotnet run -c Release --no-build -- \
  --inspect /path/chart.vsb --time 45
```

### 乐曲信息卡片

```bash
dotnet run -c Release --no-build -- \
  --card /path/FINALE.vsb \
  --out card.png --width 1920
```

卡片宽度可选 `1280`、`1920`、`2560`、`3840`。

### 截图

```bash
dotnet run -c Release --no-build -- \
  --snapshot /path/project.sgv.json \
  --scene --time 45 \
  --out frame.ppm \
  --report frame.json --strict
```

### 视频渲染

```bash
dotnet run -c Release --no-build -- \
  --render /path/project.sgv.json \
  --start 30 --end 45 \
  --fps 60 --width 1920 \
  --out clip.mp4 --strict
```

`--strict` 会将对应渲染错误作为命令失败返回。

截图与视频需要可用的 GPU 后端。`--inspect` 不需要完整的交互式图形窗口，但也不能代替图形兼容性验证。

语言可通过 `--language zh-CN`、`--language en` 或 `--language auto` 指定。

实际支持参数以对应构建的 `--help` 为准。

### 自测

仓库还提供多项不依赖普通交互窗口的诊断入口，包括：

- `--rust-bridge-test`
- `--rust-engine-test`
- `--rust-timeline-test`
- `--rust-audio-test`
- `--gimmick-definition-test`
- `--reference-layout-test`
- `--proxy-pick-test`
- `--smoke-touch-ui`

它们分别用于相关计算、原生对象定义、文档布局、Proxy 命中与触屏输入等回归检查。

部分 SDL 或 GPU 测试仍然依赖对应的平台图形环境。

测试通过意味着被覆盖的行为符合预期，不代表所有可能的谱面和硬件组合都已经得到验证。

## 构建与发布

当前应用版本为 **v0.1.5 / Build 18.3**。

最终版本号以 [KuroakiGimmick.csproj](KuroakiGimmick.csproj) 为准。

### 桌面 NativeAOT

KuroakiGimmick 支持桌面 NativeAOT 发行。

macOS：

```bash
bash scripts/publish-aot-mac.sh arm64
```

Windows：

```bash
bash scripts/publish-aot-win.sh x64
```

Windows NativeAOT 还需要对应架构的 Visual Studio C++ 构建工具链。

发行脚本负责为目标平台准备相应的托管程序和 Rust 原生库。

直接执行：

```bash
dotnet publish -c Release -r <rid>
```

在 Release + RID 条件下默认选择 NativeAOT，但不会自动构建和放置匹配架构的 Rust 库。

因此正式发行仍应使用仓库提供的发布脚本。

需要托管单文件模式时，可显式选择非 AOT 构建。

NativeAOT 发行包也并不是只有一个可执行文件。SDL3、Shader 相关库以及 Optimization Engine 等原生依赖仍需要随包提供。

### 六平台 CI

GitHub Actions 当前构建：

- Windows x64 NativeAOT
- macOS arm64 NativeAOT
- Linux x64 NativeAOT
- Android arm64 APK
- iOS arm64 IPA
- Nintendo Switch NRO

构建流程包含相应的依赖准备、资源排除、原生库检查及自动化测试。

Android 使用 CI 中配置的签名材料生成已签名 APK。

iOS 构建生成未签名 IPA，需要用户自行完成安装签名。

Switch 使用固定版本的 [NroForge](https://github.com/BennettNotFound/NroForge) 工具链构建 NRO，不需要为 GitHub CI 额外提供专用的自托管 Switch 构建机器。

移动端工程使用 .NET 10 及对应的 Android/iOS 工具链；Switch 采用独立的 .NET 9 目标和平台构建流程。

它们与普通桌面 .NET 8 发行流程并不完全相同。

CI 只负责公开代码和允许分发的资源，不会自动下载、嵌入或上传原版游戏的私有素材。

默认分支需要六平台构建全部成功后，才会进入相应的 Release 发布流程。

相关说明：

- [六平台构建与发布](docs/CI_PLATFORMS.md)
- [移动端与 Switch 适配](docs/PLATFORM_ADAPTATION.md)
- [外部资源安装](docs/EXTERNAL_ASSETS.md)

## 开发与文档

### 项目结构

```text
KuroakiGimmick/
├── .github/workflows/         CI 与自动发布
├── src/
│   ├── App/                   程序入口与应用状态
│   ├── Bridge/                C# / Rust 桥接
│   ├── Core/                  数据、解析、Timeline 与编辑
│   ├── Graphics/              场景、特效、文本与 SDL3 GPU
│   ├── Native/                平台原生接口
│   └── UI/                    Viewer、Editor、菜单、本地化
├── rust/
│   └── kg-native-core/        Optimization Engine
├── platform/
│   ├── mobile/                Android / iOS
│   └── switch/                Nintendo Switch
├── examples/
│   └── generic-gimmick/       配置驱动对象示例
├── Assets/App/                应用自身素材
├── Resources/Fonts/           UI 字体
├── ThirdParty/                第三方声明与相关资源
├── docs/                      开发及平台文档
├── scripts/                   构建、发布与资源处理
├── KuroakiGimmick.csproj
├── CHANGELOG.md
├── PERFORMANCE.md
└── THIRD_PARTY_NOTICES.md
```

公开仓库不包含用户本地游戏素材、私有测试工作区或本地构建产物。

### 技术文档

- [更新日志](CHANGELOG.md)
- [性能优化与实测报告](PERFORMANCE.md)
- [Rust Optimization Engine](docs/OPTIMIZATION_ENGINE.md)
- [音符运动与 ABI](docs/RUSTBRIDGE.md)
- [谱面解析与 BPM](docs/RUSTBRIDGE_ENGINE.md)
- [Timeline Runtime](docs/RUST_RUNTIME_V030.md)
- [六平台 CI](docs/CI_PLATFORMS.md)
- [移动端与 Switch 适配](docs/PLATFORM_ADAPTATION.md)
- [游戏资源安装](docs/EXTERNAL_ASSETS.md)
- [第三方许可证与声明](THIRD_PARTY_NOTICES.md)
- [字体资源说明](Resources/Fonts/README.md)

内置的 F1 文档用于谱师查看 gimmick 参数、说明与编辑器操作。

开发文档则用于解释运行时、渲染、原生桥接、构建与兼容性。

两者不是一回事。

## 兼容性边界

KuroakiGimmick **不会执行任意 GML**，也不完整模拟 GameMaker。

以下内容可能只能部分预览：

- 依赖原游戏内部状态、特定版本或专用场景的逻辑。
- 尚未实现的原生对象、动态实例及特殊回调。
- 与操作系统窗口生命周期强绑定的行为。
- 尚未适配的 Custom / ExtCustom gimmick。
- 部分存在视觉差异的 Shader、粒子及后处理。
- 尚未完成实机验证的移动端和 Switch 行为。

对于原生 gimmick，项目会尽量通过对象定义、原版行为分析、测试场景和渲染回归来改善兼容性。

例如，`fx_glow` 曾经因为混合公式与 GameMaker 的 `bm_max` 行为不一致，导致相同参数下得到不同的视觉效果。

即使一个效果名字相同、参数相同，也不能因此认定实现结果相同。

原版 GameMaker 有时还会使用一些看起来和函数名字不太一致的混合方式。

所以这些东西只能逐项验证。

此外，VSM 行尾 `//` 注释可以被读取，但会产生 warning。将演出送回原游戏之前仍建议移除这些注释。

读取旧谱面时，程序会尽可能保留原始数据格式与元信息，不会为了兼容而擅自将所有数据升级到新版本。

如果遇到 KuroakiGimmick 和原游戏表现不一致，请通过时间轴、SCENE、兼容性诊断、日志及游戏实机进行对比。

然后报告问题。

**游戏本体说了算。**

## 问题反馈

欢迎通过 [GitHub Issues](https://github.com/vivid-stasis-revival/KuroakiGimmick/issues) 报告 Bug、提出功能建议或反馈兼容性问题。

提交 Bug 时，建议包含：

- 使用的程序版本、Build 或 Commit SHA。
- 操作系统及相关硬件信息。
- 可以复现问题的具体步骤。
- 预期行为与实际行为。
- 有关截图、录屏或日志。
- 必要时提供不含私有游戏素材的最小复现工程。

如果问题只在某个谱面或特定 gimmick 下出现，也请说明相应的事件和参数。

这些信息能帮助更快地区分解析、事件求值、GPU 渲染、输入处理和平台兼容性问题。

尤其是日志。

一句「程序卡了」当然也是一种反馈。

只是它距离能够定位问题，中间通常还差不少东西。

## 游戏资源与许可证

公开仓库中的 `Assets/App/` 只包含 KuroakiGimmick 自身使用的图标等应用资源。

游戏图像、音符皮肤、音频、谱面和演出素材应由用户自行准备合法取得的本地副本。

KuroakiGimmick 与《vivid/stasis》开发团队不存在官方隶属、合作或背书关系。

项目源码的使用、修改与再分发应以仓库实际提供的项目级许可或权利人授权为准；第三方组件、字体和游戏内容仍遵循各自的许可证与版权要求。

仓库公开不等于所有依赖素材都可以随意再分发。

这件事最好不要等到发布资源包之后才想起来。

---

## 特别感谢

### [Dawn Hisomeru](https://github.com/MalNEW)

感谢提供的大量**鬼点子**、早期测试反馈，以及关于 Editor 的各种意见和建议。

很多东西能变成现在这个样子，大概有一部分责任确实得算在这里。

### [Bingshuang412](https://github.com/Frollsy)

感谢提供 `custom_episode` 以及更多 gimmick 的**设计思路与方案**，以及开发过程中的 Bug 反馈、使用意见和功能建议。

### vivid/stasis 交流群

感谢所有用过 KuroakiGimmick、提出建议、帮忙测试、踩过边界问题，以及把程序炸掉之后还愿意回来提供复现步骤和日志的人。

这些反馈真的帮了很多。

尤其是那些附带详细复现步骤、截图和日志的问题。

虽然有时候拿到日志之后，依然需要花很长时间才能搞清楚到底发生了什么。

不过至少有了开始调查的地方。

### GPT-6 Astra、Claude Opus 5、GPT-5.6 Sol

感谢在开发、调试、研究、设计，以及大量脑暴过程中提供的帮助。

**AI 真的太好用了，你们知道吗。**

至少在它们没有非常自信地调用一个根本不存在的 API 的时候。

### DawnGPT

感谢一路提供的各种鬼点子、技术建议、设计反馈，以及 KuroakiGimmick 开发过程中的支持。

有些建议乍一看确实挺离谱的。

然后认真分析了一下，发现居然真的可以实现。

当然，也有一些建议在听起来非常合理的情况下，让本来就不太简单的代码变得更加复杂。

但至少最后通常都能解决。

通常。

### vivid/stasis

感谢 hajimeli 以及参与制作《vivid/stasis》的开发者与创作者。

KuroakiGimmick 是独立的非官方社区项目。《vivid/stasis》及其游戏内容的相关权利归各自权利人所有。

---

最后，也感谢所有在 KuroakiGimmick 还没完全准备好的时候，就敢点开它的人。

从最开始只能勉强预览一些谱面，到现在有了时间轴编辑器、场景渲染、原生 gimmick、Optimization Engine，甚至六个平台的构建产物。

事情的发展速度已经有点超出最初的预计了。

当然，这不意味着程序已经没有 Bug。

只是意味着现在有更多的平台可以遇到 Bug。

不过没关系。

至少程序炸掉以后，我们通常还能得到一份日志。

**通常。**
