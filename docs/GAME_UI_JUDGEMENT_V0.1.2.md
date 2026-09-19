# KuroakiGimmick v0.1.2 / 16.3 — 原版判定特效与游戏内 HUD

依据 `/Applications/VIVIDSTASIS.app/Contents/Resources/6767/game.ios` 反编译出的 GML：`cc` 的 Create/Draw、`obj_note_rendering`、`obj_pressed_holds`、`handle_judgement_normal`、`spawn_particles_directional`、`o_pt_diamonddust2`、`o_combodisplay`、`o_judgement_ingame`、`obj_judgement_display`、`LoadSong` 的 notecount 分支。较新版本（8266）把粒子绘制移进了 C++ DLL，不能作为参考。

## 判定流

预览没有输入，按原版 autoplay 分支还原：所有判定都是 A.CRITICAL（`timings[0] = 35`）。`ScoreState` 在载入时把整张谱面展开成一条按时间排序的判定表 `Hits`，HUD 和打击特效都从这一张表取数：

| 谱面类型 | 判定 | 时间 |
|---|---|---|
| 0 普通 | Note | 音符时间 |
| 1 / 8 宽键 | Wide（lane + 4） | 音符时间 |
| 2 长条 | 头判 + 每拍一次 HoldTick + 尾判 | `ms + p × 60000/bpm`；尾判在 `end` |
| 6 / 7 地雷 | Mine | `ms + 35`（原版 `miss_timing = -timings[0]`） |

长条的 tick 数按原版 `floor(max(0, 时长 - 150) / 每拍毫秒)` 计算，尾判固定保留 150 ms 宽限。`NoteValue = 1000000 / notecount`，与 `LoadSong` 的计数口径一致。

## 无状态求值

原版的分数爬升、补间和淡出都靠逐帧累加，预览必须支持拖时间轴，因此全部改写成「距上次判定多久」的闭式。`sdisp_accscore` / `sdisp_exscore` 是追赶积分器，原版按 `timediff`（曲目时间）推进，这里预先算出每次判定处的追赶值 `D_k = min(k·step, D_{k-1} + rate·Δt)`，任意时刻 `O(log n)` 二分求得。补间和粒子原版按 `delta_s`（真实帧时间）推进，这里一律换成曲目时间，否则跳转后会停在补间半路。

## VS UI 元素

总开关仍是顶栏的 `VS UI` 按钮（`GameUiEnabled`）。SETTINGS 面板新增一段单项开关，全部保存进设置文件与工程：

| 设置 | 字段 | 对应原版 |
|---|---|---|
| SCORE | `GameUiScore` | `sp_2024cc_score` 帧 0 + `fnt_credits` 的 `round(sdisp_accscore)` |
| EX SCORE | `GameUiExScore` | `sp_2024cc_score` 帧 2 + 默认字体的 `round(sdisp_exscore)` |
| HOLD FX | `GameUiHoldEffects` | `obj_pressed_holds` 每拍触发的 `o_pt_diamonddust2` |
| TOP COMBO READOUT | `GameUiCombo` | `global.op_minusscore`：OFF / COMBO / EX SCORE / ACC SCORE / MAX SCORE / ACCURACY |
| JUDGEMENT POPUP | `GameUiJudgement` | OFF / MODERN（`sp_judgements_2`）/ CLASSIC（`sp_judgements`）/ TEXT（`draw_text_outlined`） |

连击数用 `font_add_sprite(sp_combofont_newer, 32, true, -7)` 还原：步进 = 纹理页裁剪矩形的宽 + (-7)，负字距是原版有意让字形描边互相压边。原版 `draw_text_o(171, y + 8)` 配 `fa_center` 的起笔点无法直接照搬，但同一次事件里的钻尘是以 `160 ± (string_width / 2 + 6)` 向两侧喷的，说明数字视觉上落在 160，因此这里按墨迹范围居中到 160。

原版的 Graph 档（`sp_hit_display`）需要每次判定的时间误差，autoplay 下恒为 0，没有还原。

## 资源导出

`scripts/assets/Dump_Game_UI.csx` 与 `Dump_Game_UI_Mac.sh` 增加导出 `sp_judgements`、`sp_judgements_2`、`sp_combofont_newer`。当字模用的精灵额外写出逐帧 `bounds`（纹理页的 `TargetX/TargetY/TargetWidth/TargetHeight`），因为比例字宽就取自这个裁剪矩形。精灵缺少 `bounds` 时不排版，不拿整帧宽度去猜；判定精灵缺失时该档位不绘制，其余 HUD 照常。旧的资源包只要重新跑一次导出脚本即可补齐。
