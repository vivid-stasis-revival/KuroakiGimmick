# 渲染与兼容行为

[返回 README](../README.md) · [编辑与导出指南](EDITING_GUIDE.md) · [Custom Proxy 与循环修复](CUSTOM_PROXY_AND_LOOPS.md)

本文记录已实现的演出行为与边界。涉及的游戏素材、原始源码与曲包保存在本地，未随公开仓库提供。带日期的验证记录只适用于当时的修订。

## fx_film

时间轴可直接使用 `fx_film`，0 关闭、1 开启；F1 中的参数说明仍可用于添加事件。开关控制选定房间真实存在的 `FX_film` 图层，保留原图层深度，不依赖 `ENABLE_NON_BASE_FX`。当前本地资源已补齐 gameplay、angelstar、extendnova、scarletdeath、sekaisen、starcrashers 的原始电影图层；原房间没有该层时不会自动套用其他房间的效果。

原始 shader、纹理与参数从本机《vivid/stasis》只读导出，仍保存在非公开的 `Assets/` 中。`scripts/assets/Dump_Film.csx` 可用于 UTMT 只读导出，输出目录通过 `KUROAKI_FILM_OUTPUT` 指定且必须尚不存在。噪声动画使用演出时间，前后跳转与固定帧导出可重复。

## 星星、遮罩与 playspeed

Custom 对象已支持 `starspawner_timer`、`starspd_low/high`、`starspd_multiplier`、`active_startrans`、`startrans_alpha`，并接入同一原始系统的渐变色星星参数。`ENABLE_STARPARTICLE` 默认关闭，启用后才创建星星系统。蒙版星星在背景乘色之前绘制，停用时冻结自身状态；速度变化使用累计位移，已经越界或死亡的粒子不会因为反向播放而复活。预览与导出使用相同的固定 60 Hz 模拟和随机种子，可重复跳转；随机布局不保证与游戏某一次运行完全相同。

`cover1/2/3` 使用游戏原始精灵及黑色混合，按源码的覆盖层顺序绘制。`hide_combo` 仍是空操作：无论谱面是否包含该参数、其值为何，都不改变画面；顶部连击读数由 SETTINGS 的 TOP COMBO READOUT 控制，默认显示 COMBO，可设为 OFF。该参数仍保留在谱面中。

`playspeed` 遵循游戏源码：在事件开始时将当前速度乘以按游戏规则取两位小数的终值，忽略普通补间时长，受 `ENABLE_MUSIC_CONTROL` 控制。负拍数初始化同样生效；多个事件累乘，倒拖不会重复叠乘。界面手动倍速作为额外倍率，时间轴仍显示音乐/谱面时间；Viewer 在时间旁显示非 1 的 `CHART` 倍率。

视频导出遵循谱面 `playspeed`，同时调整画面进度、音频速度与音高，不继承界面手动倍速。例如 3 秒谱面区间在 1.2 倍速下输出 2.5 秒视频。支持中途倍率切换；累计倍率支持 0.01～100，音频导出单次最多 512 个变速区间，超出限制明确报错。

**对象作用域**：`track_alpha`、`en_whiteoverlay`、`eo_endsat1–4`、`eo_endcg`、`sekaisen_arrow_point` 属于 Seka­isen 原生对象。原游戏的 Custom 对象没有注册这些参数，会在 `updateMods` 中跳过。对于声明 `obj_custom_gimmick` 的谱面，Viewer 保留源事件，按原游戏跳过，并在兼容提示和报告的 `sourceNoOps` 中说明原因；这里没有把这些参数伪装成已完成的原生对象适配。

上述实现依据本机游戏的只读 UTMT 导出；相关原始代码和素材保存在非公开的 `Assets/CustomGimmicks/`。专项检查：`dotnet run -c Release -- --custom-adaptation-self-test`；`--gpu-test` 包含星星、遮罩和 `hide_combo` 不改变画面的实际绘制检查。

## 其他 Custom 演出与音乐跳转

- `fx_edge`：使用原游戏边缘检测 shader、房间阈值和图层深度，受 `ENABLE_NON_BASE_FX` 控制；常规 gameplay 房间包含其创建代码动态添加的 `FX_edge`。
- `static`：使用原始四帧雪花素材，按 `floor(beat × 16) % 4` 切帧，并按游戏中的四个区域绘制。
- `unraveling_sidething` / `sides`：生成左右冲击波及原始粒子拖尾；位置、速度、淡出和生存时间来自游戏对象代码。
- `df_sideline` / `df_sideline_alpha`：控制两侧原始线条的位置和透明度，同时支持同一处理器的 `df_grid_alpha/top/bottom`；受 `ENABLE_DF_GRID_AND_SIDELINE` 控制。
- `plaudite_pburst`：计数模式保留原行为；连续模式将 From 作为开关、To 作为持续毫秒数，模拟原代码的倒计时补间和固定 tick 发射，不把持续时间当粒子数量。超出粒子预算时只报告一次，不按展开事件刷屏。
- 水平噪声：已使用游戏的 `sp_noise2` 原始噪声纹理；素材缺失时才回退并报告。

`jumpto_beat` 在正常播放时跳到指定拍数；`jumpto_s` 按 From 选择秒或毫秒单位。音乐控制开关关闭时不执行。跳转回调在一次播放路径中只执行一次，跳到自身不会死循环；暂停和切换手动倍速保留路径进度，手动定位重新建立后续路径。导出会同步拼接跳转前后的画面与音频。后退跳转后的画面按目标歌曲时间重新求值，不复刻 GameMaker 任意实例的历史状态。音频导出最多 512 个路径片段，过于复杂或过长的路径会明确报错。

2026-09-14 验证：新增 15 项 FX/音乐控制自测及完整 CPU/IO 回归通过；Metal 验证边缘检测、雪花切帧、DF 侧线与冲击波/拖尾。实际音视频导出验证了向前跳转和向后跳转的时长与音频拼接。`Still Shining` 当前解析报告无兼容提示。

## Extendnova 原生演出

`obj_extendnova_gimmick` 已补齐原游戏的 41 项扩展编号表，支持原生后处理、按难度调整的 glitch 强度、24 帧 CG、两套热浪背景、斩击、血条与白色判定区域。音符统一使用普通皮肤，不切换 Extendnova 纯黑皮肤。房间配置包含原始 `FX_glow`、背景 glow、posterise 和 edge 图层，使用原始深度和参数。

**不包含游戏安装包中的 n7 剧情自动跳过补丁。** Viewer、Editor 和视频导出保留完整音乐时间轴，不会因为剧情回调跳到下一段音符。8 段剧情按原音乐时间提供只读字幕与 CG 预览，使用原版剧情框、角色渐变线和独立名牌，按行数调整框的位置、逐字显示正文，并执行原脚本的清空与退场时序；不再每 5 秒循环翻页。不执行游戏对话交互、暂停流程或自动跳转，不播放逐字音效。结尾白色淡出按原脚本时间绘制。

`en_evildawn_hpbar_shake` 在该对象中只注册、初始化，没有对应的读取行为，报告保留此源码说明；不通过伪造抖动来消除提示。顶部连击读数与该对象无关，由 SETTINGS 的 VS UI 分区统一控制。

验证命令：`dotnet run -c Release -- --native-sequence-self-test /path/ENCORE.vsb`。本机 n7 ENCORE 的长音频、扩展编号和完整播放时长验证通过；Metal 检查覆盖 CG 换帧、背景、Glow、斩击、血条与回退一致性。Windows 尚未实机验证。所有新增游戏素材与剧情原文继续保存在非公开的 `Assets/` 内。
