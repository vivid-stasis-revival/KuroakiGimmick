# v0.1.1 editor-preview.8：VSM / Custom Gimmick 内置手册

## 更新范围

本次只补充文档索引、轨道说明、手册界面及所需的中文字形。不新增 VSM/VSP/VSV 解释执行能力，不改变已有事件、解析器、音频时间轴、游戏绘制、窗口运动或保存格式。

来源只有本次提供的两份附件：

- `Assets/Documentation/Sources/vsm格式说明.md`
- `Assets/Documentation/Sources/Custom Gimmick说明.md`

两份原文件按字节保留，索引保存来源文件名、行号和 SHA-256。原始词法拼写、标点和空字段可以通过“原文”查看，不以编辑器的旧版推测替代。

## 安装

已有 preview.7：退出程序，将 preview.7 → preview.8 热修覆盖到工程根目录，再重新构建。

```bash
cd ~/KuroakiGimmick-v0.1.1
unzip -o ~/Downloads/KuroakiGimmick-v0.1.1-preview.7-to-preview.8-vsm-manual-hotfix.zip
./scripts/publish-mac.sh
```

也可以把完整 preview.8 源码包解压到新目录，再运行 `bash scripts/publish-mac.sh`。Windows 构建仍使用原有 `scripts/publish-windows.ps1`。第一次构建需要 .NET 8 SDK 和项目原有依赖；本次没有增加或更新 NuGet 包。

启动 dist 中本次新生成的版本，并保留随发行目录提供的 Assets。首次查询手册时正常日志为：

```text
[docs] editor-preview.8: 292 entries loaded from supplied documents
```

手册 JSON 同时嵌入程序集，避免外部旧 Assets 覆盖新版解释；原 Markdown 与 JSON 仍随发布目录保留，便于审阅。字体仍然需要发布目录里的 PNG/JSON 图集。

## 操作

### 轨道提示

轨道继续显示实际原始名字，不改名、不翻译 identifier。

鼠标悬停显示文档中的简短作用；持续按住 W 展开完整字段说明、适用条件与来源行号。内容很长时保持 W 并滚轮翻阅。按 F1 打开当前悬停名称对应的手册条目。

`imgx_[图像名]`、`textX_[tid]b`、`notealpind[lane]`、`twx[id]` 等模板会匹配实际名字。lane 范围 0～6、tw 的 id 范围 1～4 来自附件。tid 与图片名字允许下划线，不通过只截取最后一段来判断。

如果任意 tid 与末尾 b 参数模板同时可匹配，将标出歧义，优先展示更具体的模板，但不修改运行时目标绑定。大小写近似匹配同样只用于查询提示，不新增游戏别名。未在两份附件中查到的名字明确显示“附件未收录”，不会填入猜测的默认值或单位。

现有窗口轨道说明保留，并标注它们来自编辑器既有实现而不是这两份附件。

### 可搜索手册

Editor 右上角 `DOCS` 或键盘 F1/H 打开手册；打开时暂停播放。

- `搜索` 或 Ctrl/Cmd+F：按原始名字、中文作用、obj、mpf、配置项、章节搜索，支持输入/粘贴中文。
- 左侧 `<` 与分类按钮切换分类；`清空` 同时清除查询和分类。
- 鼠标停在左侧目录时滚动目录，其他正文区域滚动正文。↑/↓选择，PgUp/PgDn 翻正文，Home/End 到正文首尾。
- `原文` 打开带原始行号的完整附件；不是从网络重新下载的版本。
- `选用名字` 仅为左侧待添加 gimmick 选择名称。带占位符的条目先要求输入实际名字。不会自动添加事件、切换 !obj、写磁盘或启动窗口。
- Esc/F1/H 关闭手册。搜索输入框打开时，Esc 先关闭输入框。手册输入不会穿透到下面的播放或时间轴编辑快捷键。

示例查询：`velocity`、`旋转`、`spinProxies`、`JACKET_MANAGE_MODE`、`imgx_background`、`textX_字幕b`、`addVeloTween`。

## 收录范围

合计 **292 个索引条目，17 个分类**：205 个 mod 名称/模板、29 个 obj、12 个不同的 mpf 名称、10 个配置项，另有 16 个语法条目、15 个章节、3 个使用/边界说明和 2 份完整原文。

基础 VSM 包括文件配对、语句格式、节拍、重复范围、duration、easing、value1/value2、`_`/573613、proxy、!proxies、!obj、其它 !metadata、mpf、原始示例、基础参数、轨道参数和 obj/mpf 对照。

Custom 包括文档约定、配置、单/多文件字幕、背景、VSP 图层与 static/animated、图片参数、滤镜、粒子、轨道/Note 参数、其它效果以及 VSV 变速语句。

VSM 的时间字段使用节拍；VSV `addVelo` / `addVeloTween` 的 time/timeEnd 使用毫秒。图片参数中的 ms、msx，以及其它效果自己的参数单位分别保留，不把所有数字强行按节拍解释。参见原文 `vsm格式说明.md:7–33` 与 `Custom Gimmick说明.md:315–341`。

## 来源边界与原文差异

“已收录说明”不等于“已实现效果”。该手册不用于自动注册运行时参数；兼容报告仍然生效。文档说某项能在游戏中使用，不代表当前预览器已实现或已验证它。

配置默认值存在原文差异：Custom L17/L37 写无配置或未填写时按 true，表格 L24/L31～33 则有 plaudite 和 false。这些说法同时呈现，不擅自决定新的默认值。

基础表总体要求 proxy=-1，但 hom 的行说明写“proxies不能为-1”。单独标出例外和原字段拼写，选用 hom 名称时不会由手册自动推断目标。

静态图片声明的两个尺寸参数原文都写“图像初始宽度”；动态图声明则写宽度/高度。原文保留，并提示差异，不悄悄修正文档。`ditortedBG`、`BG_ditort`、`col_convertion`、`wigglr`、`filcker` 等名称保持原拼写。

缓动章节保留附件列出的命名示例及引用；本次未抓取附件外的完整缓动列表。未提供具体算法、默认值或效果描述的单元格明确保持“原文未说明”。

`vsm格式说明.md:173` 引用了 **Extra Gimmicks(NO COMMENTS).xlsx**，但本次没有该附件。各 obj 独有 gimmick 的外部表格内容没有编造补入；29 个 obj 和原表列出的 mpf 关联已收录。

## 字体

沿用 preview.7 的比例 Noto Sans CJK SC Regular/Bold、17 单位正文、22 单位标题、独立 em/baseline 度量和不透明高对比背景。图集覆盖扩展为每个字重 **954 个字形**，包含这两份原文、整个索引和当前帮助界面使用的全部字符，不宣称覆盖任意 Unicode。

只包含生成的 PNG 字形图集与 JSON 度量，不分发 TTF/TTC/OTF 字体二进制。字体许可说明仍保留在 ThirdParty。Viewer、轨道 identifier 和游戏 HUD 的原字体文件未改。

## 维护与验证

两份原文位于 Sources，生成器位于 `scripts/dev/build-vsm-manual.py`。维护时先改来源，再重建索引；新增字符后用 `scripts/dev/build-editor-help-atlas.py` 在安装了对应字体的维护环境重新生成图集。正常构建不需要 Python 或系统字体。

```bash
python -B scripts/dev/build-vsm-manual.py
python -B scripts/dev/check-vsm-manual.py
python -B scripts/dev/check-help-typography.py
python -B scripts/dev/check-source.py
```

`check-vsm-manual.py` 独立逐行核对 **244/244 个表格条目**及其字段，检查原文全文/行号/哈希、模板范围、原文差异、未提供内容提示、两套图集字符覆盖、嵌入资源和可重复生成。报告在 `docs/validation-vsm-manual.json`。

另提供 C# `ManualSelfTest`，随原 `--editor-self-test` 入口执行。当前交付环境没有可用的 .NET SDK，**没有执行 C# 编译、C# 自测或 macOS/Windows 原生交互**。离线检查与图集覆盖不代替这些验证；详细记录见 `VALIDATION_V0.1.1.md`。
