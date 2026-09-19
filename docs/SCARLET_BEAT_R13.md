> 历史文档：以下内容来自输入源码及其既有修订，不能作为 r19 的编译、运行或画面验收结果。当前状态见 [VALIDATION_R19.md](VALIDATION_R19.md)，当前结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

# Scarlet Beat / src-r13-original-glow

针对用户提供的 xinghongsiwang / ENCORE，当前兼容报告 **0 Error、0 Notice**。保留 13,238 条效果、209 个可玩音符、85 条文本和 16 次棋盘刷新；没有删减事件来清空报告。完整曲包仍由用户提供，不随应用发布。

## 这次补齐的证据

来自 `GimmickExtras_20260908_231856.wFD6EZ.zip` 的实际 Mac 游戏代码与房间定义。公共资源位于程序旁 `Assets/GimmickExtras`，最小原始证据保存在其 `evidence` 子目录，哈希见 `runtime-manifest.json`。

| 项目 | 原游戏行为 / 实现 |
|---|---|
| 房间选择 | `start_song` 按 `song_get_info(..., "name", difficulty)` 的完整歌曲名匹配；本曲 `Scarlet Death(DouBaoAI Edit)` 进入 default 的 `scene_gameplay`。不是按封面模式、formatted_name 或名字包含 Scarlet Death 来选。显式 ROOM FX / 外部 profile 仍优先。 |
| 动态 underwater | 房间 Create 创建 `FX_underwater`，depth -1400；两组速度 .01/.03、尺度 [20,2]/[100,10]、RGB spread 1。强度由 custom Step 的 `fx_underwater` 同时控制两组 amount。 |
| chroma | 静态房间文件为隐藏，但这个房间的 Create 明确把它显示出来；使用静态文件而漏执行 Create 会得出错误结论。 |
| 背景对比度 | 原 `Effect_1` depth 600，contrast 1.6、brightness 1，使用已导出的公共 `_filter_contrast_shader`，在背景 glow 之前执行。 |
| 背景 glow | `glow` depth 500，radius 256、quality 5、gamma 0，`fx_particleglow` 默认 .5；处理曲绘乘色与背景之后的画面，不包含其后绘制的轨道、音符、HUD。 |
| 整屏 glow | `FX_glow` depth -1700，radius 141、quality 5、gamma 0，强度由 `fx_glow` 控制；遵守 underwater / chroma / hue / tint 的原深度顺序。 |
| 模糊内核 | 实际 Mac 游戏就用 `_effect_glow_shader` 的 Disk Glow，36 次 Fibonacci 圆盘采样。按这次实际 `_effect_glow_script` 计算 `mult = radius^(1/quality)`，每遍半径乘 `-mult`，采样上一遍结果，并逐遍 MAX 合成；不再称其为替代 optimized kernel。 |

`scene_gameplay.runtime.fx.json` 合并静态定义与 Create 的有效参数。当前附带 profile 聚焦普通游玩房间；其他专用房间保留原 profile，不凭名称伪造其动态行为。glow 实现对不透明整屏缓冲的 alpha 合成与源码一致；不是通用 GameMaker 的单图层透明 FX 执行器。

## 曲尾和空操作

最后一条展开后的 xoffset 位于 beat 110→111，结束约 60.54545 秒；音乐长 60.02501 秒。事件完整保留并记入报告 `eventTimeline.pastAudioTail`，正常曲尾不再产生兼容警告。显式导出 `--end` 可覆盖这一小段，音轨使用静音补齐。

第 6 行裸 `fx_colorise` 在提供的当前 custom 注册表中没有注册，按原规则不执行；记录在 `sourceNoOps`。实际颜色参数 `fx_colorise_col_rgb`、`fx_colorise_col_alpha`、`fx_colorise_intensity` 照常渲染。其他未知 mod 和缺失资源仍产生报告。

## 验证及边界

- 实际外部曲包完成 22 项读取/图像/诊断检查，包含分别移除三层效果后重新报告并改变像素；原 VSM 哈希不变。
- 原 Glow 的 GPU 结果与独立 CPU 公式进行 6 项检查：1/3/5 遍、radius 9/48/141/256、gamma 0/3；本环境比较样本的像素差为 0。
- 项目自检 127 项通过；最终源码解压到新目录独立编译并复验。
- 0 Report 表示本曲当前声明与所需资源均有实现，并不等于原游戏每次随机运行都逐像素一致。粒子/棋盘仍使用确定性随机种子，以便跳转与导出一致；尚未拿 Mac 原游戏进行逐帧同步对照。也不表示其他歌曲的专用对象已经全部实现。
