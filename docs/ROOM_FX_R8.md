> 历史文档：以下内容来自输入源码及其既有修订，不能作为 r19 的编译、运行或画面验收结果。当前状态见 [VALIDATION_R19.md](VALIDATION_R19.md)，当前结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

> r9 更正：本文最后的轨道/默认 INTEGER 处理已撤回。轨道在 y=165 截止，底栏留给标题；默认 FIT。以 GAME_UI_R9.md 为准。

# src-r8-room-fx

已接入用户在原 Mac 游戏上导出的 7 份房间 FX 配置。原 JSON 字节不变，来源与 SHA-256 见 Assets/RoomFX/manifest.json。只增加不到 10 KiB 的配置和一个 colour balance shader；没有 ASTELLION 歌曲/贴图/曲绘。

## 房间选择

左侧 ROOM FX 按钮依次切换 AUTO、GAMEPLAY、PLAUDITE、ANGELSTAR、SCARLETDEATH、EXTENDNOVA、SEKAISEN、STARCRASHERS、NONE。选择会随工程保存，重建后保留当前位置。也可继续附加外部 `.fx.json`。

```bash
dotnet run -c Release -- --inspect /path/FINALE.vsm --room plaudite
```

`--room scene_gameplay_plaudite` 等完整房间名同样可用。优先顺序：显式附加的 FxProfile → AUTO 模式下谱面旁 gimmick-fx.json → 选择的内置配置。显式选择某个房间会覆盖本地自动配置；NONE 只关闭房间 profile，原有 custom 后处理仍受 FX 按钮控制。

AUTO 根据已知原对象选择对应房间；普通 base 选 scene_gameplay。**自制歌曲的 VSM/文件夹没有提供实际选房信息时，AUTO 暂用 scene_gameplay，并在 REPORT 标记 autoFallback。** Custom 的 jacket 管理模式不是房间名，plaudite 换曲绘也不证明使用 Plaudite 房间。原包没有提供自制加载器的完整房间创建/选房代码，不能确认 Weavers / Looping 实际使用哪个房间。请通过 ROOM FX 选择与你游戏一致的房间，或附加修改后配置。

## 原配置确认的差异

| 房间 | FX_chroma filter / depth / 可见 | FX_contrast depth | FX_red |
|---|---|---|---|
| gameplay | underwater / -1500 / 隐藏 | -1400 | 无 |
| plaudite | heat haze / -1400 / 显示 | -1300 | 无 |
| angelstar | underwater / -1600 / 隐藏 | -1500 | colourise，depth -2300 |
| scarletdeath | underwater / -1600 / 隐藏 | -1500 | colour balance，depth -2200 |
| extendnova | underwater / -1600 / 隐藏 | -1400 | 无 |
| sekaisen | underwater / -1500 / 隐藏 | -1400 | 无 |
| starcrashers | underwater / -300 / 隐藏 | -200 | 无 |

全部 contrast 都是 colourise：白色 tint，动态 `g_Intensity = 1 - fx_contrast`。不是凭名字套一个普通对比度公式。

普通 underwater chroma：第二组 scale=[60,35]，speed=0.046，色散=3。Plaudite heat haze：第二组 scale=[66,66]，speed=0.063，色散=5.75。使用各自原始通用噪声贴图。

六个房间的 chroma 在静态配置中隐藏，提供的 cc Step 只设置强度，没有将它显示的代码。本版保留隐藏状态；即使谱面有 fx_chroma_distort，也不会强行打开。REPORT 会注明这种情况。若自制加载器在运行时改变可见性，仍需相应代码/运行时配置才能匹配。

Scarlet Death 的 colour balance 使用阴影色调 [1,0,0]、中间调和高光 [0,0,0]；按原 shader 调色并保持亮度，读取真实三组参数。它没有 colourise 的 g_Intensity / g_TintCol，自制 Step 向不存在的 uniform 写值不会改变本滤镜。Angelstar 则保留 colourise；custom 对象可覆盖 tint/intensity，原 Angelstar 对象保留房间 intensity。

默认 underwater 两组 amount=0 会使原 glint 公式出现 0/0；colour balance 在黑色像素同样可能除零。适配仅对这两个退化情况加有限值保护，非退化公式不变，避免不同 GPU 输出 NaN/黑屏。

## 补丁遗漏修正

`notealpind0..6` 实际在 Custom Gimmicks 的 **codepatches.json** 中被注入原 note 与 pressed hold Draw：`notealp * notealpind[lane]`。r7 只检索了 GML 文件，之前“没有 Draw 使用它”的判断不完整。r8 已对普通音符、bumper 和 hold 使用逐轨 alpha，默认 1，后续 proxy 与局部裁剪流程不变。

## 范围

这些是静态房间 FX 的实现，不等于完整的七首歌特殊对象。Scarlet/Angelstar 的 red/hue 可见性与已知 Step 绑定已接入，其余剧情/场景特效仍未完整移植。VSB 的专属 mod ID 映射仍以现有对象支持范围为准；文本 VSM 可直接保留这些名字。

已适配 FX 按导出的 depth 排序，自制 tintLayer 插在 -2400。显式选择的房间或外部 profile 若没有 red/hue，缺失层不绘制；AUTO 无法确认自制房间或关闭 profile 时，才沿用 r7 的源公式回退并明确提示顺序未确认。没有假装把缺失的对象/图层创建过程还原出来。

这次导出不含 FX_underwater（注意与 FX_chroma 使用 underwater filter 不同）、LBG、其它未导出的 FX 或 shader_optimized_glow。原有全局 underwater 回退层参数、替代 glow kernel、水平噪声、独立粒子 glow、运行时相位以及绝对对象 depth 的差异仍存在。原房间动画时间起点也可能早于歌曲零点。

Weavers 仍可读取 4,907 事件、1,936 音符、916 图片、124 文本轨道；contrast 的缺配置提示已消失。Looping 仍只有 VMV 缓存，测试仅为事件兼容，不能宣称整曲已还原。

## 轨道底部与预览像素（同轮追加）

原始 sp_laneOverlay / sp_holdnote_overlay 高度为 180；旧版把整个 field 裁到 165，造成底部 15 像素缺失。现在直接绘制完整轨道，只有 note/hold 仍在局部 y=165 裁剪。proxy 依照 Custom Gimmicks Draw_74 分别合成 y=0..165 的移动轨道与 x=81..239、y=165..180 的原位底栏；底栏遵循 hom / uialpha。原 PNG 字节没有改动。

Viewer 的 PLAYING / PAUSED 移到预览外，不再覆盖演出图片或歌词。默认 INTEGER 使用实际屏幕像素计算倍率与起点，适配 Retina，不会把一个源像素交替放大成不同宽度。FIT 可继续切换；窗口太小时缩小适配，不越过面板。此选项只作用于预览显示；导出保留原 320×180 场景及既有分辨率设置。

收到的 Looping 缓存没有配套 VSP / 图片 / 文本，因此不能判断截图中 “pause” 字样是在原图片/字体栅格化阶段损失笔画，还是单纯的非整数放大。此轮修复遮挡与显示倍率，没有更换原有图片采样或声称已逐帧复原该字样。完整 Looping 资源仍需补齐。
