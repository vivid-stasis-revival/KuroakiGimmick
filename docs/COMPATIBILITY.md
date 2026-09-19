> 历史文档：以下内容来自输入源码及其既有修订，不能作为 r19 的编译、运行或画面验收结果。当前状态见 [VALIDATION_R19.md](VALIDATION_R19.md)，当前结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

# v0.1.0 兼容范围

## 已实现

- VSC1 `.vsb`：音符、额外字段、对象、proxy 数量、内嵌 mod、per-frame 声明。签名尾部不参与渲染，不执行谱面校验。
- 文本 `.vsc`：毫秒时间、普通/hold/bumper/地雷/带判定 bumper，type 3 的 BPM 元数据；坏行报告后继续。
- 文本 `.vsm`：`!obj`、`!proxies`、任意非空元数据键保留、七列事件、拍数区间展开、31 种 easing、`_` / 573613、注释、行号诊断。负时长按即时赋值处理。
- BPM 变化、offset、随机跳转、逐帧预览、项目保存、曲长以完整音频为准。
- 常见轨道运动、proxy 位移/缩放/旋转/斜切/波形/crop、note alpha、scroll、wave、boost、beat，以及通用灰度/桶形/鱼眼/水平扭曲/暗角/色差。
- 外部 ASTELLION VSB 的音符、内嵌 mod ID、174 BPM 和谱面时间轴；读取曲包 `astellion/manifest.json` 后支持水下背景、碎屑、sides、astbars、两层轨道装饰及原始整屏后处理。**没有内置歌曲、背景贴图或示例谱面。**
- Custom Gimmicks v1.12.7 `.vsp`：静态图、横向多帧图，13 类 img* 参数，初始尺寸、lane 时间偏移、RGB、alpha、中心变换、两级优先级。不同 ID 可共用贴图。图片按需解码，GPU 缓存 128 MiB，单图解码上限 64 MiB；最多 4096 声明。
- Custom Gimmicks 文本：旧式单文件和新版多 ID 文本，按拍数显示，位置/附加位置/缩放/旋转/alpha/RGB/对齐/换行，尊重 ENABLE_TEXT。
- 共享原生对象定义：按对象名加载精灵回调、GUI 覆盖层、post shader 和额外二进制 mod 映射；当前提供 `obj_scarletdeath_gimmick` 的资源与配置，支持同类型的任意谱面。参见 [NATIVE_GIMMICKS.md](NATIVE_GIMMICKS.md)。
- 使用 custom 主 shader 处理鱼眼、桶形、噪声、横纵扭曲、两类色差与 bloom。fx_glow、fx_underwater、fx_posterize(_vis) 有兼容实现，精确差异见下。
- 原始 sp_laneOverlay / sp_holdnote_overlay 贴图、独立 bgalph / holdoverlayalpha；普通显示区域 y=0..164，保留源图完整 180 行供 proxy crop。
- 自制歌曲的 pt_diamonddust 四帧原始粒子图，以及源代码的生成、运动、淡出和出界销毁；彩色 slash。
- 预览和视频共用 SceneRenderer，图片/歌词动画依据歌曲时间，不会因为编码耗时而变速。

## 当前差异

这不是完整的 GameMaker 运行环境。图形成功绘制不等于与原游戏逐像素一致。

- 音符使用用户提供的 vsnotes PNG 皮肤（普通、hold、bumper、地雷、带判定 bumper）；原 HUD 需加载公共 UI 导出包；没有玩法判定、游玩输入、3D 音符旋转等。
- ASTELLION 游玩专属素材从歌曲目录加载，逐项验证 manifest、图片尺寸、相对路径、噪声和 shader；缺失项单独报告。粒子随机序列及新版公共粒子对象未逐版确认，使用固定种子和旧版运动/两秒淡出。剧情转场与语音未接入，直接从歌曲零点开始。详见 [ASTELLION.md](ASTELLION.md)。
- 尚无共享配置的原生对象，如 `obj_stargazers_gimmick`，只保留可执行的通用参数，不会自动翻译对象 GML、生成其原版星体。已有配置的对象通过已实现的通用绘制能力扩展；未知参数和 per-frame 函数仍报告。
- Custom Gimmicks 的 VSV 滚速 warp、换 note 皮肤、部分扩展滤镜、音乐跳转和真实窗口移动尚未实现；未支持事件可检查但不绘制。不是整个 v1.12.7 包全兼容。
- 公共 GameUI 内含原 Default / Monaco 字体图集；已修正 Monaco CJK 图集的 AscenderOffset。资源缺失才回退 Fusion Pixel，并报告。
- 普通游玩房间 Glow 使用当前 Mac 原版 Disk Glow 与实际两层参数；underwater 使用实际 Create 参数。其他房间若缺少这些层，仍报告缺失。uhnoise 仍使用替代噪声并报告。
- 粒子贴图为原始 pt_diamonddust，9×9，原点 (4,4)，不再用方块近似。High 默认设置每两帧生成两颗同位置、独立速度/帧号粒子，寿命 120/60 秒；跟随 particlexpower / particleypower / rainbow / particle_alpha，越界永久销毁。以 60Hz 歌曲时间复现，随机种子固定以保证跳转/导出一致；与原游戏某次运行的随机排布及变速时的墙钟行为不同。普通房间的背景粒子 glow 已按实际 depth 500 绘制；其他缺失 profile 继续报告。
- 轨道使用原图的默认 frame 0；未加入游戏内其他轨道皮肤选择、按键亮起及命中反馈。已结束的音符移除，长条/旋转音符在局部 y=165 处裁剪后再进入 proxy 与后处理。
- 图片按 priority 插入背景、轨道、note、HUD 的绘制层，高优先级图片可覆盖轨道与 HUD，并保留图片自身 proxy 变换。不是任意 GML 对象深度的通用执行器。
- 图片 `imgytime` 按提供代码使用 `gmlNoteModsY` 返回坐标。`imgscaleytime` 原码是 `posEnd - posO / height`，保留这个运算顺序，没有改写成 `(posEnd - posO) / height`。同一图片层内 time 变量沿用原 drawer 的循环状态。没有 VSV warp 时，常见时间偏移可用；发现 VSV 会提示差异。
- 同一参数的重叠 tween 使用“最新开始的事件优先”。与 GameMaker 逐帧活动 tween 队列，在短 tween 结束后较旧长 tween 继续运行的情况下可能不同。
- `.vsm` 的 `_` from 从事件开始时的当前值取样；to 为 `_` 也按当前值处理，是预览器的容错扩展。
- 用特殊自制扩展导出的二进制自定义 mod ID，若没有匹配对象映射，不能凭 ID 猜出名字；优先附加文本 `.vsm`。
- ASTELLION 强制 effect BPM 174，Stargazers auto profile 初始 BPM 186；其他歌曲优先读取 VSC/VSB 的 BPM 表和 info.json 的 bpm_display。缺失这些数据时默认 120，可手动调整初始 BPM。

## 收到的样本

| 文件 | 实际结果 |
|---|---|
| 外部 ASTELLION / 四种难度 VSB | 每份 1259 个 mod、796 次 sides、128 次 astbars；7 组外部精灵和原始 shader 接入；保留粒子一致性提示 |
| _ENCORE_stargazers.vsm | 249 个事件，语法有效；星体专用对象待适配 |
| FINALE.vsm | 4907 个事件全部载入；第 4801 行 `327.6.6` 按数值前缀 327.6 读取并提示 |
| 完整 Weavers 文件夹 | 自动加载 FINALE.vsc、FINALE.vsm、FINALE.vsp、124 份文本及 music.ogg；1,936 个音符、4,907 个有效事件、916 张图，174 BPM、292.41381 秒 |
| FINALE_text_46.txt | 第 3 行只有 `564.875`，没有逗号/内容；按原加载器跳过并提示 |

原始样本没有被自动改写。REPORT 中的“无未知事件”表示已识别声明，不表示所有绘制细节与游戏完全一致。

参考实现来自用户提供的 Custom Gimmicks v1.12.7：`o_sprite_drawer_manager`、`o_csm_sprite_drawer`、`csmNoteMods`、`o_text_displayer_ext`、custom shader 与文档。文本 API 参照 [GameMaker draw_text_ext_transformed](https://manual.gamemaker.io/lts/en/GameMaker_Language/GML_Reference/Drawing/Text/draw_text_ext_transformed.htm)。

VSM 数值兼容：原读取器使用 GML real()。参考并实测 [官方 HTML5 real() 实现](https://github.com/YoYoGames/GameMaker-HTML5/blob/develop/scripts/functions/Function_String.js)，支持读取数值前缀，因此 `327.6.6` 得到 327.6。后缀忽略会生成 notice；没有数值前缀、非有限值等仍报告错误。冒号 `start:end:step` 才是此读取器的区间语法。Mac 原生 runner 的对应边界行为未直接验证。

## r6 增补

本地 jacket 自动关联、custom / plaudite 换曲绘、按当前位置的曲绘逐像素乘色、df_whitebg 与 plaudite_pburst 的即时分支已实现。plaudite 内置歌曲封面需外部提供；重复爆发分支仍报告未实现。Looping 与 Weavers 的实际兼容状态及原码依据见 [LOOPING_WEAVERS_R6.md](LOOPING_WEAVERS_R6.md)。

## r7 增补

侧向粒子、扭曲背景显示/染色/模糊、red/hue/colourise、逐轨 note 位移/boost、proxy prsy/rotdir、twist/波形已接入实际渲染。chroma/contrast 与背景 heat haze 需外部原房间配置；不等同于无条件全兼容。详见 [GIMMICKS_R7.md](GIMMICKS_R7.md) 与 [FX_RESOURCES.md](FX_RESOURCES.md)。

## r8 增补

原 Mac 游戏 7 份 room FX 已接入，支持 colour balance，可切换房间并保存。`notealpind0..6` 已根据 codepatches.json 补齐。AUTO 的自制房间选择仍是明确标记的暂用配置，六个隐藏 chroma 不会被强行启用。当前边界与勘误见 [ROOM_FX_R8.md](ROOM_FX_R8.md)。

## r13 增补

Scarlet Beat 实际完整曲包 0 Report；普通房间动态 Create 与两层原 Glow 已接入。原静态文件中隐藏的 chroma 在该房间 Create 中会显式显示，旧版本对普通房间隐藏状态的推断已修正。其他歌曲未宣称全兼容，详见 [SCARLET_BEAT_R13.md](SCARLET_BEAT_R13.md)。
