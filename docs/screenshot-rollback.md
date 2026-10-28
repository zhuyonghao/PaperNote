# 截图版本回退（2026-10-28）

已从界面重新设计之前的历史记录恢复 MainWindow.cs、AppTheme.cs、UI.cs 和 App.axaml 原文，对应淡紫侧栏、工作空间面包屑、长搜索框与浅色纸张卡片。

- 当前改动备份：artifacts/rollback-before-screenshot-20261028-2258/
- 可运行程序：artifacts/screenshot-restored/PaperNote.exe
- Release 发布成功；现有 88 项检查通过。
- 使用正式便签的独立副本进行了窗口检查，正式 notes.json 未被回退或替换。
- 后续新增的 LibrarySelection 与 LibraryCardsPanel 保留在源代码中，但恢复后的便签库不再使用它们。
