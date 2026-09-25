# 更新日志

版本以 `KuroakiGimmick.csproj` 为准。16.2 及更早的条目见 [历史归档](docs/history/releases/CHANGELOG.md)，其中的命令、相对路径与验证状态可能已经失效。

## v0.1.4 / 17.6

- 修复 Custom 字幕绘制层级：旧式和具名 text 按原版 depth -10 绘制，`cover1/2/3` 能正确遮住字幕。预览与视频导出共用此顺序，字幕与遮罩一起参与 Proxy 采样。
- 新增 GPU 像素回归，覆盖三种 cover、透明度动画、倒拖及 Proxy 移动。

## v0.1.4 / 17.5

### 谱面内剧情（Custom Episodes 的 `custom_episode`）

- 谱面里写 `beat,0,linear,_,_,custom_episode,-1` 即可在演奏中浮出对白框。现在 viewer 会按歌曲时间把同目录的 `story.json` 演出来，`!story:` 可指向别处共用一份剧本。
- 时刻表是一次性前缀和展开的闭式结果，不依赖"上一帧演到哪儿"，所以拖时间轴、倒放和导出得到同一画面。每句占用"打字时间 + 停留时间"：打字机每秒 `100 × speed` 个字，停留取显式 `dwell`，否则是 `chart_dwell` 加一份按字数的估算，两者分开累加——与模组的 `mod_cs_chart_step` 逐项对齐。
- 谱面内的常量与剧情房间不同，照模组取：文字框停在 **132**（剧情房间是 122），退场从停稳位置滑回、用 0.8 秒，`speed` 缺省 **0.5**（谱面房间可跑到 1000 fps，沿用原版速度会快得看不见打字过程）。
- **不支持的指令在试玩前就报出来。** `portrait`、`bg`、`cg`、`flash`、`fade`、`fullscreen` 这些属于剧情房间的步骤在谱面内会被游戏静默跳过，日志写在存档目录且作者关不掉；现在逐种转成诊断。剧本缺失或损坏同样报错而不是什么都不发生。
- `custom_episode` 登记进 `ModCatalog.Supported`，不再被报成 Unsupported mod。权重另立 `ModWeights.External`——`ModWeights.Global` 是原版 `mod_setup` 的镜像，模组运行时注册的名字混进去就再也没法拿它和源码对照。权重 0 取自 `addGlobalMod` 的第二个参数，所以触发剧情不计入 GIMMICK 统计。
- 文字框绘制与原生 sequence 剧情共用同一段代码，避免两条来源在版式上慢慢分叉。
- 时间轴上 `custom_episode` 常驻最后一行：整首歌通常只有一两个触发点却要占满一整条轨，夹在 SV / 图片 / 窗口那些逐条对齐着看的轨中间等于把它们劈成两截。同目录有剧本时哪怕还没写下触发也留着这一行，可以直接在轨上插入；剧本不在而谱面里已经写了触发时同样留着，否则那条事件会连同"剧本找不到"的诊断一起消失，变得既看不见也删不掉。
- **琥珀色的剧情区间可以点了。** 此前只有零时长的触发片段那 9 像素能选中，作者想选的"这一段"却几乎必然点在区间上：单击什么都不发生，双击还会被轨道的"空白处双击插入"接走，在已经有剧情的地方又叠出一条触发。现在区间按帧登记命中矩形并归属到拥有它的触发，单击选中该触发，双击不再插入。片段本身仍然优先，拖拽也仍然只从片段上开始。

### SHATTER 的等级与谱师（修复）

- `shatterinfo.json` 此前根本不读。歌曲信息文件只有 `info.json` 与它两种：其余难度的等级与谱师都写在 `info.json` 带 `_N` 后缀的槽位字段里，没有各自的文件；SHATTER 的写在 `shatterinfo.json` 里，字段是单数的 `difficulty_number` / `note_designer`，所以 `info.json` 的第 5 槽几乎总是空的——等级与谱师一路显示为空，信息卡片的难度徽章也就一直是块空底板。
- 手头 206 个谱包里有 18 个带 `shatterinfo.json`，其中 13 个**连 `info.json` 都没有**：曲名、曲师、BPM、封面作者整首歌的信息都在这一份文件里。此前这类歌开出来标题就是难度名 `SHATTER`、曲师为空、BPM 退回默认值。现在 `info.json` 缺失（或损坏）时由 `shatterinfo.json` 顶上全曲字段。
- 全曲字段里 `shatterinfo.json` 排在 `info.json` 之后：它写的往往是"曲名 [Shatter]"这种带难度后缀的变体，`info.json` 写了曲名就该听 `info.json` 的。等级与谱师反过来以 `shatterinfo.json` 为准，那才是这个难度自己的数据。
- 认谱面文件名不认文件内容，`difficulty_name` 只是显示用的标签：真有谱包在里面写着 `EVIL`，那条谱面仍旧是 `SHATTER.vsc`，等级与谱师仍旧归 SHATTER，只有界面上的难度名跟着改。
- `shatterinfo.json` 与 `info.json` 共用同一套读取约束（4 MiB 上限、根必须是对象、解析失败只上报诊断），损坏时只丢它自己那一份，主曲照常显示。
- 直接打开或拖入 `shatterinfo.json` 打开的是同目录的 SHATTER 谱面，因为它只描述这一个难度。打开目录或 `info.json` 仍按原来的难度优先级（ENCORE → FINALE → MIDDLE → OPENING）选谱面，SHATTER 接在这四个之后。
- 顺带修好难度徽章：认不出的等级（有谱包把 SHATTER 的难度写成 `17+++`）此前是先把 LEVEL 底板画下去、再发现数字没有对应帧，于是调用方退回来的文字压在 LEVEL 字样上糊成一团。现在帧号先定下来再落笔，画不出就整块让位给文字。

### 界面字体改为运行时栅格化

- 整套交互界面——viewer、editor、文档与信息卡片——改用随程序集内嵌的 **Noto Sans SC** 真字体，由 `StbTrueTypeSharp` 在绘制时按目标的物理像素密度现场栅格化。此前界面同时拼用四套预烘焙位图图集：DejaVu Sans / Sans Mono（英文）、IBM Plex Sans SC 生成的 Kuroaki UI Sans（中文）、Noto Sans CJK SC（帮助文档）与 Noto Sans Mono CJK SC（统一卡片）。
- **不再存在"图集没烘到这个字"这回事。** 新增界面文案不需要重新生成任何资源，也不需要本地安装字体；此前每次加中文都要重跑烘焙脚本，漏字只能等它在界面上显示成空白才发现。
- **高分辨率导出真正变清晰。** 4K 信息卡片此前是把 64px em 的图集放大 3 倍采样，实测标题笔画的边缘过渡宽度 3.86 像素；现在按 3 倍像素密度直接栅格化，同一位置 1.08 像素。1× 下也从 1.33 降到 0.97。
- 字形缓存是内部实现：用到哪个字补哪个字，按（码位、栅格尺寸、字重）缓存进一张 2048² 动态图集，装满时整体作废重建。重排只在帧首发生，不会移动本帧已提交的字形。
- 排版基线取 OS/2 的 typo 升部（0.88 em）而不是 hhea 的行高升部（Noto Sans SC 上是 1.16 em），与界面既有的 0.89 em 版式约定一致，241 处文字调用不必重新标定。同时去掉了旧图集时代 `(-2, -3) * scale` 的留白补偿魔法偏移。
- 等宽请求的语义改为"不比数字格宽窄"：数字列严格对齐（信息卡片统计面板靠它右对齐），而整格一个 em 的汉字保留自身宽度。此前的实现把格宽一刀切地加给所有字形，汉字会叠在一起。
- 信息卡片标题的自动缩字号此前按 Regular 量宽却按 SemiBold 绘制，两者不一致；换字体后标题末字被裁掉才暴露出来，现已按实际字重量宽。
- 字体按 SIL OFL 1.1 发行，全文见 `ThirdParty/NotoSansSC-OFL.txt`。其保留字体名是 `Source`（字族源自 Adobe Source Han Sans），本项目未使用该名称。`check-help-typography.py` 的"源码树不得含字体二进制"收窄为"字体二进制只能出现在已声明许可的 `Resources/Fonts/`"。
- 场景歌词与游戏 HUD 不受影响：它们继续使用目标游戏自己的字集与精灵字，以保证预览与游戏一致。

### BACKSTAGE 的封面与音频（修复）

- `info.json` 的 `enc_data` 此前只用于覆盖曲名与曲师，`jacket` 与 `audio_id` 两项根本没有读取。结果是 ENCORE 难度拿到主曲的 `jacket.png` 与 `music.ogg`，而不是 `enc_data` 指明的那一套；由于打开歌曲目录时 ENCORE 的候选优先级最高，一进去就会踩到。
- 新增 `SongInfo`：把 `info.json` 建模成带 encore 语义的类型，`Effective(难度)` 逐字段覆盖并对缺项回落主曲。只写了 `audio_id` / `jacket` 的部分 `enc_data` 因此仍保留主曲的曲名与 BPM。
- `hide_backstage` 为真才显示 ENCORE，缺省显示 BACKSTAGE。等级与谱师取主曲的第 4 槽；缺 `difficulty_display_N` 时按 `difficulty_constant_N` 生成（17.1 → "17"）。
- 只有 `info.json` 明确写出文件名时才覆盖，没写就沿用原有的同目录候选顺序，不新增猜测。

### 难度切换

- 歌曲目录带 `info.json` 或 `shatterinfo.json` 且存在多个难度时，左栏 `01 / SOURCES` 出现一排难度方块，显示各自的等级；带 `enc_data` 的 ENCORE 显示为 BACKSTAGE，只有信息槽而没有谱面文件的难度置灰。
- 切换等价于打开目标谱面：音频、封面、VSM 与 BPM 全部重新解析，因此 BACKSTAGE 会换成 `enc_data` 的那一套。`CHART` 行同时显示当前难度与等级。
- 打开 `.sgv.json` 工程时不提供切换：工程钉住了自己的图片、字幕与标记，换难度等于丢掉它们。
- `info.json` / `song.json` 现在可以直接作为文件打开或拖入，等同于打开它所在的歌曲目录；`shatterinfo.json` 也可以，打开的是 SHATTER 谱面。

### 编辑器剪贴板

- `Ctrl/⌘ + C` / `Ctrl/⌘ + V` 与右键菜单的 COPY / PASTE：选择集经系统剪贴板往返，两个编辑器实例之间可以互相粘贴，也可以把这段文本发给别人。
- 载荷是纯文本 JSON 信封，事件以 VSM 源行形式携带，与源文件共用同一套格式和解析器。图片动画在模型里本身就是 VSM 事件，不需要另一套表示；窗口事件一并带走。
- 也接受裸 VSM 事件行，可以从文本编辑器直接贴一段进来。复制方向绝不产出 `mods` 行或注释——原版 `read_mods_file` 见到这两者会崩。
- 粘贴按插入点整体平移，组内相对间距保留；副本一律换新 Id，单条校验失败时整次粘贴放弃。就地编辑与模态里的复制粘贴不受影响。

### 乐曲信息卡片

- 顶栏新增 `CARD`：按当前难度生成信息卡片，可保存 PNG 或直接复制图片到剪贴板。命令行为 `--card chart.vsb --out card.png`。
- 导出分辨率在设置里选 720P / 1080P / 1440P / 4K，默认 1080P；命令行用 `--width` 单次覆盖。卡面始终按 1280×720 逻辑坐标绘制，这一档只决定像素密度，所以放大不会改变版式。
- 六项统计（CHIP / TECH / STREAM / CHORD / BURST / GIMMICK）按原版 `GetSongStats` 计算，长度取解码后的音频实际时长。难度徽章复用游戏原版精灵，与 HUD 共用同一套帧序。
- GIMMICK 项需要演出权重：优先读与谱面同名的 `.vmv` 里 `[mods] weight`，没有时按 VSM 现算（重复区间先展开，表外的 mod 按 `addExtraMod` 默认权重 1）。
- 卡面文字保持英文：卡片是拿来分享的图片，界面语言不应该改变别人收到的那张图。

### 最近打开列表显示歌曲（修复）

- 欢迎页的 `RECENTLY OPENED` 此前直接把文件名当标题。而谱面文件名本身就是难度——打开过的歌在列表里排下来是一列 `ENCORE.vsc` / `BACKSTAGE.vsb`，看不出是哪首歌。
- 现在按同目录的 `info.json` / `shatterinfo.json` 还原成 `曲名 / 曲师 @ 难度  LV.等级`，例如 `Scarlet Death / lexycat @ BACKSTAGE  LV.17`。难度名与等级取自当前谱面自己那一槽，谱面叫 `ENCORE` 而界面显示 `BACKSTAGE` 的规则与左栏难度条一致；目录条目取 `SongFiles.PreferredChart` 选中的那张谱面，也就是点进去真正打开的那一档。
- 缺项逐段省略：没有曲师就只显示曲名，没有等级就只显示难度。信息文件缺失或损坏、路径已被删除时退回原来的文件名写法，解析失败不抛错——最近列表里混进一条坏路径不该让欢迎页画不出来。
- 歌曲信息要读盘，而列表每帧都重画，所以标题按路径缓存在 `Viewer` 里，最近列表变化时整体作废。
- 原生菜单的 `OPEN RECENT` 子菜单一并改掉：`MacMenuBar` / `WindowsMenuBar` 的构造参数由路径列表改成 `RecentSource.Item`（路径 + 标题），标题从同一个缓存取，欢迎页和菜单栏不会各显示各的。macOS 的完整路径仍挂在菜单项 tooltip 上，标题上限由 52 放宽到 72 个字符以适应"曲名 / 曲师 @ 难度 等级"；Windows 没有 tooltip，标题后面照旧接目录，用来区分不同唱片目录里的同名歌。
- 新增 `RecentSource`（标题规则）并把 `SongFiles.Open` 的目录难度候选顺序抽成 `SongFiles.PreferredChart`，打开目录与列表显示共用同一份顺序，不会各挑各的难度。自测覆盖用户给的这条例子、按槽取难度、缺曲师、只有 `shatterinfo.json`、无信息文件与路径已删除六种情形；macOS 菜单冒烟另断言 AppKit 里实际存着的标题就是这首歌（`--smoke-mac-menu`）。

### 打开最近条目不再自动进编辑器（修复）

- 欢迎页点最近的一行、以及原生菜单的 `OPEN RECENT`，此前都会在加载结束后调用 `OpenEditor()`，点开一首歌直接落在时间轴上；同一个"打开"动作，`OPEN PROJECT / FILE`、`OPEN SONG FOLDER` 对话框和拖放进来的文件却停在 viewer。现在三个入口统一：**打开就是看**，要编辑再由 `VIEW → EDITOR / VIEWER` 或工作区里的按钮显式进入。
- 编辑器里有未保存修改时，从最近列表打开仍旧先弹确认，这条没有变。
- 删掉 `LoadFromStartup` 的 `enterEditor` 参数与 `startupEnterEditorAfterLoad` 状态：所有调用方早就传 `false`，只留最近列表那一处传 `true`，这个没有第二个取值的参数只会让人以为还存在"从最近进来直接编辑"的分支。
- 启动页冒烟与 macOS 菜单冒烟相应改成断言落在 viewer，需要编辑器的那段 `Save As` 覆盖改为显式 `OpenEditor()` 之后再跑。

### 音符排序（修复）

- `Chart.Notes` 此前用 `List.Sort` 排序，那是不稳定的内省排序，同一时刻的音符次序会随实现变化。原版按读入顺序遍历音符，jack / chain 的配对与同刻音符的密度权重都依赖这个次序，因此统计结果会随运行漂移。改为稳定排序后，六项统计与参考实现逐值一致。

### 单文件打包改为项目级默认（构建）

- 此前只有 Windows 走单文件：`scripts/publish-common.sh` 按 RID 分支把 macOS 写死成 `single_file=false`，于是 `.app/Contents/MacOS/` 里散着近 200 个文件（运行时程序集、`libSDL3.dylib`、`createdump` 等）。现在两个平台一致，单文件默认写进 `KuroakiGimmick.csproj`：指定 RID 时（`dotnet publish -r <rid>` 以及三个发布脚本）自动产出单个可执行文件，不再依赖调用方手写 `-p:PublishSingleFile=true`。显式传 `-p:PublishSingleFile=false` 仍可覆盖，用于排查只在散装布局下复现的问题。
- 原生库改为自解压：首次运行落地到 `DOTNET_BUNDLE_EXTRACT_BASE_DIR`（默认用户缓存目录），之后复用。运行账户的缓存目录必须可写。
- 发布脚本增加单文件校验：macOS 侧统计 `Contents/MacOS` 里带执行位的文件，必须恰好一个，否则说明 `PublishSingleFile` 没生效；Windows PowerShell 侧检查包根目录没有 `.dll` / `.json` / `.pdb` 残留。`scripts/dev/check-publish.py` 相应改为断言两个 RID 都传单文件参数，并要求 macOS 的 `Contents/MacOS` 只有 apphost 一项。
- 不指定 RID 的 `dotnet publish` 无法单文件，新增 `KUROAKI001` 警告点名这一点；`dotnet build` 不受影响，仍输出散装程序集供调试。

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
