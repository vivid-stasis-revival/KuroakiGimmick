> 历史 r11 说明。r12 已补入原三帧与动态 LBG 定义，当前范围见 SCARLET_BEAT_R12.md。

# r11：中文与棋盘兼容

Monaco 的 CJK 字形不是图片分辨率不足。原资源的 glyph.Y 还需要加 AscenderOffset（此字体为 6）才能取到完整矩形；旧版把偏移用在屏幕坐标，遗漏了字形底部。现在歌词与 HUD 共用正确的源图采样，原字体 PNG 未修改。Default / Monaco 的同一组中文原字形逐像素一致。

## angelstar_checker

已根据 `o_angelstar_checker` 和 Custom Gimmicks v1.12.7 实现：

- `ENABLE_ANGELSTAR_CHECKER` 控制对象是否创建。
- `angelstar_checker_alpha` 控制透明度。
- `angelstar_checker_set` 正值刷新一次并被消费；持续 tween 按原 60 Hz 更新。刷新间保留表面，包括透明像素下的旧内容。
- 320×180 表面、32×18 个 10×10 格子。首次全部使用 frame 2，之后每格选择 0–2。
- `angelstar_checker_mode`：0 在曲绘乘色之前，1 在曲绘之后、轨道之前，2 在轨道底板之后、note 之前。
- 自制对象沿用源代码的红色色相。原游戏的随机种子未知，查看器使用固定种子确保拖动时间和视频导出可重现；随机格子布局不保证逐帧相同。

**现有附件只有 checker 的精灵元数据，没有三帧 PNG。查看器不会生成替代图案。**

## 补上原图

macOS 下载包中的 `scripts/assets/Dump_Gimmick_Extras_Mac.sh` 可以直接运行：

```bash
bash scripts/assets/Dump_Gimmick_Extras_Mac.sh
```

自动寻找游戏、校验下载 UTMT CLI，并把原图安装到这个程序文件夹的 `Assets/GimmickExtras`。若脚本另存于 Downloads，请通过 `--out` 指定 `.app` 所在的文件夹：

```bash
bash ~/Downloads/Dump_Gimmick_Extras_Mac.sh --out "/你的程序文件夹"
```

安装后按查看器 R 重载歌曲。预期文件为 `sp_angelstar_checker_0.png`、`sp_angelstar_checker_1.png`、`sp_angelstar_checker_2.png`，每帧 10×10。已有目录会先保留为带时间戳的备份。源码项目把该目录放在项目的 `Assets` 下，构建脚本会复制到输出目录。

导出只读，不写游戏数据，不带走整张图集、歌曲或曲绘。仅导出原始棋盘帧、`shader_optimized_glow` 及字符串池中可能存在的动态 FX 定义。所有缺失项写入 `gimmick-extras.json`。

## 仍需核对

- `fx_colorise` 没有出现在所提供 v1.12.7 的注册表。有效的分别是 `fx_colorise_intensity`、`fx_colorise_col_rgb`、`fx_colorise_col_alpha`；不擅自将无后缀事件解释为其中之一。
- 原粒子 glow 缺 shader 与对应参数。脚本能补出 shader，但 r11 尚不自动执行它，需要把生成的 ZIP 发回继续核对。
- 动态创建的 `LBG` heat haze 默认参数不在静态房间 dump 中。脚本尝试收集嵌入定义；若没有记录，仍需运行时参数，不能用另一房间的值代替。
- 本轮只有 Scarlet Death(DouBaoAI Edit) 的兼容报告，没有这首自制歌曲的 VSM、配置、文本文件，不能据此确认全曲演出已经还原。请连同上述 ZIP 提供这首歌的相关文件。

没有新增 ASTELLION 歌曲素材。兼容外部 ASTELLION 谱面的功能保留。
