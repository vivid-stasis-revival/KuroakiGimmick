> 历史文档：以下内容来自输入源码及其既有修订，不能作为 r19 的编译、运行或画面验收结果。当前状态见 [VALIDATION_R19.md](VALIDATION_R19.md)，当前结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

# Looping / Weavers 兼容核对 — src-r6-jacket-multiply

核对材料：用户提供的两份 2026-09-08 兼容报告、looping.vmv、weavers.vsm、完整 Weavers 文件夹、decompcode(2).zip、Custom Gimmicks v1.12.7。报告中的事件数与资源数是不同口径，不能用“全部载入”代替“效果完全一致”。

## 本次已改

- `Graphics/SceneRenderer.cs` 的 `DrawJacket` 在粒子之后、图片/轨道/文字之前，以 OpenGL `DST_COLOR, ZERO` 实现 GML `bm_dest_color, bm_zero`。结果是粒子每个像素乘以曲绘在该屏幕位置的像素，随运动实时换色；不做平均取色或出生时定色。
- 自制模式按原 `o_csm_jacket_Draw_0` 拉伸至 `(0,0,320,180)`，nearest 采样；普通 jacket overlay 按 `o_gameplay_jacketoverlay_Draw_0` 拉伸至 `(-50,-50,420,268)`，linear 采样。当前常规粒子生成器仍以 `obj_custom_gimmick` 为实现对象，其他特殊歌曲对象的独有粒子未自动移植。
- `Core/JacketAssets.cs` 自动查找本地 jacket.jpg/png；按配置读取 custom / plaudite 模式。换曲绘使用原 Step 的 `ceil(value) mod length`，负数选最后一张。重新载入会清掉旧纹理。
- `plaudite_pburst` 的即时分支：v1=0，v2 为 spawn 调用次数。每次调用按 High 设置生成两颗原始 pt_diamonddust，x 随机、y=-10、横速 0、纵速随机 0..0.75×pburstspeed。原 `o_pt_diamonddust_Step_0` 每秒 alpha 减 0.5，虽然 callback 赋 lifetime=240，该 Step 不读取此字段。爆发不受普通粒子的 particle_alpha / particlepower 控制。重复模式 v1≠0 尚未实现，会报告。
- `df_whitebg` 按 `ENABLE_DF_GRID_AND_SIDELINE` 控制绘制白色背景层，不受 UI 红色主题影响。
- 窗口、`--version` 和兼容报告显示 `src-r6-jacket-multiply`。源码与运行包使用同一份实现。

## Looping（第一份报告）

原报告：821 个可游玩音符、2 个 BPM 元数据记录；2,155 个 mod；29 个图片声明、27 张纹理；162 BPM；音频约 133.92 秒。214 条诊断中没有 error，206 条是数值后缀忽略提示。

| 项目 | 核对结果 / r6 状态 |
|---|---|
| df_whitebg，1,011 条 | VMV 中全部 from=0、to=0，本样本没有白幕可见变化；绘制功能已实现 |
| plaudite_jacket，64 条 | 切换逻辑已实现；涉及索引 1..7、10、11。1..7 和 10 是游戏内置的其他歌曲封面，需用户提供外部原图 |
| pburstspeed，1 条 | 在 beat 104 设为 25，已接入 |
| plaudite_pburst，63 条 | 全是 v1=0 / v2=25；已验证生成 3,150 颗爆发粒子，保留源运动与淡出 |
| changeskin，61 条 | 在皮肤 0、1、3 之间切换。未实现；需要原 stopmotion / stargazers note 皮肤及 ENABLE_SKIN_CHANGE 配置确认。当前保持用户提供的 vsnotes |
| fx_red，29 条 | 红色滤镜开关。尚未实现，缺对应房间 FX_red 的实际配置，不能用 UI 红色覆盖冒充 |
| 独立粒子 glow | 尚缺原 shader_optimized_glow 和对应房间配置 |
| 音频尾部警告 | 原报告已经提示有事件超出音频尾部；本次没有用 VMV 猜测或修短歌曲 |

本次只有 Looping 的 VMV 缓存和报告，没有完整 Looping 文件夹。以缓存中的已展开事件重建**临时验证输入**，2,155 条均载入且生成 3,150 颗爆发粒子；这不等于已验证其原始 VSM 语法、图片、配置、音频和整曲画面。

`looping.vmv` 是 INI 格式的 mod 统计缓存，含 `[mods] weight/total/list`。原 `gml_GlobalScript_LoadSongData.gml` 的 `LoadSongModValue` 读取其中 weight。它不是完整谱面，也不宜称为“谱面校验文件”；预览器仍不把 VMV 当成 VSB/VSC/VSM 打开。

## Weavers（第二份报告）

上传的 weavers.vsm 与已有完整文件夹中的 FINALE.vsm 字节一致。本次用完整文件夹重新载入，得到 1,936 个可游玩音符、4,907 条 mod、916 个图片声明/纹理、124 个文本轨道与 340 个 cue；174 BPM、292.41381 秒。自动读到本地 jacket.jpg，模式为 custom。没有解析 error。

| 项目 | 核对结果 / r6 状态 |
|---|---|
| 白色背景粒子 | 原图与运动原已接入；此前漏掉 jacket 乘色，本次已修复。4 秒场景已实际绘制 |
| `327.6.6` | 仍按数值前缀 327.6 载入，提示被忽略的后缀，事件没有丢失 |
| FINALE_text_46.txt 第 3 行 | 只有拍数、没有逗号/内容；按源加载器跳过并提示 |
| `ditortedBG_alp`，1 条 | 本样本设为 0，不产生可见扭曲背景；启用非零时的绘制尚未实现 |
| `fx_contrast`，2 条 | 尚未实现。原 cc 设置 FX_contrast 的 `g_Intensity = 1-mod_fx_contrast`；导出包中名为 contrast 的通用 shader 使用不同 uniform，不能只凭名字当成同一个 FX |
| 全局 glow / 水下 / 噪声 | 仍保留既有兼容实现和提示；optimized kernel、原噪声图及具体房间参数缺失 |
| 字体 / 独立粒子 glow | 字体使用已说明的替代字体；独立粒子 glow 尚未绘制 |

## 外部封面怎么放

普通本地曲绘放在谱面目录的 `jacket.jpg` 或 `jacket.png`。也可通过 `+ FILES / JACKET` 附加图片，或命令行 `--jacket /path/jacket.jpg`，保存工程会记录相对路径。

custom 模式在 cgmk_config.json（或难度专属配置）设 `"JACKET_MANAGE_MODE":"custom"`。默认索引 0 为本地曲绘；额外图片使用原格式 `jacket[1].png`、`jacket[2].jpg`。数组长度按最高索引+1，未提供的中间索引不会自动套用别的封面。

plaudite 模式按原配置默认值启用。内置封面在查看器里**只从外部加载**：可放在谱面目录的 `Jackets/` 子目录或谱面目录本身，以以下资源名加 .png/.jpg 命名。索引 11 为当前歌曲的 jacket。

| 索引 | 文件资源名 |
|---|---|
| 0 | song_transparent |
| 1 | song_pyromania |
| 2 | song_valor |
| 3 | song_unraveling |
| 4 | song_supernova |
| 5 | song_libertia |
| 6 | song_stopmotion |
| 7 | song_convergence |
| 8 | song_red |
| 9 | song_plaudite |
| 10 | song_astellion |
| 11 | 本曲 jacket.jpg/png |

缺少曲绘会在 REPORT 中列明对应资源，那个时段省略乘色层，不能宣称白色粒子就是原效果。包中不包含上述歌曲封面或其他 ASTELLION 歌曲素材。

## 原代码定位

- decompcode(2).zip：`gml_Object_o_gameplay_jacketoverlay_Draw_0.gml`、`gml_Object_o_pt_diamonddust_Step_0.gml`、`gml_GlobalScript_LoadSongData.gml`、`gml_Object_cc_Step_1.gml`。
- Custom Gimmicks v1.12.7：`o_csm_jacket` 的 Create/Step/Draw；`obj_custom_gimmick_Create_0` 中 InitCSMJacket / InitMisc / InitSkinChange；`o_csm_df_handler_Draw_0`。

这些源文件用于对照，完整反编译包和歌曲素材没有重新塞进发行包。详细运行记录见 VALIDATION.md。
