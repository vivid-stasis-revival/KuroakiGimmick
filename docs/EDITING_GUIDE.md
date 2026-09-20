# 编辑与导出指南

[返回 README](../README.md) · [渲染与兼容行为](RENDERING_BEHAVIOR.md)

## 打开与编排

使用 `OPEN CHART / VSM` 打开谱面、演出或工程，也可拖入歌曲目录；通过 `+ FILES / RESOURCES` 附加图片声明、音频、曲绘和 FX 配置。同难度 VSM/VSP 优先，缺失时可使用 GLOBAL 文件。图片路径相对谱面或声明文件目录解析。

按 Tab 切换 Viewer / Editor。选择音符作为时间参考，在轨道上双击或使用 `+ ADD GIMMICK` 添加事件；拖动片段调整时间，拖动右侧手柄调整持续时间。E 创建时间书签，默认添加位置优先使用活动标记，其次是选中音符和播放游标。

VSC 音符时间以毫秒计，VSM 事件时间以拍计；VSM duration 按事件起点 BPM 转为秒。窗口事件的 `t`、`easeDur`、`dur` 使用秒，跨 BPM 编辑时不要混用单位。未知行、重复语法和动态值 `_` 按原有保留规则处理。

重复事件支持 `起始拍(:终止拍:间隔)`，也兼容原有的 `start:end:step`。例如 `-10.01(:1.9:0.02),0,linear,0.08,1,velocity,-1` 从 **-10.01 拍**开始，每 0.02 拍触发一次，直到不超过 1.9 拍；本例最后一次为 1.89 拍，共 596 次。负数、小数及科学记数法均可使用。编辑器将一整行循环保留为一个片段，编辑和保存继续保留括号写法。间隔须为正数、终点不得早于起点，总展开量最多一百万事件；非法括号语法会报明确错误，不再误当作 GML 数字前缀截断。

右键循环片段选择 **SPLIT LOOP**，可拆成每次触发的独立事件；多选后右键 **MERGE INTO LOOP**，可合成括号形式的循环。合并要求 mod、Proxy、时长、缓动、From/To 完全相同，按源文件顺序等间隔递增，且所选源行之间没有其它事件。已有循环也可继续合并。两种操作都支持一步撤销／重做，保留动态值 `_`、注释与 MPF 段；不符合条件时会显示拒绝原因。

## 图片对象

将 PNG/JPEG/BMP/TGA 拖入 Editor，或从 IMAGES 选择已有实例。IMAGE CANVAS 使用 **320 × 180 的图片本地坐标**；INITIAL 编辑基础姿态，KEY HERE 写入定时姿态，START / END 修改动画端点，CONTINUE 续接路径。LINK JOINTS 仅联动唯一且数值相接的相邻动画。

SCENE 用于检查实际场景效果。图片画布不反算 Proxy、后处理或屏幕扭曲；动态值、冲突事件与不支持的变换仍需使用原始事件面板。完整操作见 [图片对象手册](IMAGE_OBJECTS_16_2.md)。

CustomGimmick 的 SCENE 按原版先合成完整场景，再让 Proxy 裁剪、变换和复制。图片层级控制遮挡顺序，不会因跨过 `-200` 或 `1000` 而改变是否进入 Proxy；低层级图片、轨道与字幕可以一同被采样。详情及验证范围见 [Custom 图片与循环编辑修复](CUSTOM_PROXY_AND_LOOPS.md)。

## 文字对象与字幕

Editor 左侧选择 **TEXT → + TEXT**，直接输入内容；可粘贴多行文字，也可用 `{n}` 换行。需要切换 Custom 对象或启用字幕时，界面会先提示。已有 `[难度]_text.txt` 和 `[难度]_text_[tid].txt` 会显示为文字对象；旧式单文件字幕保留其优先级，不会在添加时自动转换格式。

选中文字后进入 **TEXT CANVAS**：拖动文字移动，拖角等比缩放，拖顶部手柄旋转；Shift 旋转吸附到 15°，方向键移动 1 像素（Shift 为 10），Ctrl/⌘ + 滚轮缩放视图。一个拖动对应一次撤销，Esc 或窗口失焦取消。画布复用场景字体绘制，使用本地 320×180 坐标；SCENE 查看实际后处理效果。

右侧 **INITIAL / KEY HERE** 选择写入时间，Content 编辑该拍字幕，CLEAR TEXT HERE 添加空字幕作为结束点。可修改位置、大小、旋转、透明度、颜色、对齐、行距与换行宽度；向下滚动可添加 MOVE / FADE IN / FADE OUT 动画。选择动画后使用 **EDIT START / EDIT END** 配合字段或画布调整端点。时间轴的 TXT 轨与图片对象采用一致的片段样式，仅显示姿态和动画，不绘制字幕内容蓝条；字幕内容与时间点在文字面板中编辑。展开箭头可操作原始事件轨道，支持原有拖动时间和持续时间操作。

动态值、重复行、同拍多次写入、歧义的尾缀 `b` 名称与冲突动画不会被直接变换重写，应使用原始事件面板。现有旧式字幕的颜色继续遵循 `col_convertion`，具名字幕颜色使用 RGB。字幕内容和视觉参数分别保存，内容修改不会改变其他时间点。

## 保存与输出

`SAVE / SAVE AS` 保存新的 `.sgv.json` 及所需的 `.editor.vsm`、`.editor.vsp`、cgmk 配置和 `editor-assets`。原谱面、歌曲与图片不覆盖；工程仍可能引用外部歌曲资源，换机器时需要保留依赖与相对路径。

文字编辑另存到 `<工程名>.editor-texts/`，工程使用相对路径记录每个文字 ID 的字幕文件。**Chart Folder** 会把尚未保存的字幕编辑输出为游戏可读的 `[难度]_text[_tid].txt`；VSM / VSM + config 不包含字幕内容文件，导出预检会明确提示。

| EXPORT 模式 | 用途与范围 |
| --- | --- |
| VSM | 导出演出文本，不包含 VSP 和图片。 |
| VSM + cgmk config | 同时导出演出与窗口配置，不包含 VSP 和图片。 |
| Chart Folder | 创建新歌曲目录，整理当前难度的 VSP、图片等依赖，并生成可重新打开的预览工程。 |
| 视频导出 | 输出 H.264/AAC MP4，固定输出帧率，画面与音频遵循谱面 playspeed，需要 FFmpeg。 |

导出前通过 `REVIEW FILES` 查看目标与提示，再选择 `EXPORT COPY`；目标必须不存在。Chart Folder 不安装游戏扩展，也不保证其他难度的外部依赖全部收齐。视频只导出歌曲场景，不录制虚拟桌面或多个原生窗口。

## 常用快捷键

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

## 本地示例与扩展

以下目录仅在配有对应本地资源的工作区中可用，未包含在公开仓库中。路径相对项目根目录。启动脚本不传工程路径时会尝试打开 `Samples/EditorDemo/demo.sgv.json`；请在没有该示例时显式传入自己的工程。

| 路径 | 内容 |
| --- | --- |
| `Samples/EditorDemo/demo.sgv.json` | 120 BPM 合成节拍器、音符参考、位移/旋转/缩放、重复片段与三个窗口。 |
| `Samples/EditorDemo/proxy.sgv.json` | 两份普通 Proxy 绑定与独立裁剪内容；不演示 pause / FCAC / merge。 |
| `Samples/ImageObjects162/demo.sgv.json` | 原创测试图案、合成节拍器、XY 路线、旋转缩放及保留 `_` 的 RAW 透明度事件。 |
| `Samples/TextObjects/demo.sgv.json` | 原创文字、移动动画与 `fx_film` 开关，不包含游戏歌曲或音乐。 |
| `Samples/ReusableObject/ENCORE.vsc` | 原创 pulse 图形与通用对象定义，演示回调粒子、逐帧表达式和 shader uniform。 |
| `Samples/ImageGimmicks` | 图片声明与程序生成的演示图形。 |
| `Samples/FormatSamples` | 仅有 VSM 的格式参考；完整播放仍需对应外部歌曲资源。 |

图片示例可按 INITIAL → MOVE 的 START/END → LINK JOINTS → CONTINUE → REPLACE IMAGE → Save As → Chart Folder 依次试用，并检查撤销与重新打开后的结果。`card_side` 的动态透明度保留为 RAW，不应变成固定曲线。

可复用对象示例通过 `gimmick-object.json` 定义行为。复制目录后，同时修改 VSM 的 `!obj` 与 JSON 的 `objectName`；修改回调时，同步 callbacks、callbackConstraints、extraMods 与 VSM。已有通用能力范围内不需要增加专用 C# loader。

FormatSamples 的 FINALE 第 4801 行包含 `327.6.6`；当前解析器读取数字前缀 `327.6` 并给出提示，原生 GameMaker runner 的边界行为仍需实际核对。

`Integrations/ExtCustomGimmick/` 保留原扩展的源文件、原生库、示例和原始文档。适配代码位于 `src/Core/Windows/` 与 `src/Graphics/Windows/`；SDL3 GPU 前端不会直接加载其中的 GameMaker DLL/dylib。游戏侧安装参照本地 `Integrations/ExtCustomGimmick/README.md`。
