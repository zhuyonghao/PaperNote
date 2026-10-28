# 编辑器启动底色修复（2026-10-28）

便签与 Markdown 文档共用的 EditorPane 在创建 WebView 前设置对应纸色/画布色；导航 HTML 在首帧前预设主题、纸色与 compact 模式。主题配置先于正文加载，动态切换时同步原生背景与页面画布色。

验证：Release 发布通过；14 种原生启动背景组合通过；浏览器编辑器 44 项检查全部通过（含显式画布色的主题切换检查）；.NET 存储与集成检查 88 项通过。独立目录 Markdown 窗口正常加载。

修复包：artifacts/editor-no-flash/，同步更新 artifacts/screenshot-restored/ 的程序 DLL/PDB。
更新前的程序 DLL/PDB：artifacts/before-editor-no-flash/。便签数据不做替换。
