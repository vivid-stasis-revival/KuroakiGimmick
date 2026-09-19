> 历史文档：以下内容来自输入源码及其既有修订，不能作为 r19 的编译、运行或画面验收结果。当前状态见 [VALIDATION_R19.md](VALIDATION_R19.md)，当前结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

# Note Skin

r16 使用 dump manifest 还原 NoteSkin。compact v1 的 PNG 是 `ExportAsPNG(..., includePadding:true)` 得到的完整 GameMaker frame，因此直接按 `source_sprites` 的 width / height / origin 整帧绘制。当前普通 / timing bumper 为 107×7、origin (53,3)，L/M/R 侧标记是原 sprite 的真实像素；不能用 45×7 的 `bumper_mine` 去裁它。

优先级：`Assets/NoteSkinFull/NotesExact` > `Assets/NotesExact` > `Assets/Notes`。raw-source / exact v2 仍使用 metadata/frames.json 的 TargetX/Y/TargetWidth/TargetHeight 把 atlas crop 放回 GameMaker bounding box。

## v0.1.2：逐轨道帧

compact v1 每个 sprite 只导出 frame 0，`chip_R` / `hold_head_R` 只能从 `chip_L` / `hold_head_L` 克隆，于是右半边四条轨道画成了左半边的淡紫色。游戏里它们是粉色：`sp_note_chip_normal` 的 frame 0 是 `cbdaf5`，frame 1 是 `f5cbde`，两帧在 manifest 的 `frame_sha256` 里本来就标着不一致。

`scripts/assets/Dump_Notes.csx` 按 `obj_note_rendering` 的实际取帧方式导出 exact-lanes v2：Create 事件的 `lane_sprites` 给出每条轨道的 sprite，Draw 事件里 chip 轨的子图下标是 `(i >= 2)`、bumper 轨是 `i - 4`。长按的头和尾同样画 `sp_note_chip_normal`，所以 `hold_head_*` 与 `chip_*` 取同一帧。

| key | sprite | frame |
| --- | --- | --- |
| `chip_L` / `hold_head_L` | `sp_note_chip_normal` | 0 |
| `chip_R` / `hold_head_R` | `sp_note_chip_normal` | 1 |
| `chip_mine_L` / `chip_mine_R` | `sp_note_chip_mine_normal` | 0 / 1（两帧相同） |
| `hold_body_L` / `hold_body_R` | `sp_note_hold_normal` | 0 / 1 |
| `bumper_L` / `bumper_M` / `bumper_R` | `sp_note_bumper_normal` | 0 / 1 / 2 |
| `bumper_mine_L` / `_M` / `_R` | `sp_note_bumper_mine_normal` | 0 / 1 / 2（三帧相同） |
| `judge_bumper_L` / `_M` / `_R` | `sp_note_bumper_timing_normal` | 0 / 1 / 2 |

与 compact v1 不同，exact v2 导出的是未补边的原始裁切（`includePadding:false`），manifest 里的 `width` / `height` 必须等于 PNG 的真实像素尺寸，绘制矩形由 metadata 的 `target` 减去 `origin` 得到。bumper 的 L / R 帧因此只有 52×7 和 53×7，落点分别是 -29 和 -22，而不是整幅 107×7。

导出与安装：

```
KUROAKI_NOTE_OUTPUT=<dir> UndertaleModCli load <game.ios> -s scripts/assets/Dump_Notes.csx
bash scripts/assets/install-note-dump.sh <dir>
```

按 `R` 事务式重载。
