<p align="center">   <img src="Assets/App/Kuroaki.png" width="200" alt="KuroakiGimmick"> </p>

<h1 align="center">KuroakiGimmick</h1>

<p align="center">   面向《vivid/stasis》的谱面演出预览与编辑工具。<br>   在同一条时间轴上编排 gimmick、图片、字幕、场景效果与窗口运动，并直接检查它们在实际歌曲场景中的表现。 </p>

<p align="center">   <img src="https://img.shields.io/badge/version-0.1.4_%2F_17.6.2-94cddd?style=flat-square" alt="v0.1.4 / Build 17.6.2">   <img src="https://img.shields.io/badge/C%23-.NET_8-b5a3e8?style=flat-square" alt="C# / .NET 8">   <img src="https://img.shields.io/badge/renderer-SDL3_GPU-94cddd?style=flat-square" alt="SDL3 GPU">   <img src="https://img.shields.io/badge/UI-中文_%2F_English-e8a4bd?style=flat-square" alt="中文 / English"> </p>

<p align="center">   <a href="#关于-kuroakigimmick">关于</a> ·   <a href="#主要功能">功能</a> ·   <a href="#快速开始">快速开始</a> ·   <a href="#编辑与导出">编辑与导出</a> ·   <a href="#命令行">命令行</a> ·   <a href="#构建">构建</a> ·   <a href="#特别感谢">特别感谢</a> </p>

> [!IMPORTANT]
> 本仓库只提供 **KuroakiGimmick 本身的源码、应用资源及可公开分发的第三方组件**。
>
> 《vivid/stasis》的游戏素材不会随仓库分发。完整场景预览及部分功能需要用户自行准备合法取得的本地游戏资源。

## 关于 KuroakiGimmick

**KuroakiGimmick**是一套为《vivid/stasis》演出制作而设计的预览与编辑工具。

它可以读取谱面和演出数据，把：

- 音符
- HUD
- VSM gimmick
- 图片
- 字幕
- Custom Proxy
- 场景效果
- 窗口运动

放进同一套时间轴和场景渲染系统中进行预览与编辑。

KuroakiGimmick 的目标不是重新实现整个 GameMaker Runtime，也不是成为完整的音符制谱器。

它更关心的是：

> **这个演出到底会长什么样，以及我要怎么把它改成我想要的样子。**

对于无法完整模拟的原生 GameMaker 行为，K/G 会尽量提供可检查的兼容实现与明确提示，而不是假装预览器和游戏本体完全一致。

最终效果仍应以实际游戏运行结果为准。

## 主要功能

### 🎬 演出预览

支持读取和组合：

- VSB / VSC 谱面
- VSM 演出
- VSP 图片声明
- SGV 工程
- 图片与字幕资源
- 部分 Custom Gimmick / 原生 gimmick
- 部分 ExtCustomGimmick 窗口演出

在歌曲场景中同时预览：

- Note
- Lane
- HUD
- 判定效果
- 图片
- 字幕
- Proxy
- Shader / FX
- Window Movement

### ✏️ 时间轴编辑

可以直接编辑：

- 事件时间
- 持续时间
- From / To
- Easing
- Proxy
- 图片姿态
- 字幕
- 窗口事件

并支持：

- 音符吸附
- 时间标记
- 循环试听
- 批量操作
- Loop 拆分 / 合并
- 撤销 / 重做
- 跨实例复制与粘贴

选择集可以通过系统剪贴板在两个 KuroakiGimmick 实例之间传递，也可以直接作为文本发送给其他人。

### 🖼️ 图片对象

提供独立的 **IMAGE CANVAS**。

支持编辑：

- 初始位置
- 缩放
- 旋转
- 透明度
- 动画关键帧
- 路径
- 动画端点
- 图片替换

可以通过：

```
INITIAL
KEY HERE
START / END
CONTINUE
```

等操作直接编排图片运动。

### 💬 字幕与文字对象

提供 **TEXT CANVAS**，支持：

- 多行文字
- 位置
- 缩放
- 旋转
- 透明度
- RGB 颜色
- 对齐
- 行距
- 自动换行宽度
- MOVE
- FADE IN
- FADE OUT

字幕内容与视觉动画可以分别编辑。

### 🪟 Window Movement

KuroakiGimmick 包含窗口运动预览系统。

支持：

- 虚拟桌面预览
- 真实辅助窗口
- 部分 ExtCustomGimmick 窗口事件
- Proxy 与窗口内容绑定
- 多窗口演出预览

由于真实窗口行为依赖操作系统，部分效果仍应在目标平台实际验证。

### 🎨 场景与特效

目前包含对多种演出行为的支持，例如：

- Custom Proxy
- 图片 Layer
- Film
- 粒子
- Room FX
- Shader
- Note 位移
- 部分原生 gimmick
- Custom / ExtCustom 行为

预览和视频导出尽可能共用相同的主要渲染流程。

具体兼容范围请以程序内诊断信息、F1 参考以及最终游戏实机表现为准。

### 📖 Custom Episode

KuroakiGimmick 支持在谱面演出中预览 `custom_episode`。

对应的剧情数据可以展开到时间轴中，使：

- 正常播放
- 时间轴拖动
- 反向定位
- 场景截图
- 视频导出

尽可能保持一致。

对于游戏本体中会被跳过、无法直接重现或依赖特殊场景状态的剧情步骤，K/G 会通过诊断信息提示，而不是静默吞掉。

### 🌐 中英双语

界面支持：

- 跟随系统
- 中文
- English

位置：

```
SETTINGS / 设置 → Language
```

语言设置只影响 KuroakiGimmick 界面，不会修改工程中的字幕、标识符或谱面内容。

## 支持平台

| 平台    | 图形后端               | 状态                           |
| ------- | ---------------------- | ------------------------------ |
| macOS   | Metal / SDL3 GPU       | 支持，Apple Silicon 已实际使用 |
| Windows | Direct3D 12 / SDL3 GPU | 支持                           |
| Linux   | -                      | 暂无交互式图形后端             |

Windows 需要可用的 **Direct3D 12**。

macOS 使用 **Metal**。

“能够编译”和“实际上能正常跑”并不是同一件事。

很遗憾，编译器没有义务替人类测试 GPU 驱动。

## 快速开始

### 环境要求

| 项目                | 要求                                       |
| ------------------- | ------------------------------------------ |
| .NET                | .NET 8 SDK，或能够构建 `net8.0` 的后续 SDK |
| macOS               | Metal                                      |
| Windows             | Direct3D 12                                |
| 完整场景资源        | 用户自行准备的本地 `Assets/`               |
| Ogg/Vorbis          | 内置解码                                   |
| 其他音频 / 视频导出 | FFmpeg                                     |

首次恢复 NuGet 包需要网络连接。

### 编译

```
dotnet restore KuroakiGimmick.csproj --locked-mode
dotnet build KuroakiGimmick.csproj -c Release --no-restore
```

### 启动

直接启动：

```
dotnet run --project KuroakiGimmick.csproj -c Release --no-build
```

直接打开 SGV 工程：

```
dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- \
  --editor /path/to/project.sgv.json
```

macOS：

```
bash scripts/run-editor.command /path/to/project.sgv.json
```

Windows PowerShell：

```
.\scripts\run-editor.ps1 -Project 'C:\Charts\project.sgv.json'
```

FFmpeg 可以直接加入 `PATH`，也可以通过：

```
KUROAKI_FFMPEG
```

指定。

## 编辑与导出

一个通常的工作流程：

### 1. 打开谱面或工程

使用：

```
OPEN CHART / VSM
```

打开：

- VSB / VSC
- VSM
- SGV
- 歌曲目录

也可以直接拖入对应文件或目录。

通过：

```
+ FILES / RESOURCES
```

附加：

- 图片
- 音频
- 曲绘
- VSP
- 其他工程资源

### 2. 编辑演出

按：

```
Tab
```

切换 Viewer / Editor。

在时间轴中添加或修改事件，并使用：

- `IMAGES`
- `TEXT`
- `SCENE`

完成对应内容的编辑和最终预览。

### 3. 保存工程

使用：

```
SAVE
SAVE AS
```

保存 `.sgv.json` 工程及相关编辑数据。

SGV 可以引用外部资源，因此移动工程时请同时保留对应文件及相对路径结构。

### 4. 检查并导出

使用：

```
REVIEW FILES
```

检查即将输出的文件和警告。

确认后执行：

```
EXPORT COPY
```

KuroakiGimmick 不会无提示覆盖已有导出目录。

## 导出格式

| 模式                  | 输出                                     |
| --------------------- | ---------------------------------------- |
| **VSM**               | 当前演出文本                             |
| **VSM + cgmk config** | VSM 与对应窗口配置                       |
| **Chart Folder**      | 当前难度演出及 VSP、图片、字幕等所需资源 |
| **Info Card**         | 当前难度的 16:9 乐曲信息卡片             |
| **Video**             | H.264 / AAC MP4                          |

### Chart Folder

Chart Folder 会整理当前难度需要的资源，并生成可重新打开的预览工程。

它：

- 不负责安装游戏扩展；
- 不保证收集其他难度的全部外部依赖；
- 不会覆盖原歌曲目录。

### Info Card

可以根据当前谱面与歌曲信息生成乐曲信息卡片。

支持不同输出分辨率，并尽量保持文字和图形在高分辨率输出下清晰，而不是简单把低分辨率结果硬拉大。

### Video

视频导出只记录歌曲场景。

以下内容不会被合成为单一视频画面：

- 虚拟桌面
- 系统窗口位置
- 多个真实原生窗口

真实窗口演出应在目标系统中单独检查。

## 常用快捷键

| 快捷键               | 操作                            |
| -------------------- | ------------------------------- |
| `Tab`                | Viewer / Editor 切换            |
| `Space`              | 播放 / 暂停                     |
| `Ctrl/⌘ + O`         | 打开                            |
| `Ctrl/⌘ + S`         | 保存                            |
| `Ctrl/⌘ + Shift + S` | 另存为                          |
| `Ctrl/⌘ + Z`         | 撤销                            |
| `Ctrl/⌘ + Shift + Z` | 重做                            |
| `Ctrl/⌘ + C`         | 复制选择集                      |
| `Ctrl/⌘ + V`         | 粘贴选择集                      |
| `E`                  | 创建 / 激活时间标记             |
| `Shift + E`          | 取消固定标记                    |
| `A`                  | 设置循环起点                    |
| `B`                  | 设置循环终点                    |
| `L`                  | 开关循环                        |
| `F1`                 | 打开离线文档                    |
| 长按 `W`             | 查看轨道详细说明                |
| `Esc`                | 取消当前操作 / 关闭真实窗口预览 |

## 命令行

KuroakiGimmick 同时提供命令行模式。

查看完整参数：

```
dotnet run -c Release --no-build -- --help
```

### 检查谱面

```
dotnet run -c Release --no-build -- \
  --inspect /path/chart.vsb \
  --time 45
```

### 生成乐曲信息卡片

```
dotnet run -c Release --no-build -- \
  --card /path/FINALE.vsb \
  --out card.png \
  --width 1920
```

可用宽度：

```
1280
1920
2560
3840
```

### 场景截图

```
dotnet run -c Release --no-build -- \
  --snapshot /path/project.sgv.json \
  --scene \
  --time 45 \
  --out frame.ppm \
  --report frame.json \
  --strict
```

### 视频渲染

```
dotnet run -c Release --no-build -- \
  --render /path/project.sgv.json \
  --start 30 \
  --end 45 \
  --fps 60 \
  --width 1920 \
  --out clip.mp4 \
  --strict
```

截图和视频渲染需要可用的 GPU 后端。

`--strict` 会在对应渲染错误发生时返回命令失败。

设置底部的“允许导出覆盖文件”（Overwrite exports）控制所有导出的同名文件处理，默认关闭，重启后保留。
开启后会在导出成功时替换已有输出；谱面文件夹按文件合并，保留本次未导出的其他文件，原谱面源文件仍受保护。
命令行导出也读取此设置，可添加 `--overwrite` 单次开启覆盖。

### 指定语言

```
--language zh-CN
--language en
--language auto
```

完整参数始终以：

```
--help
```

为准。

## 构建

当前版本：

**v0.1.4 / Build 17.6.2**

实际版本号以：

```
KuroakiGimmick.csproj
```

为准。

### macOS

```
# arm64 / x64 / all
bash scripts/publish-mac.sh arm64
```

### 从 macOS 构建 Windows .NET 单文件版本

```
# x64 / arm64 / all
bash scripts/publish-win.sh x64
```

### Windows

```
.\scripts\publish-windows.ps1 -Arch x64
```

输出类似：

```
dist/
└── KuroakiGimmick-v0.1.4-<RID>-17.6.2-<timestamp>/
```

并生成对应 ZIP。

使用 `all` 时分别产生两个架构。

macOS 不生成 Universal Binary。

## NativeAOT

KuroakiGimmick 支持 NativeAOT 构建。

### macOS

```
bash scripts/publish-aot-mac.sh arm64
```

### Windows

```
bash scripts/publish-aot-win.sh x64
```

NativeAOT 必须在目标操作系统上完成构建。

Windows NativeAOT 还需要对应架构的 Visual Studio C++ Build Tools。

NativeAOT 输出包含：

- KuroakiGimmick 原生可执行文件
- SDL3
- shaderc
- SPIRV-Cross
- 其他所需原生动态库

这些文件必须作为完整目录一起保留。

### `dotnet publish`

直接执行：

```
dotnet publish -c Release -r <rid>
```

时，Release + RID 默认选择 NativeAOT。

如果需要原有的 .NET 单文件发布：

```
-p:PublishAot=false -p:PublishSingleFile=true
```

普通：

```
dotnet build
```

不受影响。

macOS 构建在条件允许时使用 ad-hoc 签名，但不会执行 Apple notarization。

## 游戏资源

KuroakiGimmick **不会公开分发《vivid/stasis》的游戏 Assets**。

公开仓库中的：

```
Assets/App/
```

只包含 KuroakiGimmick 自身使用的应用资源。

以下内容不会作为游戏资源进入公开仓库：

```
Assets/       除 Assets/App/ 外的本地游戏资源
Samples/
Integrations/
bin/
obj/
dist/
```

开发与完整预览所需的《vivid/stasis》资源需要用户自行从合法取得的本地游戏副本中准备。

本项目不会授予任何《vivid/stasis》游戏素材的重新分发权。

## 兼容性边界

KuroakiGimmick 不执行任意 GML，也不完整模拟 GameMaker。

因此这些内容可能只能部分预览：

- 依赖游戏运行时内部状态的逻辑；
- 尚未实现的原生对象；
- 动态创建或修改的特殊实例；
- 与操作系统窗口生命周期强绑定的行为；
- 特定游戏版本的内部逻辑；
- 尚未适配的专用 gimmick。

复杂演出建议同时使用：

1. Editor 时间轴；
2. `SCENE` 场景预览；
3. 兼容性 / 诊断信息；
4. 最终游戏实机验证。

如果 KuroakiGimmick 与游戏本体表现不同：

> **游戏本体说了算。**

## 项目结构

```
KuroakiGimmick/
├── .github/
│   └── workflows/
│
├── src/
│   ├── App/
│   ├── Core/
│   ├── Graphics/
│   ├── Native/
│   └── UI/
│
├── Assets/
│   └── App/
│
├── Resources/
│   └── Fonts/
│
├── ThirdParty/
│
├── scripts/
│
├── KuroakiGimmick.csproj
├── CHANGELOG.md
├── THIRD_PARTY_NOTICES.md
└── README.md
```

### `src/Core`

包含：

- Assets
- Audio
- Definitions
- Documentation
- Editing
- Export
- Models
- Parsing
- Projects
- Serialization
- Timeline
- Timing
- Windows

### `src/Graphics`

包含：

- Cards
- Effects
- Notes
- Primitives
- Scene
- SDL3 GPU
- Text
- Windows

### `src/UI`

包含：

- Viewer
- Editor
- 原生菜单
- 动画
- 本地化
- UI 状态与交互

### `scripts/`

包含：

- 资源处理辅助工具
- 发布脚本
- NativeAOT 构建脚本

公开源码不包含内部测试、检查脚本以及游戏资源 Dump 脚本。

## 第三方组件

KuroakiGimmick 使用了包括但不限于：

- .NET
- SDL3 / SDL3-CS
- Silk.NET
- Shaderc
- SPIRV-Cross
- NVorbis
- StbImageSharp
- StbImageWriteSharp
- StbTrueTypeSharp
- Noto Sans SC
- Fusion Pixel

各组件的版权及许可证信息见：

[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

以及：

```
ThirdParty/
```

字体资源与其他第三方内容具有各自独立的许可证。

## 许可证

KuroakiGimmick **源代码**的使用、修改与分发条件以仓库中的：

```
LICENSE
```

为准。

本仓库使用的第三方组件、字体及其他第三方资源仍遵循其各自的许可证与版权声明。

详细信息请参阅：

[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

以及：

```
ThirdParty/
```

KuroakiGimmick 的源码许可证不会替代或重新授权这些第三方内容。

## 特别感谢

### [Dawn Hisomeru](https://github.com/MalNEW)

感谢提供的大量**鬼点子**、早期测试反馈，以及关于 Editor 的各种意见和建议。

很多东西能变成现在这个样子，大概有一部分责任确实得算在这里。

### [Bingshuang412](https://github.com/Frollsy)

感谢提供 `custom_episode` 以及更多 gimmick 的**设计思路与方案**。

同时感谢在开发过程中提供的部分 Bug 反馈、使用意见和功能建议。

### vivid/stasis 交流群

感谢 vs交流群 中所有使用过 KuroakiGimmick 的人。

也感谢所有：

- 提交过 Bug 的；
- 提供过功能建议的；
- 帮忙测试过新版本的；
- 踩中过各种奇怪边界情况的；
- 把什么东西炸掉以后还能回来详细告诉我“我是怎么炸的”的人。

这些反馈真的帮了很多。

### GPT-6 Astra、Claude Opus 5、GPT-5.6 Sol

感谢在开发、调试、研究、设计，以及大量脑暴过程中提供的帮助。

**AI 真的太好用了，你们知道吗。**

### DawnGPT

感谢一路提供的各种鬼点子、技术建议、设计反馈，以及 KuroakiGimmick 开发过程中的支持。

### vivid/stasis

感谢 hajimeli 以及参与制作 vivid/stasis 的所有开发者和创作者。

KuroakiGimmick 是非官方项目，与 hajimeli **不存在官方隶属、合作或背书关系**。

《vivid/stasis》及其游戏内容的相关权利归各自权利人所有。

最后，也感谢所有在 KuroakiGimmick 还没完全准备好的时候，就敢点开它的人。

至少程序炸掉以后，我们通常还能得到一份日志。

通常。
