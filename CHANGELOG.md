# 更新日志

版本以 `KuroakiGimmick.csproj` 为准。16.2 及更早的条目见 [历史归档](docs/history/releases/CHANGELOG.md)，其中的命令、相对路径与验证状态可能已经失效。

## v0.1.3 / 17.0

**TL;DR** — 相对 16.2 有三件大事：① 预览补上了游戏内 HUD 与命中特效（分数 / EX / 连击 / 判定 / hold 特效，逐项可关）；② Custom 与原生对象的演出按导出的原始素材补齐（星星、`static`、`cover`、DF 侧线、冲击波、`fx_film` / `fx_edge`、Extendnova 的 CG 与剧情框）；③ 编辑器由「弹模态框改一个值」改成「右侧就地改 + 轨道上框选批量改 + 右键菜单」，FOLLOW 改为连续平滑跟随。另外重新导出了音符素材（修掉右半轨道用错帧），脚本与历史文档归档整理，验证统一到 `scripts/verify.sh`。

### 判定特效与游戏 HUD

- 音符命中时绘制原版的判定指示框、音符颗粒与钻尘；连击数两侧的钻尘同步喷出。档位配色沿用原版 `obj_noteIndicatorNew` / `o_pt_diamonddust2`：淡黄、亮黄、绿、青、红。
- 特效按命中时刻写成闭式，不保留跨帧状态：拖动时间轴、倒放与视频导出得到同一画面。
- HUD 新增 SCORE 与 EX SCORE 数字、`sp_combofont_newer` 比例精灵字体的顶部读数，以及现代 / 经典 / 描边文字三种判定弹窗。
- SETTINGS 的 VS UI 分区新增五项：SCORE、EX SCORE、HOLD FX（长条中途节拍点的特效）、TOP COMBO READOUT（OFF / COMBO / EX SCORE / ACC SCORE / MAX SCORE / ACCURACY）、JUDGEMENT POPUP（OFF / MODERN / CLASSIC / TEXT）。总开关仍是顶栏的 VS UI，设置随工程保存。
- 分数由整张谱面展开的判定表闭式求值，跳转不会停在补间中途。
- `hide_combo` 依旧是空操作：连击读数的开关在 SETTINGS 里，不由谱面参数控制。

### 音符素材与滚动控制

- 音符皮肤改用从 `game.ios` 重新导出的 `Assets/NoteSkinFull/NotesExact/`，按 `obj_note_rendering` 的取帧规则装配。
- 修复：右半四条轨道的 chip / hold head 此前克隆了左半的第 0 帧（淡紫），现在取真实的第 1 帧（粉）；bumper 的 L / M / R 也各取真实帧，不再共用同一张。
- 新增 `changeskin`（Custom，需曲包 `ENABLE_SKIN_CHANGE`）：0-3 依次是正常、stopmotion、extendnova（纯黑）、stargazers，照 `obj_custom_gimmick` 的 `singleSkins` / `bumperSkins` 两张表整套换掉音符贴图，所有轨道同时生效。取值按 GameMaker 的实数下标向零取整，所以补间中途是一跳一跳换过去而不是渐变；越界折回正常皮肤。
- 音符导出改为一次导全四套皮肤（`NotesExact/<皮肤名>/`，manifest 升到 v3）。原表里写成 `sp_empty` 的槽位如实记成"不画"而不是退回正常皮肤那张——stopmotion 没有任何地雷、没有判定 bumper、也没有中间那根 bumper，stargazers 则是没有地雷和中间那根。正常皮肤的 17 张图与上一版逐字节相同。
- 新增 `hold_end` 键：stopmotion 和 stargazers 的长条首尾是两张不同的精灵（一个平顶圆底、一个圆顶平底），此前尾帽一律画头帧。正常皮肤与 extendnova 的首尾本来就都是 chip 帧，观感不变；没有这个键的旧素材包继续退回头帧。
- 新增 `freeze`：把音符滚动用的时钟钉在事件起点上。原版给它注册的是起止回调而不是补间，所以 `from` / `to` 只当开关看（非 0 即开启），冻结到的时刻永远是这条事件自己的起点，中途读到的值不插值。冻结期间只有音符位置停住，判定、mod 求值、HUD 与命中特效照旧走真实时间——所以音符是一个个原地弹走、同时不再有新音符落下，而不是整片画面停帧。
- 登记 `drawdist`：不再报成未知 mod。它在原版里照常补间进 `cc.mod_drawdist`，但 6767 的实时绘制走 `obj_note_rendering`，只认 `drawuntil`；读 `drawdist` 的只有早已停用、没有任何调用方的 `obj_noteNormal` 实例池（`doVisualYCheck` / `OptimizedNotePoolingPlaudite`），`cc` 的 Create 里甚至没初始化这个变量。所以这里如实报告它没有可见效果，而不是替原版补一套行为。

### Custom 对象与房间效果

- 渐变色 / 蒙版星星、`static` 四帧雪花、`cover1/2/3` 遮罩、DF 侧线与可裁剪网格、`unraveling_sidething` / `sides` 的左右冲击波与粒子拖尾，均使用导出的原始素材与参数，受曲包的 `ENABLE_STARPARTICLE` / `ENABLE_DF_GRID_AND_SIDELINE` / `ENABLE_NON_BASE_FX` 门控。
- 星星使用有界的 60 Hz 确定性模拟，可任意跳转；速度变化按累计位移，反向播放不会复活已死亡的粒子。
- `fx_film`：接入原始 old-film shader、噪声纹理与房间参数；0 / 1 开关控制房间真实存在的 `FX_film` 图层并保留原深度，不依赖 `ENABLE_NON_BASE_FX`。本地资源已覆盖 gameplay、angelstar、extendnova、scarletdeath、sekaisen、starcrashers；原房间没有该层时不会套用别的房间。
- `fx_edge` 走同一条路径，使用原始边缘检测 shader 与房间阈值，受 `ENABLE_NON_BASE_FX` 控制。
- 水平噪声使用原始 `sp_noise2`，素材缺失时才回退并报告。
- 原版 Custom 对象没有注册的参数（`track_alpha`、`eo_endcg`、`sekaisen_*` 等）按原游戏跳过，并在兼容提示与报告的 `sourceNoOps` 中写明原因，不伪装成已完成的适配。

### Extendnova 原生演出

- 补齐 41 项扩展编号表与默认参数：24 帧 CG（经原始后处理，glitch 强度按难度缩放）、chorus / enddrop 两套背景、斩击、血条与结尾白场。
- 8 段剧情按原音乐时间提供只读字幕与 CG 预览，使用原版剧情框、角色渐变线与独立名牌，按行数调整框位置、逐字显示正文，并执行原脚本的清空与退场时序。剧情状态为闭式求值，倒拖与导出一致。
- 不包含游戏安装包里的 n7 剧情自动跳过补丁，也不执行对话交互、暂停流程或自动跳转。

### 编辑器：就地编辑与批量操作

- 右侧 inspector 默认改为就地编辑：可见光标、鼠标拖拽选字、全选、Tab / Shift+Tab 在字段间跳转；提交失败时保留焦点与错误提示，不静默丢弃输入。超长或多行内容仍回退到模态。
- SETTINGS 新增 INLINE FIELDS / MODAL FIELDS 开关，可换回旧的全屏输入框。
- 时间轴空白处左键拖拽框选多个片段，Shift 追加；整组可一起拖动。
- 选中的片段同属一个 mod 时，右侧开放全部字段批量修改；混选不同 mod 时只开放与 mod 语义无关的字段。值不一致时显示 `(mixed)`，原样提交等于不改。Beat 按位移生效，组内相对间距保留。批量修改一次成一步撤销，任一值非法时在改动文档之前抛出。
- 时间轴右键拖拽平移视图；原地右键弹出菜单：片段上提供 SELECT SAME MOD / DUPLICATE / MOVE TO PLAYHEAD / SWAP FROM-TO / COPY VALUES / PASTE VALUES / DELETE，空白轨道上提供在该拍新建、SEEK、MARK、LOOP IN / OUT 与选中整轨。菜单作用范围写进条目文字。
- FOLLOW 改为把播放头锁在可视区 34% 处连续滚动，不再整页跳；播放中手动平移时间轴会自动关掉 FOLLOW 把视图让给你，SETTINGS 里可改为 ALWAYS FOLLOW（默认关）。缩放与跳转不算手动平移。
- 时间轴下方的只读音符条改为按音符真实的宽窄和配色画：chip 系占一格，bumper 系压在相邻两条 chip 轨的中缝上占两格（L 盖 0-1、M 盖 1-2、R 盖 2-3），两种颜色直接量自音符皮肤精灵（1px 亮边 + 暗底），不再是一律一格的橙 / 青两色。同拍重叠时窄的画在上面，点选也优先选中窄的那个。

### 文字对象

- 新增 TEXT 列表、检视面板与独立 TEXT CANVAS（拖动 / 等比缩放 / 旋转，一次拖动一次撤销）；时间轴按对象折叠为 TXT 轨，展开箭头仍可操作原始事件轨。
- `+ TEXT` 分配未占用的 `textN` 并写入首条 cue 与初始属性，必要时经确认切换到 `obj_custom_gimmick` 并打开 `ENABLE_TEXT`。
- cue 的增删改移是就地重写单行：其余行、注释、BOM 与换行符逐字保留。属性可打关键帧或生成补间（duration 单位为拍），动画起终点可单独编辑。
- 遇到 repeat、`_`、哨兵值、proxy、未解析源行、重叠动画或歧义的尾缀 `b` ID 一律拒绝并指回原始事件面板。
- 文字内容另存到 `<工程名>.editor-texts/`，Chart Folder 导出生成游戏可读的 `[难度]_text[_tid].txt`。

### 播放路由与导出

- `playspeed` 按游戏规则累乘（终值取两位小数，负拍事件先折进初始倍率；累计倍率必须落在 0.01～100，超出的回调跳过并给出提示），`jumpto_beat` / `jumpto_s` 展开成谱面秒 ↔ 播放秒的分段映射，受 `ENABLE_MUSIC_CONTROL` 控制。
- 一次性跳转（含向后 seek）展开成有限的等速段，用 10000 段 / 24 小时双上限挡住互跳死循环。
- 实时播放位置与倍率、顶栏 `CHART x.xx`、报告的 `playbackDuration` / `musicControls` 与视频导出共用同一条路线；导出按该路线拼接画面与音频。
- 拍区间语法（`start`、`start:end:step`、括号写法）由运行时解析器与编辑器保源片段共用；括号写法改用严格数值解析，不再被数字前缀恢复逻辑悄悄截断，步长须为正、展开上限一百万。
- 修复：写出的 VSM 不再带 `mods` 标记行，也不再写 `//` 注释。原版 `read_mods_file` 一开头就是 mods 模式，整段只认 `mpf` 一个切换标记，没写过回到 mods 的路；`mods` 行和注释行都会走到 `string_split(line, ",")` 然后取 `parts[1]` 越界崩掉，空行反而无害。随之：无源文件时生成的文档直接从 `!obj` / `!proxies` 接事件行；往末尾已是 `mpf` 段的文件里追加效果事件时插到 `mpf` 那一行之前，而不是另起一个 `mods` 段；随包的 5 份示例 VSM 一并清掉。
- 读取仍然宽容（这两种写法照样解析），但会各报一条错误指出原版会在第几行崩，免得作者在预览里一切正常、进游戏才发现打不开。

### 仓库与工具

- 新增启动参数 `--force-vulkan`，把 SDL_GPU 后端钉死在 Vulkan 上，不再按平台默认挑（macOS 用 Metal、Windows 先 Direct3D 12）。强制时不做回退：起不来就带着 SDL 的原始错误直接启动失败，否则"强制"没有意义。macOS 经 MoltenVK 走 Vulkan，参数会自己按 `$VULKAN_SDK/lib`、`/opt/homebrew/lib`、`/usr/local/lib` 找 loader 并设上 `SDL_VULKAN_LIBRARY`（已手动设过就不动），因为 Homebrew 的 `/opt/homebrew/lib` 不在 dyld 默认搜索路径里，装了也会被 SDL 报成"不支持"；Windows 上照旧拒绝软件 Vulkan（Dozen / Lavapipe / SwiftShader），强制也不放行。三个平台的失败提示都会说明下一步该装什么或改什么。可与 `--gpu-test` 合用，不开窗口就验后端。
- `scripts/` 根目录只保留启动、打包与验证入口：`run-editor.command` / `run-editor.ps1` 一键构建并以 `--editor` 打开工程；`verify.sh` / `verify.ps1` 是统一验证入口（`restore --locked-mode` → Release 构建 → 9 项托管自测，`--gpu` 才追加 GPU 自测，任一步失败即停）。
- 专项 `check-*` / 文档生成 / 画面对比移到 `scripts/dev/`，本地游戏资源提取与导入移到 `scripts/assets/`。
- 旧 README、打包说明、CHANGELOG、各次 HOTFIX / UPGRADE 与原始哈希清单归档到 `docs/history/releases/`，边界见 `docs/history/README.md`。

### 范围说明

- 预览没有输入，不做玩法判定：判定档位恒为 A.CRITICAL（对应原版 autoplay 分支）；原版依赖逐次时间误差的 Graph 档 `sp_hit_display` 未还原。
- Extendnova 的专用音符皮肤素材随包，但当前仍使用公共音符皮肤。
- `Assets/` 内的游戏素材来自本机只读导出，不随仓库公开。
