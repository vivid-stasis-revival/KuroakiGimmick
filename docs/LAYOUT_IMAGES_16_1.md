# v0.1.2 / 16.1：工作区布局与图片导入

## 从 16.0 更新

本升级包以最后提供的 `KuroakiGimmick-v0.1.2-16.0-source.zip` 为基线。在项目根目录（能看见 `KuroakiGimmick.csproj` 的目录）解压更新包，然后重新构建。包内文件为相对路径，没有额外的包裹目录。它是源码增量包，不是 `.app` 或 `.exe` 二进制补丁。覆盖前备份本地自行修改的源码。

```bash
unzip -o ~/Downloads/KuroakiGimmick-v0.1.2-16.0-to-16.1-update.zip
bash scripts/publish-mac.sh
```

Windows 使用 `scripts/publish-windows.ps1`。运行 `dist` 下新生成、名称包含 `v0.1.2` 与 `16.1` 的程序，保留原来的配套 Assets。

## LAYOUT

Viewer 和 Editor 顶部增加 LAYOUT。支持 75%～250% 的 UI 缩放，每步 12.5%；也可以按 Ctrl/Cmd 与加号、减号调整，Ctrl/Cmd+0 回到 100%。默认跟随显示器缩放；可以在 LAYOUT 中关闭。

绘制和鼠标输入使用同一套逻辑坐标换算。应用窗口过小时，会限制有效放大倍率以保留操作空间；面板会提示这一限制。缩小窗口不会清除用户保存的尺寸。实际的 Windows 跨显示器拖动与 macOS Retina 操作仍需要原生验证。

拖动左右侧栏分隔条调整面板宽度。拖动预览下方的横向分隔条改变预览区域与时间轴分配。在 Editor 还可以拖动轨道名和时间网格之间的分隔条，调整名称栏宽度。双击恢复该分隔条的默认尺寸，拖动中按 Esc 撤销这次调整。LAYOUT 提供 PREVIEW、BALANCED、TIMELINE 布局预设，以及 RESET LAYOUT。

这些偏好保存在设备的 settings.json，独立于歌曲和 VSM。预览区始终保持场景比例。UI 大小、预览区大小、Scene resolution 是三个独立控制，调整 UI 不改变 MP4 输出尺寸；Scene resolution 会影响预览质量和 GPU 开销。

## 图片拖入

先进入 Editor，打开当前谱面。将静态 PNG、JPEG、BMP 或 TGA 拖入编辑器窗口，或使用左侧 `+ IMAGE / DROP IMAGE`。最多一次 32 张；单文件压缩体积和单图解码体积各限 64 MiB，单边最多 16384 像素，总批次解码体积最多 256 MiB。导入前检查实际解码，不只读取文件头。GIF、WebP、SVG、HEIC 及动态图导入未实现；不会把它们静默转换成动画条带。

使用当前 E 标记，或没有标记时使用播放游标的拍点。图片默认在场景中心出现，大图按比例缩小到场景的约 60% 以内。拖放位置不决定场景坐标；本版没有新增画布移动/旋转手柄。

如果 `!obj` 不是 `obj_custom_gimmick`，先确认 `USE CUSTOM OBJ + IMPORT`。取消不改动文档；确认可能改变原来的对象专属演出。导入分配不会冲突的图片标识符，新增 VSP 图层/静态图片声明，以及 imgx、imgy、imgscalex、imgscaley、imgrot、imgalp 参数。透明度在插入拍点前初始化为零，在插入拍点设为一。之后从参数轨道修改位置、大小、旋转、透明度等。

一个批次作为一次撤销操作，同时恢复 VSP 与 VSM。导入先创建私有临时资源副本，原始图片、原 VSP 和原 VSM 不被覆盖。

## 保存和导出

保存编辑工程时生成 `.sgv.json`、`.editor.vsm`、`.editor_cgmk_config.json`；存在 VSP 内容时另生成 `.editor.vsp`。新导入的图片复制到 `<工程名>.editor-assets/`。原有图片引用保留，并改为相对伴随 VSP 的路径；这种编辑工程不一定自包含原歌曲的所有资源。

`Chart Folder` 从当前内存中的 VSP 和 VSM 导出，包含尚未保存的图片导入；外部图片收集到输出目录的 resources，重写输出 VSP，不使用失效的临时缓存路径。未导入新图但读取了原有 VSP 时，也保留原资源引用关系。

`VSM` 和 `VSM + cgmk config` 仍然只输出名称标明的文件，不包含图片或 VSP，导出页会提示。完整收集图片应选择 `Chart Folder`。游戏端仍需对应的 Custom Gimmick/扩展；预览器不安装扩展，也不宣称图片格式在所有目标运行环境均被验证。

新增图片可撤销、保存、重新打开；本版没有提供资源浏览器中删除图片实例或编辑已有 VSP 文本的 UI。可以修改相应 VSM 参数来隐藏或调整图片。

## 验证

本次增量包提供真实 C# CPU/IO 自测，但交付环境没有可用的 .NET SDK；未执行 C# 编译、C# 自测或 Metal/D3D12 实机交互。离线检查与模拟发布只验证源结构、数学映射和包布局，不代替编译或运行。

本机先构建再执行自测：

```bash
bash scripts/verify.sh
# Windows: ./scripts/verify.ps1
```

测试代码覆盖 DPI/逻辑坐标换算、布局范围、偏好读写、图片解码、批量导入、撤销重做、VSP 读写、保存后重新打开、Chart Folder 资源重定位、VSM-only 提示及既有导出测试。
