# KuroakiGimmick v0.1.0

C# + SDL3 GPU 的原生 gimmick 预览器。载入 `.vsb` / `.vsc` 谱面与 `.vsm` 演出，搭配 `.vsp` 图片声明，预览并导出 MP4。黑底猩红 UI，320 × 180 像素场景。音符已使用提供的 vsnotes 图片皮肤，预览与视频导出共用。

这是一版可运行的初始实现。Custom Gimmicks v1.12.7 的图片和文本已接入。支持读取外部 ASTELLION 谱面，运行包不附带 ASTELLION 的歌曲、贴图或示例谱面。它不执行任意 GML，也不是完整 GameMaker 模拟器。具体差异见 [兼容范围](docs/COMPATIBILITY.md)。真实窗口移动留给 v0.1.1。

ASTELLION 游玩素材已支持从歌曲目录的 `astellion/manifest.json` 自动加载：水下背景、菱形碎屑、sides、astbars、轨道装饰和整屏原版后处理。把完整 `ASTELLION_Test_Chart_Pack` 拖入窗口即可，默认 ENCORE；歌曲素材保留在曲包内。报告保留“随机序列/公共粒子对象未逐版确认”，剧情转场未启用。资源格式与验证见 [ASTELLION 接入说明](docs/ASTELLION.md)。

原生 gimmick 支持共享对象配置：根据 VSM 的 `!obj` / VSB 对象名读取 `Assets/Gimmicks/<对象名>/manifest.json`，复用精灵回调、GUI 覆盖层、shader 绑定和粒子/棋盘能力。现已提供 `obj_scarletdeath_gimmick` 配置，所有声明该对象的谱面都可使用其 `heartattack1–6`、`cover2` 与原生后处理；无需按歌曲名、谱面文件名或目录配置。详见 [共享对象配置](docs/NATIVE_GIMMICKS.md)。

当前修订 **src-r16-complete-bumper-frame**：保留 manifest NoteSkin，并按当前 `obj_note_rendering` 的 `draw_sprite_ext` 语义整帧还原 compact v1 bumper。当前 `sp_note_bumper_normal` / timing 为 107×7、origin (53,3)，其中 L/M/R 侧标记属于原 sprite，不能裁掉；`bumper_mine` 仍按自己的 45×7。完整 v2 `NotesExact` 继续按 TargetX/Y 与 sprite origin 还原 raw atlas crop。公共资源仍在程序旁 `Assets`，不含歌曲素材。详见 [Note Skin](docs/NOTE_SKIN.md)、[r13 说明](docs/SCARLET_BEAT_R13.md) 和 [macOS / Windows publish](docs/PUBLISH.md)。

## 打开

**Mac 运行包（Apple Silicon / Intel）**：解压后打开 `KuroakiGimmick.app`，保留旁边的 `Assets` 文件夹一起移动。包内包含两种架构的 .NET 8.0.30 运行时与 SDL3，不需要安装 SDK。也可运行旁边的 `Start_Kuroaki.command`，出错时终端会保留信息。运行包未签名、未公证；如果 macOS 阻止首次打开，请在“系统设置 → 隐私与安全性”中确认允许这个应用。没有自动关闭系统防护的脚本。

**Windows x64 运行包**：解压后双击 `Start_Kuroaki.cmd`。运行时已包含，不需要 SDK。请保留整个目录。

**源码**：安装 .NET 8 SDK，在项目目录运行 `dotnet run -c Release`。SDL3 本体随固定版本 NuGet 包恢复，无需单独编译 SDL。Mac 使用 Metal，Windows 使用 Direct3D 12。着色器编译依赖随 NuGet 恢复，无需另装 GLSL／HLSL 编译器；现有 GLSL 特效和外部对象 shader 在运行时转换。详见 [SDL_GPU 后端与验证](docs/SDL_GPU.md)。

首次打开是空白工作区。已移除 BUNDLED / ASTELLION 和 IMAGE GIMMICKS DEMO 按钮。打开或拖入自己的谱面、VSM 或歌曲文件夹后，点击 PLAY 开始。

## 载入你自己的演出

- `OPEN CHART / VSM`：打开 `.vsb`、`.vsc`、独立 `.vsm` 或 `.sgv.json` 工程。
- `+ FILES / RESOURCES`：把效果、图片声明、音乐或曲绘附加到当前工程。外置 VSM 会替换 VSB 内嵌 mods。
- `gimmick-fx.json` 可自动关联，也可附加 `.fx.json` 原房间配置，详见 [FX_RESOURCES.md](docs/FX_RESOURCES.md)。
- 也支持拖入歌曲文件夹或文件；一次选择多个文件时，先载入谱面 / 工程再附加资源。
- 打开 `FINALE.vsm` 时自动找同目录 `FINALE.vsb` / `FINALE.vsc`；打开谱面时自动关联同名 VSM，缺失时用 `GLOBAL.vsm`。图片优先同难度 VSP，再 `GLOBAL.vsp`。
- VSC 是文本谱面：时间与 hold 结束时间以毫秒计，type 3 的 `b:BPM` 建立变速表。`info.json` 的歌曲名和 `bpm_display` 也会自动读取。
- 谱面目录中的 `FINALE_text.txt` 或 `FINALE_text_*.txt` 自动关联，无需逐一附加。
- 图片路径相对谱面目录；独立 VSM 搭配手动选择 VSP 时，相对 VSP 目录。请保留图片子目录结构。
- 常见同目录音乐名 `music.ogg`、`song.ogg`、`song.wav`、`song.mp3` 等会自动关联。否则手动附加音频。
- 调整右侧 BPM / offset / scroll，按 SAVE 保存相对路径工程。`R` 重新读入源文件并保留当前位置。
- `.vmv` 不作为可游玩谱面载入；收到的 looping.vmv 是 mod 统计缓存。原文件不会被修改。

Ogg/Vorbis 预览内置解码。其他音频格式及视频导出需要 **FFmpeg**。

## Note dump / 原版音符皮肤

推荐把 `dump_notes_full.sh` 产生的整个 `NoteSkinFull` 文件夹放到 `Assets/NoteSkinFull/`；程序会优先使用 `NotesExact`。现有紧凑 `newdumpnote.zip` 可运行 `bash scripts/install-note-dump.sh /path/newdumpnote.zip` 安装到 `Assets/Notes/`。旧 `vsnotes` 仍兼容。替换后按 `R` 事务式重载，损坏或缺文件的皮肤不会覆盖当前可用皮肤。

兼容报告新增 `noteSkin` 字段，记录实际加载的 format、目录、是否 exact lanes 与 dump warning。详细映射见 [NOTE_SKIN.md](docs/NOTE_SKIN.md)。

## 图片 gimmick

新版图片声明格式是 `.vsp`，动画参数仍写在 `.vsm` 中。例如：

```text
#Layer
background,-100
front,10
#Image
background:
static,bg,images/bg.png,0,320,180
front:
animated,logo,images/logo-strip.png,0,8,64,64,0
```

`logo-strip.png` 必须是 8 帧横向条带。省略宽高时使用原始单帧尺寸。图层和图层内图片均按优先级升序绘制，优先级较大的在上。

```text
!obj:obj_custom_gimmick
!proxies:1
0,4,outCubic,0,1,imgalp_logo,-1
0,16,linear,0,360,imgrot_logo,-1
0,16,linear,0,32,imgidx_logo,-1
```

13 类图片参数：`imgx`、`imgy`、`imgxtime`、`imgytime`、`imgrot`、`imgscalex`、`imgscaley`、`imgscaleytime`、`imgskewx`、`imgskewy`、`imgcolrgb`、`imgalp`、`imgidx`；后面接 `_图片名`。PNG 透明度、JPG、共享贴图、指定初始大小、帧号取整及循环均可用。完整可运行示例在 `Samples/ImageGimmicks/`。

只给 VSM 中的 `imgalp_weavers1` 等参数，不能推导出原始图片。需要对应 VSP 及其中引用的实际图像。缺图和未声明 ID 会出现在 REPORT。

## 文本 gimmick

支持旧式 `[难度]_text.txt` 和新版 `[难度]_text_ID.txt`。ID 可以是字符串；两种形式同时存在时按原加载器优先使用旧式单文件。每行是 `拍数,内容`，支持 `{n}` 换行、`{comma}` 逗号和 `{t}` 制表符。跳转会重新定位到该时刻的歌词。

支持 `textX_ID`、`textY_ID`、位置附加项 `textX_IDb` / `textY_IDb`、`textrot_ID`、`textalp_ID`、`textcolrgb_ID`、`textscale_ID`、`textsep_ID`、`textmaxwidth_ID`、`textalignh_ID` / `textalignv_ID`。旧式支持无 ID 的位置、缩放、旋转、透明度和 `textcolhex`。尊重配置文件的 `ENABLE_TEXT`。

歌词优先使用同一套原游戏字体图集和原字宽；原资源不可用时才回退到 Fusion Pixel 10px 点阵字体并报告缺图。界面字体与歌词字体独立。轨道使用原始 `sp_laneOverlay` PNG 和 `bgalph`；灰色判定区使用原始 `sp_holdnote_overlay` PNG 和 `holdoverlayalpha`；HUD 使用 `uialpha`；谱面主动隐藏轨道时不会强制显示。黑红配色只属于界面，预览没有额外红色网格或节拍闪光。

## 导出视频

安装 FFmpeg 后，设置 IN / OUT、分辨率和帧率，点击 EXPORT MP4。导出的是纯演出画面，不含编辑器 UI。

Mac 如果使用 Homebrew，可运行：

```bash
brew install ffmpeg
```

也可以把可信来源的 `ffmpeg` 可执行文件放在 app 的 `Contents/Resources/app/tools/`，或用环境变量 `KUROAKI_FFMPEG`（兼容旧变量 `SCARLET_FFMPEG`） 指定完整路径。Windows 对应 `tools/ffmpeg.exe`。程序也查找 PATH 和 Mac 常见 Homebrew 路径。FFmpeg 需要 `libx264` 和 AAC 编码支持，**运行包未包含 FFmpeg**。

输出 H.264 + AAC MP4，固定帧时钟，30 / 60 / 120 FPS，720p / 1080p / 1440p / 4K。场景原生 320 × 180，输出按最近邻放大，不会凭空增加原画细节。导出采用歌曲原速、原始音频，不使用预览的变速和监听音量。IN / OUT 以秒计，末尾向上补齐到整帧。

ESC 可取消导出；未完成文件会清理，已有视频不会被覆盖。选了音频但解码失败时会阻止导出，避免意外生成无声片段。只载入无音乐 VSM 时可正常导出无声视频。

## 快捷键

| 按键 | 操作 |
|---|---|
| 空格 | 播放 / 暂停 |
| ← / → | 后退 / 前进一帧 |
| Shift + ← / → | 后退 / 前进一秒 |
| Home / End | 跳到开头 / 结尾 |
| I / U | 设置导出 IN / OUT |
| N / P / M | 音符 / 后处理 / 静音 |
| F / ESC | 全屏 / 退出全屏或取消导出 |
| Ctrl 或 ⌘ + O / S / E | 打开 / 保存工程 / 导出 |
| R / H | 重新载入 / 帮助 |

## 命令行与开发

```bash
dotnet run -c Release -- --self-test
dotnet run -c Release -- --gpu-test
dotnet run -c Release -- --inspect /path/ENCORE.vsb --vsm /path/ENCORE.vsm --vsp /path/ENCORE.vsp
dotnet run -c Release -- --render /path/project.sgv.json --out clip.mp4 --start 30 --end 45 --fps 60 --width 1920
```

独立 VSM：`--inspect file.vsm --bpm 174`。还可用 `--fx-profile room.fx.json`、`--audio song.ogg`、`--offset 0`、`--profile auto|core|astellion`。`--snapshot --time 45 --out ui.ppm` 可输出实际 UI 截图；追加 `--scene` 只取演出画面。

运行包的命令行入口：Mac `KuroakiGimmick.app/Contents/MacOS/KuroakiGimmick`；Windows `runtime\dotnet.exe KuroakiGimmick.dll`。

构建脚本：`bash scripts/build-mac.sh`；Windows PowerShell：`./scripts/build-windows.ps1`。发布正常通过 .NET SDK 的 self-contained publish；在本次容器里 SDK 发布阶段遇到进程元数据接口异常，所以下载包采用同一已编译程序集 + 官方私有运行时启动器，未声称执行过 Mac 或 Windows 本机发布测试。

代码布局：`Core/` 格式、时间轴、音频、导出；`Graphics/` SDL_GPU、着色器转换与场景绘制；`UI/` SDL UI；`Native/` SDL ABI；`Samples/` 小型格式与图片示例，不包含 ASTELLION 素材。测试和实际验证记录见 [VALIDATION.md](docs/VALIDATION.md)。

日志：应用数据目录下 `KuroakiGimmick/viewer.log`。兼容报告和默认视频目录：文稿 / `KuroakiGimmick/Exports`。当前 UI 文案为英文，界面字体不包含完整 CJK；中文路径可以读写，部分文件名显示为替代字符。歌词另有 CJK 点阵字体。

通用轨道与菱形粒子使用用户导出的 6 张原始 PNG，共 1,532 字节，不含 ASTELLION 专属素材。原始粒子图、生成/运动/淡出、曲绘逐像素乘色和 plaudite 即时爆发已接入，独立粒子 glow 尚缺 `shader_optimized_glow` 与对应游玩房间配置；全局 glow 仍为已标注的兼容实现。

## r8 原房间 FX

构建标记 `src-r8-room-fx`。左侧 ROOM FX 可切换 7 个原房间配置，支持实际 colour balance / colourise 差异与原可见性。自制 AUTO 暂用普通 gameplay 并明确标记，仍应选择真实房间。另补上 notealpind0..6。详见 [ROOM_FX_R8.md](docs/ROOM_FX_R8.md)。

## r7 更多 gimmick（已包含）

已接入侧向粒子爆发、曲绘背景显露/染色/模糊、红色与 hue/colourise、逐轨 note 位移/boost、proxy 纵向斜切和四组 twist/波形。chroma/contrast 有原 shader 绑定，但需要对应房间 FX 配置。范围与测试见 [GIMMICKS_R7.md](docs/GIMMICKS_R7.md)。

## r6 曲绘修复（已包含）

同目录 jacket.jpg/png 自动乘到粒子上，颜色随当前屏幕位置变化；支持 custom_jacket 与外部 plaudite 封面切换。运行包仍不附带歌曲封面。Looping / Weavers 的逐项结果、文件命名和未实现效果见 [LOOPING_WEAVERS_R6.md](docs/LOOPING_WEAVERS_R6.md)。


## r10：UI 遮挡与字体选项

窗口标题固定为 `Kuroaki/Gimmick`，程序、启动器、图标和页头统一为 Kuroaki。这些 r10 功能已包含在当前 r11 中。

已内置本次成功导出的原始公共 UI 精灵和三套字体，解压即可显示原 UI，无需再运行导出脚本。可选 `bash scripts/Dump_Game_UI_Mac.sh` 从其它版本游戏更新资源，按 R 重读。不会导出或附带歌曲、曲绘、音频。完整说明见 [GAME_UI_R9.md](docs/GAME_UI_R9.md)。

SETTINGS 面板新增 Text Font：**Default（新版）/ Monaco（旧版）**，使用原游戏字体图集，影响 Escape、歌曲名、作者及 gimmick 文本；图片里已经画好的文字保持原样。两个选项对应原代码的 fnt_monacovs / fnt_phosphor，固定数字/难度图标也保持原素材。

其余设置提供 Note Alignment（Top / Bottom）、实际渲染分辨率 320×180 / 640×360 / 720p / 1080p / 1440p / 4K、试听音量、音频延迟和视觉延迟。正延迟表示更晚，范围 ±2000ms。音量与音频延迟用于试听设备校准，导出保留原声；视觉延迟只改变 note 显示时机，预览/视频一致，gimmick 不随其偏移。设置即时生效，DONE/ESC 保存默认值；工程也保留独立设置。

320×180 是 VS 原生画面，默认 FIT；INTEGER 仍可手动选择。高分辨率会分配真实 GPU 渲染目标，重新绘制几何和后处理；原像素贴图不会增加细节。右侧 VIDEO EXPORT 的输出尺寸独立于 SETTINGS 的内部渲染尺寸。

原玩法 UI 包括 PAUSE Escape、score/EX、底部歌曲名/作者和难度牌，省略左侧统计。没有玩家输入判定，原生分数/EX 保持 0；图片 gimmick 绘制的假分数按谱面控制。固定玩法 UI 在移动/旋转轨道之后合成，PAUSE、Escape、底部标题和难度不会被轨道挡住；高优先级 VSP 图片仍可盖住 UI，并保留轨道副本变换。uialpha 控制原 UI，hom 参与 proxy 侧区/底栏及固定 HUD 合成，VS UI 按钮可关闭原 UI。

撤回 r8 轨道延伸到 y=180 和播放状态标签的误改：轨道到 y=165，为歌曲标题留出底部；没有固定假轨道续接。VSP 图片按原 depth 插入轨道、判定区、note、HUD 之间，可覆盖轨道和 UI。按住的 hold 使用原 obj_pressed_holds 规则，去掉判定线下多余起始帽；旋转 note 增加原一像素补偿。

从完整源码发布：`bash scripts/publish-mac.sh` 与 `bash scripts/publish-win.sh`；均可在 Mac 上执行，分别输出 `.app` / 单文件 `.exe` 与完整 ZIP 到 `dist`。Windows 的 .NET 运行时和 SDL3 已合并进 `.exe`，资源文件夹仍需一起分发；两个平台的发布包均不包含 `docs`。
