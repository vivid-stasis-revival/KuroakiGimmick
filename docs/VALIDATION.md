> 历史文档：以下内容来自输入源码及其既有修订，不能作为 r19 的编译、运行或画面验收结果。当前状态见 [VALIDATION_R19.md](VALIDATION_R19.md)，当前结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

# r16 NoteSkin complete bumper frame

当前 compact dump 的 `sp_note_bumper_normal` / `sp_note_bumper_timing_normal` metadata 是 107×7、origin (53,3)。反编译 `obj_note_rendering.Draw_0` 直接调用 `draw_sprite_ext(sprite, frame, ...)`，因此 frame 内 L/M/R 侧标记与中央主体必须一起绘制。r16 compact-v1 解析结果：draw 107×7、local (-53,-3)、UV (0,0,1,1)。`sp_note_bumper_mine_normal` 是独立 45×7 sprite，不能用于推断普通 bumper 的裁剪尺寸。
