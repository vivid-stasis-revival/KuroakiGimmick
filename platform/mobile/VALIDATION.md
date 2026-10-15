> 历史交接记录：以下是上传包作者对 2026-10-11 版本的记录，并非当前集成分支的复验结果。当前 CI、外置素材导入和触摸改动尚未完成平台构建或实机验证。

# Mobile r1 验证记录 — 2026-10-11

完成 Android ARM64 APK 和 iOS ARM64 未签名 IPA 构建。最终包的大小和 SHA-256 见 `BUILD-INFO.json`。

- Android：API 35 ARM64 平板模拟器，安装、.NET/SDLActivity 启动、Vulkan 设备创建、中文/英文界面绘制和触摸打开系统文件选择器通过。系统选择器能看到 K/G 的只读 DocumentsProvider。
- iOS：iOS 26.2 iPad ARM64 模拟器，Mono AOT、SDL3 Metal、UIKit 逐帧回调、横屏、中文界面通过。通过应用目录选择器导入含 24 个音符和 12 秒 Ogg 的 ZIP，确认相对音频路径、预览画面和播放时间推进；通过软键盘保存 `Exports/project.sgv.json`，验证保存后的 chart/audio 引用指向解压后的歌曲目录。
- MobileChecks：52 个 Android/iOS shader pack 载入、跨格式拒绝、损坏拒绝、uniform 布局；ZIP 相对路径、路径穿越拒绝与失败清理；保存目录限制、字面启动参数和 8 个嵌入 gimmick manifest 检查通过。
- 桌面 CPU Regression：全部通过。
- Switch 托管分支：0 warning / 0 error，独立输出目录构建通过。
- 构建脚本 Python/Shell 语法检查通过；APK 签名、IPA ZIP 完整性、ARM64 真机架构和两包资源路径已核对。

iOS 首版采用应用目录浏览器。系统 UIDocumentPicker 在此 SDL/模拟器组合中出现空白，未列为可用功能；先通过“文件”App 把歌曲或 ZIP 复制到 K/G 应用目录，再在 K/G 内选择。

尚未测试真实 Android/iPhone/iPad：真机 IPA 侧载签名、实际扬声器声音、复杂编辑手势、后台/前台切换、长谱面内存和各机型性能。模拟器截图中的 FPS 不能作为手机性能承诺。音频支持 Ogg/Vorbis；MP4 导出和 LIVE 多窗口未启用。
