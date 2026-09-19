> 历史文档：以下内容来自输入源码及其既有修订，不能作为 r19 的编译、运行或画面验收结果。当前状态见 [VALIDATION_R19.md](VALIDATION_R19.md)，当前结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

# 原房间 FX 配置

r8 已内置这次 Mac dump 的 7 份配置，可直接用 ROOM FX 选择。外部 profile 仍可覆盖。详见 ROOM_FX_R8.md。

有 shader 还不够：FX_chroma、FX_contrast 是房间层名。真实 filter 类型、其它参数、噪声资源、启用状态与 depth 决定画面。r7 可以读取这些数据，不用猜一套默认值。

## 导出与载入

1. 在 UTMT 中打开原游戏数据。Mac 通常为 `/Applications/VIVIDSTASIS.app/Contents/Resources/game.ios`。
2. 运行源码包 `scripts/assets/Dump_Room_FX.csx`，选择输出目录。CLI 环境可设置 `SCARLET_FX_OUTPUT` 来代替目录选择框。
3. 输出 `Scarlet_FX_profiles/`，每个含相关层的房间一个 `房间名.fx.json`，另有 INDEX.txt。选实际游玩房间；不同歌曲/房间可能不同，不要随便选 ASTELLION 房间代替普通自制房间。
4. 在查看器打开歌曲，使用 `+ FILES / RESOURCES` 附加对应 `.fx.json`。或复制为谱面旁的 `gimmick-fx.json` 自动读取。保存工程会保留相对路径。

```bash
dotnet run -c Release -- --inspect /path/FINALE.vsm --fx-profile /path/room.fx.json
```

脚本只读取资源，不修改或保存游戏数据。仅导出 FX_chroma、FX_contrast、FX_red、FX_hue、LBG 的静态房间配置，不打包全游戏贴图。已通过编译和模拟 UTMT 资源导出/查看器回读；实际 UTMT 打开用户 game.ios 的导出尚未执行。

LBG 通常由 Custom Gimmicks 在运行时创建；静态 room 导出不会自动包含它。房间创建代码或 Step 对其它 FX 的运行时修改也不会被这份静态快照捕获。要匹配这些参数，仍需相应运行时数据。

## 读取规则

顶层为 `{"format":1,"room":"房间名","layers":[…]}`。每层字段：

| 字段 | 形式 |
|---|---|
| name | 原层名，例如 FX_chroma |
| filter | 实际 EffectType，例如 _filter_heathaze；不能从层名推断 |
| depth | 原 LayerDepth |
| visible / enabled | 原 IsVisible / EffectEnabled，缺省为 true |
| parameters | uniform 名到数值、数值数组、或 sampler 字符串 |

重复的数值属性合并为数组；颜色 #AARRGGBB / #RRGGBB 转为 RGBA 的 0..1 数值。sampler 按配置所在目录读取同名路径，或匹配 Assets/GameFX 中原始通用噪声资源名。缺少非通用贴图时需把原 PNG 一起放到相对路径。

现有绑定：

| 层 | 支持的实际 filter | VSM 覆盖 |
|---|---|---|
| FX_chroma | _filter_heathaze / _filter_underwater | g_Distort2Amount ← fx_chroma_distort，其余值来自配置 |
| FX_contrast | _filter_colourise | g_Intensity ← 1 - fx_contrast，原 g_TintCol 来自配置 |
| FX_red | _filter_colourise / _filter_colour_balance | 可见性 ← fx_red，强度 ← fx_red_intensity，颜色 ← recolor 的 curcolor |
| FX_hue | _filter_hue | g_HueShift / g_HueSaturation ← 对应 mod |
| LBG | _filter_heathaze | 两组 scale / amount ← BG_ditortScale / BG_ditortAmount |

如果实际导出 filter 是别的类型，查看器会明确报告缺少匹配绑定，保留其它有效层；不会把不同 filter 强行互换。归一化 JSON 要求所有 source shader 所需值齐全，非有限值或缺少贴图会报告错误并跳过该层。

原噪声贴图与源码可复现公式，但没有原游戏进房时间，所以动画相位从歌曲零点开始。具体范围见 GIMMICKS_R7.md。

colour balance 使用三组 g_ColourBalanceShadows/Midtones/Highlights（各 3 个数值），不接受 colourise 强度/颜色参数。
