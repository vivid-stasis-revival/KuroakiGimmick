> 历史文档：以下内容来自输入源码及其既有修订，不能作为 r19 的编译、运行或画面验收结果。当前状态见 [VALIDATION_R19.md](VALIDATION_R19.md)，当前结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

# 原 UI / 设置 / note 修正（r10 更新）

依据用户提供的反编译源码：cc Draw_0、obj_note_rendering Draw_0、obj_pressed_holds Draw_0、LaneData、define_options，以及 Custom Gimmicks 图片层创建/Draw_74。

## 原始资源

新版已经内置本次 Mac 导出的公共 UI 和三套字体，正常使用无需再导出。更新资源可运行 `bash scripts/assets/Dump_Game_UI_Mac.sh`；默认寻找 /Applications/VIVIDSTASIS.app/Contents/Resources/game.ios，支持指定其它路径。读取程序旁 Assets/GameUI/，按 R 重读。不再扫描文稿目录；移动 .app 时把旁边 Assets 文件夹一起移动。

仅读取 sp_gameplayoverlay2024、sp_2024cc_score、sp_newdifficultyindicator、sp_newdifficultylevel、sp_newdifficultynumbers、可选 sp_fc_indicator；fnt_monacovs、fnt_credits、可选 fnt_phosphor。原点、padding、每帧 PNG、字宽/偏移/kerning 保留。自定义输出路径时，把 game-ui.gameui.json 拖到查看器，或 `--game-ui /path/GameUI`。（v0.1.2 起导出内容与游戏内 HUD 的还原范围见 [GAME_UI_JUDGEMENT_V0.1.2.md](GAME_UI_JUDGEMENT_V0.1.2.md)。）

原素材缺失时报告具体错误；没有替代 HUD。图片 gimmick 的 PAUSE、标题、分数不属于这套原 HUD，不用 uialpha 错误地一起隐藏。Looping 的原游戏截图本身也有变形 PAUSE 图案，不能把这幅图当作查看器播放标签。

## 原布局及图层

歌曲名/作者起点 (3,168)，难度牌 (274,167)，PAUSE Escape / score / EX 采用原代码位置和字体度量。曲目资料读取 info.json/song.json，formatted_name 优先；ENCORE 使用 enc_data 覆盖以及 backstage 类型。可用 --song-name / --song-artist / --song-level 显式覆盖。缺失等级显示 ?，不编造等级。无玩法判定，原生 score/EX 为零；省略左侧统计。

| 内容 | 原 depth | 对应 VSP priority 分界 |
|---|---:|---:|
| 轨道 | 200 | -200 |
| 判定区 | 0 | 0 |
| note | -350 | 350 |
| 原 HUD | -1000 | 1000 |

图片 layer depth = -priority。priority 越大越靠上；同一分界的图片在上述对象之后画。图片可盖轨道、note 和 HUD。最底部 y=165..179 保留给标题，不能再把原位轨道片段拼在移动轨道下面。r10 将固定 HUD 从轨道贴图中分离：先合成轨道副本，再绘制固定 HUD，最后合成 priority >= 1000 的图片。图片继续使用原 proxy crop、motion、warp、alpha；固定 HUD 不再被复制到移动轨道中。底栏保留图片，不包含裁掉的轨道片段。

uialpha 应用于原 HUD；hom 应用于原 proxy 合成固定区域。特殊对象自行绘制的任意 GML UI/对象不会自动执行。没有真实 Looping VSP 时无法确定每张假标题图的层、像素和大小。

## 字体选择

SETTINGS → Text Font 提供 Default / Monaco。原 define_options 的索引 0、1 在 set_default_fonts 中分别映射到 fnt_monacovs、fnt_phosphor；不可按资源名字反推选项名字。两个图集均来自成功导出的原包。选项即时改变普通 HUD 文字和 gimmick 文本，保存到默认设置及工程，关闭重开仍保留；旧设置文件没有此字段时使用 Default。

PAUSE 位图、难度徽章、固定分数字体 fnt_credits、VSP 图片中的字形不随 Text Font 重画。游戏中的第三项 Alternate 对应 fnt_phosphor_2a，本次没有其图集，因此不显示。CLI 可用 `--ui-font Default` 或 `--ui-font Monaco`，也保留原资源名参数。

## Note 与校准

普通 chip 中心 x=126/149/172/195，bumper x=137/160/183，与原 LaneData 一致。Top 默认 note anchor = NoteModsY + 3；Bottom = NoteModsY - 4。按住 hold 后保留尾帽，端点限制在 y=140，body 延伸到 y=144；不再在判定线下额外画普通起始帽。旋转 chip / bumper 按原条件补偿一像素。

设置的音频延迟：正值在预览音轨前补静音，负值跳过开头相应样本；seek / 变速重新定位并保留校准。试听音量与音频校准不改变导出原音轨。视觉延迟只延迟 note 位置、开始/结束可见性与 hold 按住状态，图片/FX 仍使用歌曲时钟。所有设置范围与 CLI 参数见 --help。

设置位于系统应用数据目录 KuroakiGimmick/settings.json；DONE 或 ESC 保存，工程 `.sgv.json` 同时保存其值。直接打开新歌曲使用默认设置；打开工程保留工程设置。

## 渲染质量

默认 320×180，不是 320 像素高。选项为 320×180、640×360、1280×720、1920×1080、2560×1440、3840×2160。所有场景、图层合成和后处理目标按该尺寸创建；坐标和原 FX 参数仍按 VS 的 320×180 逻辑单位，避免质量设置改变效果的大小。高分辨率旋转边缘重新栅格化；像素素材依然最近邻。高分辨率增加 GPU 显存和运算开销。

导出尺寸可单独设定：内部 320×180 输出 4K 是像素放大；内部 4K 输出 4K 是全尺寸绘制。这两种模式在设置和预览信息中明确区分。

## 验证边界

已编译真实 UTMT 0.9.2.0 API，使用实际 Undertale 数据模型执行导出，检查透明 padding、原点、字体图集裁剪和 kerning。用户已成功运行修正版脚本，实际公共 UI 精灵和原字体已加入 Assets/GameUI；Weavers 205 秒原 UI 可见时段已用这些资源渲染并检查。尚未和原游戏同帧逐像素对照。查看器用 Linux SDL3/Mesa 执行实际 GPU 测试，包括图层覆盖、原 UI alpha、假标题、hold、旋转、视觉延迟和真实 4K 场景尺寸。Mac 实机/音频设备校准仍待实测。
