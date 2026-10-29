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

## Windows 编辑器优化（1.3.8）

2026-10-29 继续优化编辑器和窗口生命周期：

- 收起的便签首次恢复时不挂载 WebView；展开后才初始化原生浏览器和正文。
- 已加载编辑器所在窗口隐藏、最小化，或便签收起时，设置 WebView2 的 `MemoryUsageTargetLevel.Low`；恢复显示时切回 `Normal`。这是浏览器的尽力内存回收提示，保留正文、选区、撤销和脚本运行能力，避免挂起脚本影响自动保存。不调用强制 GC 或工作集裁剪。旧运行时不支持该接口时继续使用原有行为。
- 源码模式销毁富文本编辑器及其文档树；回到富文本时才重新解析，清空隐藏 textarea 的正文和原生撤销缓存。源码模式启动、切换文件也不会创建隐藏的富文本视图。
- 连续输入在停顿 120ms 后合并序列化 / 字数统计 / 全文同步；持续输入最长 400ms 同步一次。存储收到更新后继续使用原有 400ms 保存防抖。失焦和页面隐藏会立即提交待同步内容；保存、关闭、切换模式仍同步读取最新正文。切换文件会取消旧文件的待执行回调，保留便签 ID / token 检查。
- 去重相同的字号、工具栏、只读配置和原生背景画刷，避免移动窗口或逐字输入时反复创建对象、调用浏览器并遍历全文 DOM。
- 关闭首个便签 / 文档窗口后重新选择应用的 `MainWindow`，避免应用生命周期继续持有已关闭窗口的托管控件树和文档。

原生互操作使用 Avalonia 公开的 `IWindowsWebView2PlatformHandle` 和 WebView2 的稳定 COM 接口 `ICoreWebView2_19`，不增加 SDK 或浏览器依赖。调用前查询接口、结束后释放此次查询的引用。接口 ABI 对照 Microsoft.Web.WebView2 1.0.1823.32 的 `WebView2.h`；Windows x64 已通过真实运行时读回属性验证。参考 [Avalonia 原生浏览器互操作](https://docs.avaloniaui.net/docs/app-development/embedding-web-content#native-browser-interop) 和 [微软的内存目标说明](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2_19)。

### 编辑器基准

同一 Windows 机器、本机 Edge、2,000 行任务列表正文。每个版本独立启动浏览器 3 次，表中取各指标的中位数。JS 堆在 CDP 请求垃圾回收后读取；输入用 50 次独立的 `insertText` 和事件循环让出模拟，计时包含让出、编辑器处理及布局，随后等待合并同步完成，并检查最后 50 个字符全部保留。

| 指标 | 优化前 | 1.3.8 |
| --- | ---: | ---: |
| 源码模式 JS 堆 | 9,660,276 B | 3,584,384 B（约减少 63%） |
| 源码模式 DOM 节点（含已分离节点） | 32,079 | 4,073 |
| 富文本模式 JS 堆 | 9,527,852 B | 9,656,340 B |
| 50 次输入总耗时 | 1,669.6 ms | 1,005.5 ms（约减少 40%） |
| 50 次输入全文同步次数 | 50 | 3 |
| 全文消息 UTF-8 总大小 | 4,505,675 B | 270,382 B（约减少 94%） |

这些数据反映编辑器 JS 堆和消息负载，不代表整个应用或 WebView2 进程组的常驻内存降幅。富文本模式的 JS 堆接近原水平；主要内存收益来自源码视图、未展开便签、后台浏览器回收，以及关闭窗口后的引用释放。耗时和合并次数随机器、输入速度和负载变化。

```powershell
# 在仓库根目录保存此轮优化前的生成文件
git show c7686f7:src/PaperNote/Editor/editor.html | Set-Content -Encoding utf8 artifacts/windows-editor-before.html
cd editor
$env:PAPERNOTE_EDGE = '1'
node benchmark.mjs ../artifacts/windows-editor-before.html
node benchmark.mjs
```

### 回归结果

- Release 构建：0 警告、0 错误；Windows x64 自包含发布成功。
- 存储 / 系统集成：134 项通过；便签库原生生命周期：11 项通过。
- 编辑器浏览器检查：Chromium / Edge 51 项、WebKit 51 项，合计 102 项通过。
- Windows 原生 WebView 专项：15 项通过，直接读取真实 WebView2 的内存目标值，验证隐藏、收起、最小化及恢复；同时检查延迟初始化、配置去重、源码释放、计时器触发前保存、隐藏状态保存和关闭后的适配器销毁。

```powershell
dotnet run --project tests/PaperNote.UiTests -c Release -- --webview
```

专项需要桌面会话和已安装的浏览器运行时，使用屏幕外窗口，不注入键鼠、不启动系统集成、不读取用户便签。Windows CI 增加便签库与 WebView 原生检查；macOS 的原生专项尚未在本轮实机验证。
