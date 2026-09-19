# v0.1.2 / 16.1 track-help z-order hotfix.4

修复 Editor 轨道悬停文档与布局 resize grip 之间的闪烁。

- 布局分隔条始终在 workspace 层正常绘制。
- 轨道说明卡延迟到 `FinishUiFrame`，在分隔条之后绘制，因此视觉上稳定覆盖分隔条。
- 分隔条不再根据说明卡的显示/隐藏状态反复消失和出现。
- 真正的 modal/blocking overlay（Settings、F1 Docs、Layout、导入/导出窗口等）仍会隐藏分隔条。
- 未修改主题、字体、VSM、图片 gimmick、EXPORT、窗口移动或游戏渲染。

当前交付环境无 .NET SDK；已运行 `scripts/check-authoring.py` 和 `scripts/check-ui-presentation.py` 的离线检查，但未执行 C# 编译。
