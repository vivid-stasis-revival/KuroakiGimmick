> 历史文档：以下内容来自输入源码及其既有修订，不能作为 r19 的编译、运行或画面验收结果。当前状态见 [VALIDATION_R19.md](VALIDATION_R19.md)，当前结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

# src-r7-more-gimmicks

本轮依据用户提供的 decompcode(2).zip、Custom Gimmicks v1.12.7 和已有歌曲资源扩展实际绘制。截图里五项逐一处理；没有收到截图对应的新 VSM，所以不能把合成测试说成该歌曲的整曲验证。

## 截图项目

| 项目 | r7 行为 | 依据与限制 |
|---|---|---|
| Unsupported VSM directive: 0 | `!0:…` 等非空键保留到 metadata，检查报告可见；不再当未知效果报警 | read_mods 把 `!key:value` 写入 data；未收到截图原始行，不能推断其内容 |
| pburstleft / pburstright | 左右慢速粒子系统，真正创建并绘制原始粒子 | 原 mod_setup 的 x=130/190、y=-50..230、pburstspeed、两颗一组，4 秒淡出，跟随全局 particle power；越界永久消失 |
| ditortedBG_alp | 白色底层显露曲绘，曲绘乘色后再绘制轨道/文字 | InitDistortBG、o_csm_distortbg_handler、jacket renderer；`ENABLE_DISTORT_BG` 控制 |
| fx_chroma_distort | 已有原 shader 与参数绑定；读取原房间 FX_chroma 配置后工作 | 原 cc Step 设置 `g_Distort2Amount`。缺少 filter 类型、其余参数、贴图或不支持的实际 filter 时，明确报告并跳过该层 |

## 同时增加

| 控制 | 实际绘制 |
|---|---|
| ditortedBG_col_rgb | 曲绘乘色层 RGB tint；保持源代码中的拼写 |
| BG_blurRadius | 背景专用 32 样本 large blur，使用原噪声图，轨道/note/文字之后再画 |
| BG_ditortAmount / BG_ditortScale | 原 heat haze 两组位移；需要外部 LBG 参数，scale 必须大于 0 |
| fx_red / fx_red_intensity / recolor | 原 colourise 按亮度着色，保留白色高光；recolor 确定性选色，便于跳转重现 |
| fx_hue_hue / fx_hue_saturation | 原 YIQ hue shader |
| fx_colorise_intensity / fx_colorise_col_rgb / fx_colorise_col_alpha | 自制 tintLayer 的原 colourise，按源代码设置 alpha；不额外发明透明度乘法 |
| fx_contrast | 读取 FX_contrast 的实际 filter 和 tint，强度为 `1 - mod`；需要原房间配置 |
| xoffsetind0..6 / yoffsetind0..6 | 指定轨道的 note 坐标，与图片时间坐标共用原公式 |
| boost_timeind0..6 / boost_distanceind0..6 | 逐轨 boost；保留原码仅在全局 boost_distance 非零时启用的门控 |
| prsy / rotdir | proxy 纵向斜切与旋转方向倍率 |
| twx/twy/twa/twr1..4 | 原 custom shader 四组 twist，振幅乘 0.6，默认半径 0.4 |
| sina/sinp/sino、cosa/coso、tana/tanp/tano | 原 shader 正弦、余弦、正切波形。原 Draw 将 cosm.y 绑定到 sinp，本版保留；不把未用的 cosp 标为支持 |

红色/hue/tint 尊重 `ENABLE_NON_BASE_FX`。白闪仍为白色。附加房间 FX 配置时保留可见、启用状态及 depth；自制 tintLayer 按 depth=-2400 插入这些 FX 层。没有房间配置时 red/hue 使用源 shader 公式，但它们相对其他层的顺序尚未确认，会提示。

水下效果现在使用原始 `_filter_underwater_noise_sprite`；仍使用通用回退层参数，不能宣称已取得完整原房间配置。新增三张通用 FX 噪声图共 191,675 字节，未加入 ASTELLION 歌曲、封面或专属背景。

## 仍有边界

- 缺少完整 gameplay 房间配置；FX_chroma / FX_contrast 不根据层名臆测类型。支持的配置格式见 [FX_RESOURCES.md](FX_RESOURCES.md)。
- LBG 是自制初始化时创建的层，静态房间导出不能取得它的运行时 FX 默认值。需要运行时导出的真实参数才能完整匹配 heat haze。
- 房间 FX 动画从歌曲零点计时；原游戏可能自进入房间时开始，相位不能保证相同。
- 当前按 depth 排序的是已适配的房间 FX 与 tintLayer；没有重建所有 GameMaker 对象/图层的绝对 depth 合成。原有 glow、水下、posterize 管线亦不等价于整个房间。
- 独立粒子 glow 缺少 shader_optimized_glow，仍未绘制。全局 glow 的 optimized kernel 仍用 Disk Glow 兼容；uhnoise 的 sp_noise2 图像仍缺失。
- changeskin、VSV warp、音乐跳转、3D/perspective proxy、特殊对象、真实窗口移动仍不完整或未实现。r7 当时遗漏了 codepatches.json 对 notealpind 的 Draw 注入，r8 已修正并实现；cosp 仍没有找到实际绑定。
- 固定粒子随机序列用于预览/导出重现，不等同于原游戏某次运行的随机排布。side burst 按 GML repeat 的舍入次数，每组两颗；极大数量有资源上限。plaudite 即时 burst 的 2 秒寿命不变，重复分支仍未实现。

## 样本结果

Weavers 全资源：4,907 事件、1,936 音符、916 图、124 文本轨道 / 340 cue，174 BPM；无解析 error。原来未支持的扭曲背景显示已绘制；fx_contrast 缺房间配置会给资源提示。327.6.6 仍按前缀 327.6 读取并保留 notice。

Looping 只有 VMV 缓存。临时提取的 2,155 条事件可载入，63 次即时 burst 共 3,150 颗；fx_red 现在有绘制。changeskin 仍未实现；缺真实谱面、VSP、图片和封面，不能验证完整 Looping 演出。该临时提取输入没有打包进程序。

原逻辑入口：`gml_GlobalScript_read_mods`、`gml_GlobalScript_mod_setup`、`o_songgameplay_particlesystem`、custom `InitDistortBG`、`csmNoteMods`、cc Step 与 Draw_74。原 shader 文件及通用噪声来源散列记录在 Assets/GameFX/manifest.json；所有未知 mod 继续报告。
