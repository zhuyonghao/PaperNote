# 性能与内存优化

2026-10-29，Windows，.NET SDK 10.0.402，Release 构建。

## 改动

- 标题按行读取并缓存，正文改变时失效，避免每次访问标题都拆分整篇 Markdown。
- 卡片摘要最多读取 320 个 UTF-16 代码单元及省略号，截断时保留完整代理对；完整 `Preview` 属性保持原有文本语义。摘要不再把长正文完整复制进文本控件。
- JSON 直接从流读取、向临时文件流写入；保留 UTF-8 / UTF-16 / UTF-32 BOM 兼容、400ms 自动保存、原子替换、备份和损坏数据保护。仅在保存状态改变时广播通知。
- 便签库复用未改变的卡片，搜索输入合并至 220ms 后刷新。隐藏时停止刷新并释放卡片控件与缓存，重新显示时读取最新数据，保留筛选和多选状态。
- 桌面便签只在颜色变化时重建配色按钮，只为相关便签派发界面更新，避免输入时重复发送相同颜色到 WebView。
- 编辑器字数统计不再构建字符数组；工具栏缓存按钮引用，每次事务更新一次状态，每个格式只计算一次激活状态，并减少无变化的 DOM 属性写入。

## 基准

测试正文为标题加 2,000 行中文、Markdown 和 emoji。标题读取 200 次，卡片摘要读取 20 次；加载和保存使用 100 张这样的便签，保存包含备份。序列化、正则、JIT 和缓冲池先预热一次。

| 操作 | 优化前分配量 | 优化后分配量 |
| --- | ---: | ---: |
| 重复读取标题 200 次 | 38,474,680 B | 40 B |
| 卡片摘要读取 20 次 | 10,969,416 B | 108,680 B |
| 加载 100 张长便签 | 75,586,552 B | 12,850,616 B |
| 保存 100 张长便签 | 31,323,256 B | 5,928 B |

优化前卡片读取完整 `Preview`；优化后卡片使用 `GetPreview(320)`。完整摘要属性仍可用于需要全部文本的调用方。

分配量通过 `GC.GetAllocatedBytesForCurrentThread` 测量，包含少量测量工具开销。它反映当前线程一次操作的托管分配，不能等同于进程常驻内存或 WebView 原生内存。加载分配约减少 83%，保存约减少 99.98%，卡片摘要约减少 99%。耗时受磁盘与系统负载影响，基准命令会一并打印。

```powershell
dotnet run --project tests/PaperNote.Tests -c Release -- --benchmark
```

## 回归验证

```powershell
dotnet build PaperNote.slnx -c Release
dotnet run --project tests/PaperNote.Tests -c Release
dotnet run --project tests/PaperNote.UiTests -c Release
cd editor
npm run build
$env:PAPERNOTE_EDGE = '1'
npm test
```

原生检查在独立临时数据目录创建屏幕外测试窗口，直接验证卡片对象复用、搜索、多选、隐藏释放与重开恢复；需要可用桌面会话，不注入键盘或鼠标输入，也不启动系统集成或 WebView。浏览器测试每个引擎 46 项；Windows 可用本机 Edge 运行 Chromium 项目，WebKit 需先执行 `npx playwright install webkit`。原有粗体工具栏用例改为明确选中文本并确认选区，避免 Windows WebKit 下 `Ctrl+A` 未选中文本导致的误报；该问题也能在优化前的编辑器包中复现。
