# 通用对象定义

## 文件与发现顺序

对象定义描述行为，曲包资源 manifest 描述实际文件，二者不能混为一谈。一个 `obj_*` 名称只用于查找数据；任何名称都走相同加载、调度和渲染代码。

定义优先级为：工程的 `GimmickDefinition` / CLI `--gimmick-definition`、曲包根目录的 `gimmick-object.json`、`Assets/Gimmicks/<objectName>/manifest.json`、程序内嵌的同一份定义。`--profile core` 禁用对象演出；其他别名必须在配置 `profileAliases` 中声明。

对自动模式，声明的 `objectName` 必须匹配谱面对象名。显式 alias 可以切换对象配置，但不能在编译代码中增加 alias 到特定 loader 的分支。

## 最小示例

`Samples/ReusableObject/` 同时包含 `ENCORE.vsc`、`ENCORE.vsm`、`gimmick-object.json`、示例 PNG 和 shader，无需下载外部歌曲或游戏素材。

```bash
dotnet run -c Release --no-build -- Samples/ReusableObject/ENCORE.vsc
```

示例对象 `obj_reusable_demo` 声明一个带缩放/淡出的 `emit_pulse` 回调、120 BPM、一个正弦逐帧输出和通用 shader uniform 绑定。修改示例对象名及 VSM 中的 `!obj`，无需新增 C# 加载器。已有能力范围内可以进一步改精灵、回调名、表达式和合成位置。

## 数据组成

| 字段 | 含义 |
|---|---|
| `version`, `objectName`, `displayName` | v1 定义、查找身份和显示标签 |
| `extraMods` | 原顺序扩展 ID 表；VSB 的编号大于 128 时按此位置读取 |
| `defaults`, `modAliases` | 默认值和参数别名；不允许引用环 |
| `initialBpm`, `fixedBpm` | 初始化 BPM 与固定 BPM 能力，二者语义不同 |
| `perFrameBindings` | 函数名到输出参数/标量表达式的映射 |
| `callbacks`, `callbackConstraints` | 回调的一个或多个 drawing 与事件值约束 |
| `callbackLifetimes`, `callbackFades` | 时间轴尾部与回调驱动的淡入/淡出输出 |
| `overlays`, `background`, `particleEmitter` | 常驻层、带可选 shader 的背景、确定性粒子 |
| `resourcePack`, `requiredSprites` | 外部资源包与必需的尺寸/帧数契约 |
| `sprites`, `textures`, `shaders` | 精灵帧、候选贴图与 GLSL 源路径 |
| `postModes`, `postModeMod` | 参数选择的后处理模式与 uniform/sampler 绑定 |
| 其他渲染能力布尔值 | 公共后处理、轨道装饰、环境粒子等可组合行为 |

`extraMods` 不按字母排序，也不去重。相同文本名称可以位于不同二进制编号位置，删除重复项会让后面的 ID 全部错位。定义加载必须先于 VSB 扩展解码，而不是到绘制阶段才找配置。

## 绘制与时间

Drawing 的 `stage` 仅允许：`before-rails`、`before-playfield`、`fixed-judgment`、`gui`。`Depth` 降序、`SortOrder` 升序；完全相同的键保留声明顺序。

`Lifetime` 是可见时长，单位秒。`CallbackLifetimes` 是可选的回调尾部，必须不短于该回调所有 drawing。`LatestOnly` 的覆盖范围是当前 drawing，不是整个回调表。`EventValue` 可用于同一回调按通道选择不同 drawing。`SuppressRanges` 按回调产生时间过滤，不按当前渲染时间随机清空状态。

Tween 只作用于 x/y/scaleX/scaleY/alpha/angle。相同 delay 保留输入顺序，使用现有 easing。跳转时直接求值，不依赖“上一帧播放到了哪里”。

粒子间隔和速度采用原 60 Hz 逻辑 tick，发射与生存上限由验证器限定；拖动时间轴和视频导出使用同一固定种子结果。这保证应用自身的可重复性，不等于已经复刻游戏引擎的随机序列。

## 资源与 shader

路径由资源解析器做边界检查，不能通过 `..` 或符号链接逃出对应资源根目录。scope 为 `definition`、`pack` 或 `shared`；默认相对资源包，没有 pack 时相对定义目录。图片文件尺寸必须匹配 manifest，帧数、解码尺寸与累计预算有上限。

`resourcePack: "astellion/manifest.json"` 是保留旧曲包的配置数据，不意味着 renderer 分支识别这个路径。旧资源包的 `Version` / `Sprites` / `Background.Parameters` 等结构字段继续兼容大小写；真正的参数 ID 和资源名称仍按原精确名称引用。

Shader 的 uniform 由 1 至 4 个 `GimmickScalar` 组成；sampler 指向声明的纹理/精灵。标量可组合常量、时间、拍数、mod、资源参数、sin/cos、缩放与乘法，不支持任意表达式脚本。当前输入后端的 shader 声明转换覆盖已有的 float/int/bool、vec2/3/4 和 sampler2D；超出范围应扩展通用编译器并加测试，而不是为一首歌添加特殊 GPU 路径。

Shader 文本使用当前 SDL_GPU 转译链路。颜色、glow、原房间 FX 的默认与合成顺序不通过“全屏染色兜底”替代。

## 旧配置兼容

`UseNativeColorControls` 为 nullable bool：缺省沿用旧完整原生对象的启用语义；显式 false 才关闭。Scarlet 缺字段时不能默认禁用 `FX_red`。ASTELLION 配置显式 false，保留它自己的后处理路径。

旧 `perFrameFunctions` 通过 `Assets/Catalog/legacy-per-frame-bindings.json` 转成 `perFrameBindings`。显式新绑定优先；没有转换模板的旧函数记录错误，不能装作完整支持。

共享定义和目录表同时内嵌，防止只更新程序集但保留旧 Assets 时扩展编号表消失。外置有效文件仍优先；存在而损坏的文件不会被静默覆盖。缺失外部 shader、图片、音频仍需要补齐对应素材。

## 接入新能力的规则

当需要的行为能由上述字段表达，只改 JSON 和曲包素材。不能表达时，先新增可复用数据模型和验证，再补时间轴或渲染组件，并加任意对象名的测试。禁止增加 `if (song == ...)`、对象名到专用 renderer 的 switch 或把原有专用类换个“通用”名称继续调用。
