# r19 代码结构与边界

## 设计目标

此次从用户新上传的 SDL_GPU 源码开始整理，而不是从旧 OpenGL 版向前覆盖。保留 GPU 后端的实现与依赖，改变源码组织和对象演出的数据表示。重构保留现有命名空间、单项目入口和原始资产路径；不引入空壳服务层。

目录分层是协作边界，不是单独程序集强制的依赖隔离。`Session` 负责 CPU 会话装配，`SceneRenderer` 负责一帧合成，`GpuDevice` 负责提交；三者不能合并成按曲名分支的大型加载器。

## 加载流程

1. `Program` / `Session.ReadProject` 只解析工程和绝对路径。先应用 CLI 覆盖值，再创建一次 `Session`，避免为了覆盖一个选项重复解码音频和构建粒子。
2. VSB 解码时先按对象名解析扩展 mod 表。表的位置就是二进制编号，不排序、不去重。读取 VSM 时仍使用文本协议。
3. `GimmickCatalog` 查找定义；`GimmickDefinitionReader` 转换旧字段；`GimmickValidation` 校验结构、范围和引用图。
4. `NativeGimmickProfile` 载入资源元数据、图片尺寸和 shader 文本，记录各组件错误。此处不创建 GPU 纹理。
5. 时间轴合并 BPM、mod、逐帧绑定和回调，按固定逻辑 tick 烘焙确定性粒子。跳转只查询这些结果。
6. 场景渲染器看到新会话时，先提交旧引用，再切换纹理、shader 和对象调度器。

显式定义 → 曲包 `gimmick-object.json` → 共享定义 → 同构建内嵌定义。外置文件存在但损坏时应报告错误，而不是悄悄使用另一份配置。新版外置元数据缺失时才兜底；图片/shader 仍需要对应实际文件。内嵌 JSON 不是把专用 loader 藏进程序。

## 时间与顺序

| 数据 | 单位 / 行为 |
|---|---|
| VSC 音符、工程 offset | 毫秒输入，按原解析路径转换 |
| VSM/VSB mod 与逐帧函数区间 | 拍，通过 BPM map 换算 |
| 渲染 time、callback lifetime、fade/tween | 秒 |
| 对象粒子计时与速度 | 固定 60 Hz 逻辑 tick，不随显示帧率改变 |
| sprite 尺寸、原点、变换 | 320×180 逻辑空间；纹理原始像素尺寸另行验证 |

回调使用稳定顺序。`Depth` 大的先绘制；同深度按 `SortOrder` 小的先绘制；两者相同时保留事件和声明顺序。`LatestOnly` 只替换同一 drawing，不能吞掉不同通道或不同回调。

`CallbackLifetimes` 表示时间轴尾部占用，`GimmickDrawing.Lifetime` 表示可见绘制寿命，两者不混用。侧边回调的时间轴尾部是 0.4 秒，可见绘制为 0.3875 秒。为“简化逻辑”把它们统一会改变最后一个回调后的时间轴。

`GimmickScalar` 是有限的标量表达式，不执行动态脚本。先累加常量、秒、拍、mod 和可选资源参数，再应用正弦/余弦、缩放与乘法。验证器在加载时检查别名和逐帧输出依赖环，不将递归留到每帧求值时爆栈。

## 合成顺序

`SceneRenderer.Render` 保留显式阶段：对象背景/粒子、原房间与自定义背景、图片分层、轨道装饰、对象 before-playfield 回调、游玩轨道、固定判定装饰、proxy 合成、游戏 HUD、高优先级图片与文本、公共/对象后处理、GUI 覆盖和白闪。

Custom 对象的 Proxy 在完整场景（包括所有 VSP 层、轨道、HUD、文字）合成后统一执行，对应原版 Draw_74 对 application_surface 的采样。此前的透明 field 分段用于组装未变换源画面；最终复制进 field 后清空 scene，再绘制固定边条和各 Proxy，防止原位画面残留。原生对象仍沿用原来的分阶段合成。

`BeforeRails`、`BeforePlayfield`、`FixedJudgment`、`Gui` 是接口常量。新歌曲复用阶段，不增加曲名阶段。GUI 覆盖的位置不能通过任意移动 shader pass 来“顺便统一”。透明离屏场景的预乘/反预乘路径保持原行为，避免音符 alpha 被乘两次。

## SDL_GPU 组织

| 文件 | 职责 |
|---|---|
| `GpuDevice.cs` | 设备、状态、批次对象与资源入口 |
| `GpuDevice.Submission.cs` | 顶点提交、render pass、swapchain 与命令缓冲 |
| `GpuDevice.Transfers.cs` | 上传/读回、transfer buffer 与 fence |
| `Canvas.cs` / `.Geometry.cs` / `.Readback.cs` | 2D 批次、形状、截图输出 |
| `ShaderCompiler.cs` / `.Translation.cs` / `.Toolchain.cs` | 编译模型、GLSL 接口转换、native 工具链 |
| `Texture.cs` / `Target.cs` / `Shader.cs` | 资源所有权、目标尺寸和 uniform 状态 |
| `Native/Sdl.*.cs` | 按系统、音频、输入、GPU 描述符和命令分类的 P/Invoke |

保留的实现约束：8 个 float 的顶点布局；16 字节 uniform 槽；每批次快照 uniform、sampler 与裁剪；Metal 紧密上传行距、D3D12 对齐上传；读回对齐后去除填充；acquired swapchain 的命令缓冲不能被当作普通未使用缓冲取消；销毁/缩放前提交引用它的队列。

这些细节来自本次输入后端，没有趁结构调整更换 shader 表达式或重写设备策略。源码契约检查对 13 个类型共 272 个成员做 token 对照，忽略注释、空白和控制流补括号；这仍不是 native ABI 或 GPU 运行验证。

## 缓存与错误边界

GPU 对象由 renderer/device 拥有。CPU profile 不持有 GPU 指针。纹理缓存键包含路径、repeat、linear，避免同一路径的噪声采样改变像素贴图的最近邻行为。缓存预算同时计入不同 sampler 变体的纹理。

切换会话前先 `Flush`，实际底层资源释放保持输入后端原有提交逻辑。shader 编译或单张图片失败按组件去重写入诊断，其余有效资源仍可预览；后处理失败时复制当前输入，不留下上一帧的离屏残影。

对象缺少 `UseNativeColorControls` 与明确写 `false` 不等价。旧完整原生对象默认沿用颜色控制；显式 false 才禁用。这让旧 Scarlet 资源继续通过真实 `FX_red` / colour-balance 链路，而不是额外叠红色矩形。

## 报告与测试

报告 schema2 的 `nativeGimmick` 明确记录定义/资源来源、兼容迁移和错误，不把解析成功当成渲染成功。绘制后报告才可能包含实际 shader 错误；截图/导出 strict 模式对错误诊断给出非零退出码。

测试分成源码契约、真实 CPU 自测、真实 GPU 自测、实际谱面同帧图像对照。四者不能相互替代。当前交付已执行的只有源码/数据/脚本级检查，详见 `VALIDATION_R19.md`。
