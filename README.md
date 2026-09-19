
<h1 align="center">KuroakiGimmick</h1>

<p align="center">
  为《vivid/stasis》制作的谱面演出预览与编辑工具。<br>
  在时间轴上编排 gimmick、图片动画与窗口运动，让演出随节拍发生。
</p>

<p align="center">
  <a href="#快速开始">快速开始</a> ·
  <a href="#编辑与导出">编辑与导出</a> ·
  <a href="#示例">示例</a> ·
  <a href="#构建应用包">构建应用包</a> ·
  <a href="#开发与检查">开发与检查</a> ·
  <a href="#资源说明">资源说明</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.1.3_%2F_17.0-94cddd?style=flat-square" alt="v0.1.3 / 17.0">
  <img src="https://img.shields.io/badge/C%23-.NET_8-b5a3e8?style=flat-square" alt="C# / .NET 8">
  <img src="https://img.shields.io/badge/renderer-SDL3_GPU-94cddd?style=flat-square" alt="SDL3 GPU">
  <img src="https://img.shields.io/badge/Assets-private-e8a4bd?style=flat-square" alt="Assets 不公开">
</p>

<table align="center">
  <tr><th align="center">macOS</th><th align="center">Windows</th><th align="center">从源码开始</th></tr>
  <tr>
    <td align="center">Metal · Apple Silicon / Intel<br><a href="#构建应用包">构建 .app</a></td>
    <td align="center">Direct3D 12 · x64 / ARM64<br><a href="#构建应用包">构建 .exe</a></td>
    <td align="center">.NET SDK · 本地 Assets<br><a href="#快速开始">运行编辑器</a></td>
  </tr>
</table>

> **`Assets/` 中的游戏素材属于游戏《vivid/stasis》及其相应权利人，不公开提供。** 本地运行和构建需要自行准备有权使用的完整资源目录。

## 功能

- **谱面预览**：读取 `.vsb` / `.vsc` 谱面、`.vsm` 演出、`.vsp` 图片声明和 `.sgv.json` 工程；预览与视频导出共用 SDL3 GPU 场景管线。
- **判定与 HUD**：还原命中特效与分数 / EX / 连击 / 判定弹窗 / hold 特效，在 SETTINGS 的 VS UI 分区逐项开关。
- **时间轴编辑**：调整事件时间与缓动、吸附音符、循环试听、撤销重做；右侧字段就地编辑，轨道上框选批量改参与右键菜单；使用 E 标记固定添加位置。
- **图片动画**：IMAGES 列表、本地 IMAGE CANVAS、移动/缩放/旋转、初始与定时姿态、成组动画、路径续接和资源替换。
- **文字对象**：TEXT 列表、字幕内容与时间点编辑、独立文字画布、拖动/等比缩放/旋转、颜色与排版、动画起终点编辑，时间轴按对象折叠。
- **老电影效果**：`fx_film` 接入原始 `_filter_old_film` shader、噪声纹理和房间参数，预览与视频导出共用实现。
- **窗口演出**：虚拟桌面与真实辅助窗口预览，支持部分 ExtCustomGimmick 窗口移动及 Proxy 内容绑定。
- **保存与导出**：保存编辑工程副本，导出 VSM、VSM + cgmk config、Chart Folder 或 MP4。
- **工作区与文档**：可调布局和 UI 缩放、Nekomiya / Scarlet / Kuroaki 主题、F1 离线参数手册和轨道说明卡。

当前项目标识为 **v0.1.3 / Build 17.0**，以 `KuroakiGimmick.csproj` 为准，解压目录名不代表应用版本。历史修订和旧验证记录已归档，不作为当前版本的验收结论。

## 快速开始

需要 .NET 8 SDK（仅安装 Runtime 不够）或可构建 `net8.0` 的后续 SDK、首次恢复依赖所需的网络，以及完整的本地 `Assets/`。macOS 使用 Metal，Windows 使用 Direct3D 12；当前没有 Linux 交互图形后端。Ogg/Vorbis 预览内置解码，其他音频格式与视频导出需要 FFmpeg。

在项目根目录执行：

```bash
dotnet restore KuroakiGimmick.csproj --locked-mode
dotnet build KuroakiGimmick.csproj -c Release --no-restore
dotnet run --project KuroakiGimmick.csproj -c Release --no-build
```

直接打开编辑器示例（先构建，成功后启动）：

```bash
# macOS；也可在末尾传入自己的工程路径
bash scripts/run-editor.command
bash scripts/run-editor.command /path/to/project.sgv.json
```

```powershell
# Windows PowerShell
.\scripts\run-editor.ps1
.\scripts\run-editor.ps1 -Project 'C:\Charts\project.sgv.json'
```

`KUROAKI_DOTNET` 可指定 dotnet 可执行文件。FFmpeg 可放入 `PATH`，或通过 `KUROAKI_FFMPEG` 指定。缺少 Assets 时，构建可能因内嵌资源或图标缺失而失败；资源导出工具仅处理部分资源组，不能生成完整 Assets。

音频解码后的浮点 PCM 上限为 **1024 MB（1,024,000,000 字节）**，Ogg/Vorbis 和 FFmpeg 解码路径使用相同限制。它与压缩音频文件大小不同；例如约 16 分钟的 44.1 kHz 双声道音频解码后约 337 MB。实际内存还包括渲染资源及解码缓冲。

## 编辑与导出

### 打开与编排

使用 `OPEN CHART / VSM` 打开谱面、演出或工程，也可拖入歌曲目录；通过 `+ FILES / RESOURCES` 附加图片声明、音频、曲绘和 FX 配置。同难度 VSM/VSP 优先，缺失时可使用 GLOBAL 文件。图片路径相对谱面或声明文件目录解析。

按 Tab 切换 Viewer / Editor。选择音符作为时间参考，在轨道上双击或使用 `+ ADD GIMMICK` 添加事件；拖动片段调整时间，拖动右侧手柄调整持续时间。E 创建时间书签，默认添加位置优先使用活动标记，其次是选中音符和播放游标。

VSC 音符时间以毫秒计，VSM 事件时间以拍计；VSM duration 按事件起点 BPM 转为秒。窗口事件的 `t`、`easeDur`、`dur` 使用秒，跨 BPM 编辑时不要混用单位。未知行、重复语法和动态值 `_` 按原有保留规则处理。

重复事件支持 `起始拍(:终止拍:间隔)`，也兼容原有的 `start:end:step`。例如 `-10.01(:1.9:0.02),0,linear,0.08,1,velocity,-1` 从 **-10.01 拍**开始，每 0.02 拍触发一次，直到不超过 1.9 拍；本例最后一次为 1.89 拍，共 596 次。负数、小数及科学记数法均可使用。编辑器将一整行循环保留为一个片段，编辑和保存继续保留括号写法。间隔须为正数、终点不得早于起点，总展开量最多一百万事件；非法括号语法会报明确错误，不再误当作 GML 数字前缀截断。

### 图片对象

将 PNG/JPEG/BMP/TGA 拖入 Editor，或从 IMAGES 选择已有实例。IMAGE CANVAS 使用 **320 × 180 的图片本地坐标**；INITIAL 编辑基础姿态，KEY HERE 写入定时姿态，START / END 修改动画端点，CONTINUE 续接路径。LINK JOINTS 仅联动唯一且数值相接的相邻动画。

SCENE 用于检查实际场景效果。图片画布不反算 Proxy、后处理或屏幕扭曲；动态值、冲突事件与不支持的变换仍需使用原始事件面板。完整操作见 [图片对象手册](docs/IMAGE_OBJECTS_16_2.md)。

### 文字对象与字幕

Editor 左侧选择 **TEXT → + TEXT**，直接输入内容；可粘贴多行文字，也可用 `{n}` 换行。需要切换 Custom 对象或启用字幕时，界面会先提示。已有 `[难度]_text.txt` 和 `[难度]_text_[tid].txt` 会显示为文字对象；旧式单文件字幕保留其优先级，不会在添加时自动转换格式。

选中文字后进入 **TEXT CANVAS**：拖动文字移动，拖角等比缩放，拖顶部手柄旋转；Shift 旋转吸附到 15°，方向键移动 1 像素（Shift 为 10），Ctrl/⌘ + 滚轮缩放视图。一个拖动对应一次撤销，Esc 或窗口失焦取消。画布复用场景字体绘制，使用本地 320×180 坐标；SCENE 查看实际后处理效果。

右侧 **INITIAL / KEY HERE** 选择写入时间，Content 编辑该拍字幕，CLEAR TEXT HERE 添加空字幕作为结束点。可修改位置、大小、旋转、透明度、颜色、对齐、行距与换行宽度；向下滚动可添加 MOVE / FADE IN / FADE OUT 动画。选择动画后使用 **EDIT START / EDIT END** 配合字段或画布调整端点。时间轴的 TXT 轨与图片对象采用一致的片段样式，仅显示姿态和动画，不绘制字幕内容蓝条；字幕内容与时间点在文字面板中编辑。展开箭头可操作原始事件轨道，支持原有拖动时间和持续时间操作。

动态值、重复行、同拍多次写入、歧义的尾缀 `b` 名称与冲突动画不会被直接变换重写，应使用原始事件面板。现有旧式字幕的颜色继续遵循 `col_convertion`，具名字幕颜色使用 RGB。字幕内容和视觉参数分别保存，内容修改不会改变其他时间点。

### fx_film

时间轴可直接使用 `fx_film`，0 关闭、1 开启；F1 中的参数说明仍可用于添加事件。开关控制选定房间真实存在的 `FX_film` 图层，保留原图层深度，不依赖 `ENABLE_NON_BASE_FX`。当前本地资源已补齐 gameplay、angelstar、extendnova、scarletdeath、sekaisen、starcrashers 的原始电影图层；原房间没有该层时不会自动套用其他房间的效果。

原始 shader、纹理与参数从本机《vivid/stasis》只读导出，仍保存在非公开的 `Assets/` 中。`scripts/assets/Dump_Film.csx` 可用于 UTMT 只读导出，输出目录通过 `KUROAKI_FILM_OUTPUT` 指定且必须尚不存在。噪声动画使用演出时间，前后跳转与固定帧导出可重复。

### 星星、遮罩与 playspeed

Custom 对象已支持 `starspawner_timer`、`starspd_low/high`、`starspd_multiplier`、`active_startrans`、`startrans_alpha`，并接入同一原始系统的渐变色星星参数。`ENABLE_STARPARTICLE` 默认关闭，启用后才创建星星系统。蒙版星星在背景乘色之前绘制，停用时冻结自身状态；速度变化使用累计位移，已经越界或死亡的粒子不会因为反向播放而复活。预览与导出使用相同的固定 60 Hz 模拟和随机种子，可重复跳转；随机布局不保证与游戏某一次运行完全相同。

`cover1/2/3` 使用游戏原始精灵及黑色混合，按源码的覆盖层顺序绘制。`hide_combo` 仍是空操作：无论谱面是否包含该参数、其值为何，都不改变画面；顶部连击读数由 SETTINGS 的 TOP COMBO READOUT 控制，默认显示 COMBO，可设为 OFF。该参数仍保留在谱面中。

`playspeed` 遵循游戏源码：在事件开始时将当前速度乘以按游戏规则取两位小数的终值，忽略普通补间时长，受 `ENABLE_MUSIC_CONTROL` 控制。负拍数初始化同样生效；多个事件累乘，倒拖不会重复叠乘。界面手动倍速作为额外倍率，时间轴仍显示音乐/谱面时间；Viewer 在时间旁显示非 1 的 `CHART` 倍率。

视频导出遵循谱面 `playspeed`，同时调整画面进度、音频速度与音高，不继承界面手动倍速。例如 3 秒谱面区间在 1.2 倍速下输出 2.5 秒视频。支持中途倍率切换；累计倍率支持 0.01～100，音频导出单次最多 512 个变速区间，超出限制明确报错。

**对象作用域**：`track_alpha`、`en_whiteoverlay`、`eo_endsat1–4`、`eo_endcg`、`sekaisen_arrow_point` 属于 Seka­isen 原生对象。原游戏的 Custom 对象没有注册这些参数，会在 `updateMods` 中跳过。对于声明 `obj_custom_gimmick` 的谱面，Viewer 保留源事件，按原游戏跳过，并在兼容提示和报告的 `sourceNoOps` 中说明原因；这里没有把这些参数伪装成已完成的原生对象适配。

上述实现依据本机游戏的只读 UTMT 导出；相关原始代码和素材保存在非公开的 `Assets/CustomGimmicks/`。专项检查：`dotnet run -c Release -- --custom-adaptation-self-test`；`--gpu-test` 包含星星、遮罩和 `hide_combo` 不改变画面的实际绘制检查。

### 其他 Custom 演出与音乐跳转

- `fx_edge`：使用原游戏边缘检测 shader、房间阈值和图层深度，受 `ENABLE_NON_BASE_FX` 控制；常规 gameplay 房间包含其创建代码动态添加的 `FX_edge`。
- `static`：使用原始四帧雪花素材，按 `floor(beat × 16) % 4` 切帧，并按游戏中的四个区域绘制。
- `unraveling_sidething` / `sides`：生成左右冲击波及原始粒子拖尾；位置、速度、淡出和生存时间来自游戏对象代码。
- `df_sideline` / `df_sideline_alpha`：控制两侧原始线条的位置和透明度，同时支持同一处理器的 `df_grid_alpha/top/bottom`；受 `ENABLE_DF_GRID_AND_SIDELINE` 控制。
- `plaudite_pburst`：计数模式保留原行为；连续模式将 From 作为开关、To 作为持续毫秒数，模拟原代码的倒计时补间和固定 tick 发射，不把持续时间当粒子数量。超出粒子预算时只报告一次，不按展开事件刷屏。
- 水平噪声：已使用游戏的 `sp_noise2` 原始噪声纹理；素材缺失时才回退并报告。

`jumpto_beat` 在正常播放时跳到指定拍数；`jumpto_s` 按 From 选择秒或毫秒单位。音乐控制开关关闭时不执行。跳转回调在一次播放路径中只执行一次，跳到自身不会死循环；暂停和切换手动倍速保留路径进度，手动定位重新建立后续路径。导出会同步拼接跳转前后的画面与音频。后退跳转后的画面按目标歌曲时间重新求值，不复刻 GameMaker 任意实例的历史状态。音频导出最多 512 个路径片段，过于复杂或过长的路径会明确报错。

2026-09-14 验证：新增 15 项 FX/音乐控制自测及完整 CPU/IO 回归通过；Metal 验证边缘检测、雪花切帧、DF 侧线与冲击波/拖尾。实际音视频导出验证了向前跳转和向后跳转的时长与音频拼接。`Still Shining` 当前解析报告无兼容提示。

### Extendnova 原生演出

`obj_extendnova_gimmick` 已补齐原游戏的 41 项扩展编号表，支持原生后处理、按难度调整的 glitch 强度、24 帧 CG、两套热浪背景、斩击、血条与白色判定区域。音符统一使用普通皮肤，不切换 Extendnova 纯黑皮肤。房间配置包含原始 `FX_glow`、背景 glow、posterise 和 edge 图层，使用原始深度和参数。

**不包含游戏安装包中的 n7 剧情自动跳过补丁。** Viewer、Editor 和视频导出保留完整音乐时间轴，不会因为剧情回调跳到下一段音符。8 段剧情按原音乐时间提供只读字幕与 CG 预览，使用原版剧情框、角色渐变线和独立名牌，按行数调整框的位置、逐字显示正文，并执行原脚本的清空与退场时序；不再每 5 秒循环翻页。不执行游戏对话交互、暂停流程或自动跳转，不播放逐字音效。结尾白色淡出按原脚本时间绘制。

`en_evildawn_hpbar_shake` 在该对象中只注册、初始化，没有对应的读取行为，报告保留此源码说明；不通过伪造抖动来消除提示。顶部连击读数与该对象无关，由 SETTINGS 的 VS UI 分区统一控制。

验证命令：`dotnet run -c Release -- --native-sequence-self-test /path/ENCORE.vsb`。本机 n7 ENCORE 的长音频、扩展编号和完整播放时长验证通过；Metal 检查覆盖 CG 换帧、背景、Glow、斩击、血条与回退一致性。Windows 尚未实机验证。所有新增游戏素材与剧情原文继续保存在非公开的 `Assets/` 内。

### 保存与输出

`SAVE / SAVE AS` 保存新的 `.sgv.json` 及所需的 `.editor.vsm`、`.editor.vsp`、cgmk 配置和 `editor-assets`。原谱面、歌曲与图片不覆盖；工程仍可能引用外部歌曲资源，换机器时需要保留依赖与相对路径。

文字编辑另存到 `<工程名>.editor-texts/`，工程使用相对路径记录每个文字 ID 的字幕文件。**Chart Folder** 会把尚未保存的字幕编辑输出为游戏可读的 `[难度]_text[_tid].txt`；VSM / VSM + config 不包含字幕内容文件，导出预检会明确提示。

| EXPORT 模式 | 用途与范围 |
| --- | --- |
| VSM | 导出演出文本，不包含 VSP 和图片。 |
| VSM + cgmk config | 同时导出演出与窗口配置，不包含 VSP 和图片。 |
| Chart Folder | 创建新歌曲目录，整理当前难度的 VSP、图片等依赖，并生成可重新打开的预览工程。 |
| 视频导出 | 输出 H.264/AAC MP4，固定输出帧率，画面与音频遵循谱面 playspeed，需要 FFmpeg。 |

导出前通过 `REVIEW FILES` 查看目标与提示，再选择 `EXPORT COPY`；目标必须不存在。Chart Folder 不安装游戏扩展，也不保证其他难度的外部依赖全部收齐。视频只导出歌曲场景，不录制虚拟桌面或多个原生窗口。

### 常用快捷键

| 按键 | 操作 |
| --- | --- |
| Tab / Space | 切换 Viewer 与 Editor / 播放暂停 |
| Ctrl/⌘ + O / S | 打开 / 保存 |
| Ctrl/⌘ + Shift + S | 另存为 |
| Ctrl/⌘ + Z / Ctrl/⌘ + Shift + Z | 撤销 / 重做 |
| E / Shift + E | 创建或激活时间标记 / 取消固定 |
| A / B / L | 设置循环起点 / 终点 / 切换循环 |
| D / Delete | 复制选中事件 / 删除 |
| F1 / 长按 W | 离线手册 / 轨道详细说明 |
| Shift + 滚轮 / Ctrl/⌘ + 滚轮 | 平移 / 缩放时间轴 |
| Esc | 取消当前操作或关闭真实窗口预览 |

## 示例

| 路径 | 内容 |
| --- | --- |
| [EditorDemo/demo.sgv.json](Samples/EditorDemo/demo.sgv.json) | 120 BPM 合成节拍器、音符参考、位移/旋转/缩放、重复片段与三个窗口。 |
| [EditorDemo/proxy.sgv.json](Samples/EditorDemo/proxy.sgv.json) | 两份普通 Proxy 绑定与独立裁剪内容；不演示 pause / FCAC / merge。 |
| [ImageObjects162/demo.sgv.json](Samples/ImageObjects162/demo.sgv.json) | 原创测试图案、合成节拍器、XY 路线、旋转缩放及保留 `_` 的 RAW 透明度事件。 |
| [TextObjects/demo.sgv.json](Samples/TextObjects/demo.sgv.json) | 原创文字、移动动画与 `fx_film` 开关，不包含游戏歌曲或音乐。 |
| [ReusableObject/ENCORE.vsc](Samples/ReusableObject/ENCORE.vsc) | 原创 pulse 图形与通用对象定义，演示回调粒子、逐帧表达式和 shader uniform。 |
| [ImageGimmicks](Samples/ImageGimmicks) | 图片声明与程序生成的演示图形。 |
| [FormatSamples](Samples/FormatSamples) | 仅有 VSM 的格式参考；完整播放仍需对应外部歌曲资源。 |

图片示例可按 INITIAL → MOVE 的 START/END → LINK JOINTS → CONTINUE → REPLACE IMAGE → Save As → Chart Folder 依次试用，并检查撤销与重新打开后的结果。`card_side` 的动态透明度保留为 RAW，不应变成固定曲线。

可复用对象示例通过 `gimmick-object.json` 定义行为。复制目录后，同时修改 VSM 的 `!obj` 与 JSON 的 `objectName`；修改回调时，同步 callbacks、callbackConstraints、extraMods 与 VSM。已有通用能力范围内不需要增加专用 C# loader。

FormatSamples 的 FINALE 第 4801 行包含 `327.6.6`；当前解析器读取数字前缀 `327.6` 并给出提示，原生 GameMaker runner 的边界行为仍需实际核对。

`Integrations/ExtCustomGimmick/` 保留原扩展的源文件、原生库、示例和原始文档。适配代码位于 `src/Core/Windows/` 与 `src/Graphics/Windows/`；SDL3 GPU 前端不会直接加载其中的 GameMaker DLL/dylib。游戏侧安装参照 [扩展原始说明](Integrations/ExtCustomGimmick/README.md)。

## 构建应用包

下面的脚本在本机生成包含运行时的应用包，需要完整 Assets。输出包含非公开游戏资源，仅供本地使用；不要将 `dist/` 或其中的 ZIP 上传到公开仓库或公开下载区。

```bash
# macOS：默认当前 CPU；也接受 x64 或 all
bash scripts/publish-mac.sh arm64
# macOS 上交叉构建 Windows；也接受 arm64 或 all
bash scripts/publish-win.sh x64
```

```powershell
# Windows PowerShell；也接受 arm64 或 all
.\scripts\publish-windows.ps1 -Arch x64
```

`all` 分别生成两个架构的包，不生成 Universal 应用。输出位于 `dist/KuroakiGimmick-v0.1.3-<RID>-17.0-<时间戳>/` 及同名 ZIP，日志也保留在 `dist/`。macOS 的 `.app` 与 Assets 并列；Windows 使用单文件 `.exe`，Assets 保持外置。移动本地应用时保留完整目录。

macOS 构建在可用时使用 ad-hoc 签名，不代表经过公证。交叉构建成功不代表目标系统运行通过；指定 FFmpeg 时必须提供目标系统和架构的可执行文件。

## 开发与检查

常用脚本集中在 `scripts/`，检查与文档生成工具放在 `scripts/dev/`，本地游戏资源工具放在 `scripts/assets/`。

```bash
# 恢复锁定依赖、编译，然后执行 CPU/IO 自测
bash scripts/verify.sh
# 在支持 Metal / Direct3D 12 的本机追加 GPU 自测
bash scripts/verify.sh --gpu
# 无需编译的源结构检查，不能替代 C# 或 GPU 测试
python3 scripts/dev/check-source.py
```

```powershell
.\scripts\verify.ps1
.\scripts\verify.ps1 -Gpu
```

统一验证入口覆盖基础、编辑器、参数手册、创作、布局与图片对象自测；任一步失败即停止。GPU 检查需显式开启。专项 `check-*.py` 中部分需要 DLL、外部曲包或历史基线，使用前查看脚本参数；旧版源码契约不等于当前功能验收。

2026-09-12 文字 / film 更新验证：Release 编译通过（0 警告、0 错误）；基础 228 项、编辑器 686 项、参数手册 637 项、创作/导出 48 项、图片对象 123 项以及新增文字/film 26 项自测通过。Metal 的 24 项基础/管线检查及文字画布、film 开关/动画/回退一致性检查通过；SDL 文字操作测试覆盖分组轨道、移动、缩放、旋转、撤销重做、Esc 取消、多行输入和不同窗口尺寸。Windows HLSL 转译已检查，Windows 原生运行未验证。

本次修正了布局自测 `Reject` 辅助方法对 `InvalidDataException` 的漏捕获，继续检查生产代码对非法 VSP 的拒绝行为。旧版整理时的 46 项模拟打包检查见历史说明，不代表后续更新的目标平台发布验证。

Custom 适配更新：新增 21 项自测、283 项布局/图片自测及其余 CPU/IO 回归全部通过；Metal 实测通过星星、原始 cover2 遮罩检查。2026-09-13 移除了 Combo 绘制，GPU 检查验证 `hide_combo` 开关不再改变画面。实际 FFmpeg 导出验证了恒定倍率与中途累乘倍率，视频帧数、音频和输出时长符合速度映射。Windows 原生播放尚未实测。

| 工具 | 用途 |
| --- | --- |
| `scripts/dev/compare-scenes.py` | 调用新旧程序在同一时间输出画面与差异；需要真实 DLL 和曲包。 |
| `scripts/dev/check-publish.py` | 用模拟发布器检查打包布局及失败清理，不证明真实编译成功。 |
| `scripts/dev/build-vsm-reference.py` | 从本地原始文档生成 F1 参数索引；普通构建消费已生成的 Assets。 |
| `scripts/dev/build-vsm-manual.py`、`build-editor-help-atlas.py` | 维护旧手册与字体图集，依赖原始文档和本地字体环境。 |
| `scripts/assets/Dump_*`、`install-note-dump.sh` | 本地提取或导入部分游戏资源；输出不公开。 |

### 命令行诊断与视频

```bash
dotnet run -c Release --no-build -- --help
dotnet run -c Release --no-build -- --inspect /path/ENCORE.vsb --time 184.11
dotnet run -c Release --no-build -- --snapshot /path/ENCORE.vsb \
  --scene --time 184.11 --out frame.ppm --report frame.json --strict
dotnet run -c Release --no-build -- --render /path/project.sgv.json \
  --start 30 --end 45 --fps 60 --width 1920 --out clip.mp4 --strict
```

CPU `--inspect` 不能验证 GPU shader 编译；截图与视频需要可用图形后端。`--strict` 会将相应渲染错误作为失败报告。对象定义、资源来源与兼容性诊断可通过报告检查，完整参数以 `--help` 为准。

### 目录与详细文档

```text
src/            应用入口、核心模型、解析与编辑、渲染、原生接口、UI
tests/          C# 自测与保留契约
scripts/        启动、构建应用包、统一验证入口
  dev/          专项检查、文档生成、画面对比
  assets/       本地游戏资源提取与导入
Samples/        演示工程与格式参考
Integrations/   ExtCustomGimmick 原始扩展与文档
ThirdParty/     第三方依赖声明
docs/           专题手册、架构、验证记录与 README 横幅
  history/      旧版说明、补丁记录与历史清单
Assets/         本地私有资源，不提交
dist/           本机构建产物，不提交
```

[更新日志](CHANGELOG.md) · [架构](docs/ARCHITECTURE.md) · [通用对象定义](docs/OBJECT_DEFINITIONS.md) · [图片对象](docs/IMAGE_OBJECTS_16_2.md) · [布局与图片导入](docs/LAYOUT_IMAGES_16_1.md) · [导出与 E 标记](docs/EXPORT_AND_MARKERS_V0.1.2.md) · [窗口运动](docs/WINDOW_MOVEMENT_V0.1.1.md) · [F1 文档界面](docs/DOCS_UI_PREVIEW9.md) · [历史资料](docs/history/README.md)

本工具不执行任意 GML，也不完整模拟 GameMaker。部分窗口生命周期、动态图像变换和专用演出仍有边界，应结合 SCENE、兼容报告与游戏端实际效果检查。历史文档中的版本限制和验证结果只适用于对应修订。

## 资源说明

**Assets 属于游戏《vivid/stasis》及其相应权利人，不公开。** 游戏图像、音符皮肤、音频、谱面、演出素材及相关导出资源的原始权利归各自权利人所有；本项目不提供这些素材的公开下载或再分发授权。

`.gitignore` 排除整个 `Assets/`、`bin/`、`obj/`、`dist/`、本地资源导出目录与压缩包。资源在本地保留供开发使用，包含它们的构建产物也应保持非公开。忽略规则不能移除已提交的文件；上传前应检查实际待提交内容。

第三方组件及字体的原始声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 和 `ThirdParty/`。
