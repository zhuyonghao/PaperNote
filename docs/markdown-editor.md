# Markdown 文档编辑

文档窗口默认所见即所得，直接编辑正文；没有对照或预览模式。源码入口作为可选编辑方式保留。

## 功能

- 从便签库左下角、应用 / Dock / 托盘菜单、桌面便签选项进入独立 Markdown 编辑器。
- 新建、打开 `.md` / `.markdown` / `.txt`，保存原文件，另存为，同时编辑多个文档。
- 正文复用现有 Markdown 富文本编辑器及格式工具。源码模式也可插入格式、链接，继续列表和缩进。
- 文件文档独立于便签自动保存：标题用 `*` 标记未保存；关闭、替换当前文件、退出时可保存、放弃或取消。
- 本地原子保存、外部修改检测、保存失败保留当前状态；保留 UTF-8 BOM，源码编辑保留 CRLF。
- 更换文件时重新分配桥接身份并清空撤销历史，防止旧消息和撤销内容写入新文件。

## 2026-10-27 验证

- .NET 应用构建和 macOS ARM64 发布成功，应用通过 ad-hoc 签名验证。
- 38 项存储与集成检查通过，包含 10 项新增文件读写、另存为、失败保留、外部变更和换行 / BOM 检查。
- `PAPERNOTE_EDGE=1 npm test -- --workers=1`：46 项浏览器检查通过，覆盖 Chromium（本机 Edge）与 WebKit。并行运行曾在浏览器清理阶段超时，单工作进程的完整运行正常完成。
- 隔离 macOS 原生实例验证了打开文件、直接编辑正文、Markdown 粘贴转换、保存原文件、另存为、未保存关闭提示和取消关闭。
- 测试文档和截图位于 `artifacts/markdown-qa/`。未向正式便签写入测试内容。
- 旧应用和正式数据备份于 `artifacts/backups/20261027-195643-pre-markdown/`；更新前退出后，16 张正式便签的 JSON 全部字段及文件字节均与备份一致。
- 已更新 `artifacts/osx-arm64/纸间.app`，恢复已有桌面便签，并从便签库打开新的所见即所得 Markdown 窗口。

Windows 原生文件对话框与应用交互尚未进行实机验证。富文本支持范围沿用现有编辑器，原始 HTML、公式、脚注等扩展语法请使用源码模式。

![所见即所得编辑器](../artifacts/markdown-qa/wysiwyg.png)


## 2026-10-28：1.3 打开方式与界面

- 顶部改为 52px 文档栏和 49px 格式栏，合并原有模式切换区域。新建 / 打开采用图标，另存为放入文档选项；底部显示保存状态、字数和文件格式。
- 正文居中、限制行宽、调整中英文字号与段落间距，继续默认所见即所得。桌面小便签仍使用紧凑样式。
- macOS 文档声明覆盖 `public.markdown` 与 `net.daringfireball.markdown`。本机运行时将 `.md` / `.markdown` 解析为 `public.markdown`；仅声明旧类型时不能进入推荐打开方式列表。已通过 Launch Services 的接受检查和候选应用列表验证四种 Markdown 扩展名。
- 访达中实测“打开方式 → 纸间”：应用未运行时打开 `.md`，运行中再打开 `.markdown`，重复打开同一文件并关闭一次后返回上一份文档，确认没有创建重复窗口。
- Windows 加入当前用户的 OpenWithProgids / RegisteredApplications 注册和文件路径启动处理；macOS ARM64、Windows x64 均完成发布。Windows 系统关联的实际交互仍需实机验证。
- 49 项 .NET 存储与集成检查、46 项 Chromium / WebKit 编辑检查通过。新增的真实子进程检查覆盖含中文、空格和 `#` 的文件路径转发。
- 正式应用更新到 1.3.0。16 张便签与 `artifacts/backups/20261028-pre-openwith-ui/notes.json` 的全部字段一致，测试文档未写入便签库。

可在 macOS 发布后用系统 API 检查文件关联，不会变更默认打开应用：

```sh
swift scripts/verify-macos-openwith.swift artifacts/osx-arm64/纸间.app /path/to/example.md
```

文件激活处理依据 [Avalonia IActivatableLifetime 文档](https://docs.avaloniaui.net/docs/services/activatable-lifetime)，Windows 注册依据 [Open With 文档](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-include-an-application-on-the-open-with-dialog-box)。

![1.3 所见即所得编辑器](../artifacts/markdown-qa/wysiwyg-v13.png)

## 2026-10-28：1.3.1 空桌面与粘贴修复

- 无桌面便签时保持后台，启动、再次激活、显示 / 隐藏便签以及关闭最后一个便签或文档均不再自动弹出便签库。菜单中的“打开便签库”和搜索入口仍可主动打开。
- 粘贴时使用 Markdown 解析结果识别格式，兼容同时包含 `text/plain` 和 `text/html` 的剪贴板。补齐表格、有序列表、链接、斜体、删除线、图片和分隔线等语法；在代码中粘贴保持原文，无 Markdown 语法的富文本继续保留 HTML 格式。
- Chromium / WebKit 共 70 项编辑器检查、49 项 .NET 存储与集成检查通过；新增粘贴用例在旧版本复现问题后通过修复验证，涵盖选区替换、单步撤销、源码往返和代码块原文粘贴。
- macOS 独立测试实例使用 `artifacts/paste-fix-qa/data`：空集合启动无窗口；手动打开便签库正常；原生 WKWebView 中粘贴带 HTML 的 Markdown 正确渲染并保存；关闭最后一个文档和便签均回到无窗口状态。测试未使用正式便签数据。
- 原生保存样例与截图：`artifacts/paste-fix-qa/native-paste.md`、`artifacts/paste-fix-qa/native-paste.png`。运行中的正式窗口存在未保存编辑，更新构建时保留该进程，保存并重启后生效。
- 已更新 1.3.1 的 macOS ARM64 应用、macOS / Windows x64 ZIP；macOS 签名验证通过。Windows 完成交叉发布，原生交互仍需实机验证。

![Markdown 粘贴修复](../artifacts/paste-fix-qa/native-paste.png)

## 2026-10-28：1.3.2 macOS 退出快捷键（由 1.3.3 调整）

- macOS 的 Command+Q 不退出应用，窗口和菜单栏 / 状态栏入口保持可用；菜单中的“退出纸间”仍可点击正常退出，并继续执行原有保存检查。Windows 的 Ctrl+Q 保持原有行为。
- 取消应用菜单和托盘退出项的 Command+Q 绑定，便签库、便签与文档窗口消费该快捷键但不执行退出；编辑器在所见即所得和源码模式均拦截 Meta+Q，不发送退出请求。
- 原生菜单沿用 Avalonia 提供的退出项，取消其 Gesture 并保留其他系统菜单，依据锁定版本的 [Avalonia 12.1.4 菜单实现](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Native/AvaloniaNativeMenuExporter.cs)。
- 76 项 Chromium / WebKit 检查及 49 项 .NET 检查通过。macOS 独立实例实测了便签库、桌面便签、文档编辑 / 源码模式的 Command+Q；无窗口时按键后测试进程 PID 保持不变，点击菜单中的“退出纸间”后正常退出，进程返回码为 0。
- 独立测试目录为 `artifacts/quit-shortcut-qa/data`，未修改正式便签。已更新 1.3.2 macOS ARM64 应用与 macOS / Windows x64 ZIP，保留运行中的正式进程，重启后生效。

## 2026-10-28：1.3.3 关闭窗口并驻留后台（由 1.3.4 调整）

- 按用户澄清，macOS 的 Command+Q 改为关闭所有窗口，保留菜单栏图标、后台进程和全局新建快捷键。菜单新增“关闭所有窗口”并绑定 Command+Q；“退出纸间”继续执行完整退出。
- 未保存文档沿用保存 / 不保存 / 取消提示，取消时不关闭任何窗口。关闭前刷新便签和文档内容；便签放回便签库并保存关闭状态，保存失败保留窗口。完整退出仍保留桌面布局供下次启动恢复。
- 76 项 Chromium / WebKit 检查、49 项 .NET 检查通过；编辑器测试验证 Command+Q 先刷新当前内容，再请求关闭窗口并驻留后台，Windows Ctrl+Q 仍请求退出。
- macOS 独立实例同时打开便签库、便签、设置和文档，验证保存提示的取消与保存分支。保存后诊断显示可见窗口、便签窗口、文档窗口均为 0，`TrayVisible` 为 true，进程仍存活；便签正文和文档修改均已落盘，便签桌面打开状态为 false，从同一后台实例可重新打开便签库。
- 隔离测试数据与原生验证状态保存在 `artifacts/background-close-qa/`，未使用正式便签数据。更新到 1.3.3 后需重新启动正式应用以加载新行为。
- 已更新并验证 1.3.3 macOS ARM64 应用签名及 macOS / Windows x64 ZIP；独立实例在同一进程中多次关闭、重新打开窗口后，点击菜单“退出纸间”完整退出，返回码为 0。


## 2026-10-28：1.3.4 Command+Q 仅关闭便签库（由 1.3.6 调整）

- 按最新要求，macOS 的 Command+Q 只隐藏便签库，桌面便签及 Markdown 文档保持打开；便签桌面状态不变，可继续编辑与自动保存。再次打开便签库复用原窗口。
- 应用菜单、Dock / 托盘菜单改为“关闭便签库”；原生窗口与编辑器的快捷键统一调用关闭便签库操作。移除旧的关闭所有窗口并驻留后台路径。“退出纸间”仍执行完整保存检查与退出；Windows Ctrl+Q 保持退出行为。
- 构建通过，0 警告、0 错误；49 项 .NET 检查与 76 项 Chromium / WebKit 编辑器检查通过，覆盖富文本 / 源码中 Command+Q 先刷新内容再请求关闭便签库。
- macOS 隔离实例验证：便签库内按 Command+Q 后，两张桌面便签仍可见，桌面打开状态为 true，托盘入口可用；再次打开便签库成功。文档有未保存修改时按 Command+Q，仅关闭便签库，文档与两张便签保持打开；便签继续输入后内容正常自动保存。
- 独立数据及状态记录位于 `artifacts/library-close-qa/`，未用正式便签进行编辑测试。macOS ARM64 应用与 macOS / Windows x64 ZIP 更新为 1.3.4；重启应用后加载新行为。Windows 仅完成交叉发布，未做原生交互验证。


## 2026-10-28：1.3.5 Command+W 仅关闭便签库（由 1.3.6 调整）

- macOS 的 Command+W 与 Command+Q 一致，只关闭便签库，桌面便签及 Markdown 文档保持打开。便签库、便签和文档的原生按键处理，以及富文本 / 源码中的编辑器快捷键均已统一；Windows Ctrl+W 继续关闭当前窗口。
- 构建通过，0 警告、0 错误；49 项 .NET 检查、82 项 Chromium / WebKit 编辑器检查通过，包括 Command+Q / Command+W 在富文本及源码中刷新内容再关闭便签库，Windows Ctrl+Q / Ctrl+W 保持原行为。
- macOS 独立实例实测：便签库按 Command+W 后两张便签仍可见；再次打开便签库并聚焦便签，输入内容后按 Command+W，便签继续保留并自动保存。源码文档存在未保存修改时按 Command+W，仅关闭便签库，文档和两张便签仍打开，未触发保存或关闭文档；手动保存后，点击“退出纸间”正常退出。
- 独立测试数据及验证状态位于 `artifacts/library-close-w-qa/`。macOS ARM64 应用及 macOS / Windows x64 ZIP 更新为 1.3.5；当前正式进程保持运行，重启应用后生效。Windows 完成交叉发布，未做原生交互验证。


## 2026-10-28：1.3.6 Command+Q / Command+W 关闭当前窗口

- 按最新要求，macOS 的 Command+Q 与 Command+W 均关闭当前聚焦的窗口：便签库隐藏到后台；桌面便签保存后放回便签库；Markdown 文档走原有保存 / 不保存 / 取消提示。其他窗口保持打开，应用继续驻留后台；“退出纸间”仍完整退出。
- 原生窗口和富文本 / 源码编辑器统一转发为当前窗口关闭操作；原生菜单“关闭当前窗口”绑定 Command+Q，沿用各窗口已有关闭与保存处理。Windows Ctrl+Q 退出、Ctrl+W 关闭当前窗口。
- 构建通过，0 警告、0 错误；49 项 .NET 检查、82 项 Chromium / WebKit 编辑器检查通过。完整输出位于 `artifacts/current-window-check.log`。
- macOS 隔离实例实测：便签库内 Command+Q / Command+W 只收起便签库；两张便签分别输入内容后按 Command+Q / Command+W，正文已保存、对应桌面打开状态为 false，第三张便签保持打开。文档源码中 Command+W、富文本中 Command+Q 均验证了取消继续编辑和保存后关闭，关闭文档后桌面便签仍可见。
- 独立测试数据、便签状态与文档保存样例位于 `artifacts/current-window-qa/`。macOS ARM64 应用与 macOS / Windows x64 ZIP 更新为 1.3.6，macOS 签名和压缩包内程序集核验通过。Windows 仅完成交叉发布，未做原生交互验证。重新启动应用后加载新行为。


## 2026-10-28：1.3.7 关闭快捷键按 macOS 键盘窗口定位

- 针对便签库与桌面便签同时存在时关闭目标可能错位的问题，Command+Q / Command+W 的所有入口统一经过控制器。macOS 直接读取 AppKit 的 keyWindow，并与 Avalonia 公开的 NSWindow 句柄对应；不再依赖触发 KeyDown 的窗口或托管 IsActive 的顺序。无法对应原生窗口时不关闭其他窗口。
- 编辑器的关闭消息立即确定原生焦点目标，其他编辑器命令保留原有调度。保存、关闭未保存文档的提示和 Windows 行为继续沿用原逻辑。仅测试模式记录关闭请求来源、目标以及便签库可见状态。
- 构建 0 警告、0 错误；49 项 .NET 检查、82 项 Chromium / WebKit 检查通过，输出在 `artifacts/focus-close-check.log`。
- macOS 隔离实例始终保持便签库可见，分别在桌面便签标题栏、富文本正文中按 Command+W，在源码中按 Command+Q；记录的目标均为对应桌面便签，正文已保存，便签库保留。文档 Command+W 的取消、保存分支及便签库自身的关闭也通过验证。
- 独立数据与逐次目标记录在 `artifacts/focus-close-fix-qa/`。macOS ARM64 应用和 macOS / Windows x64 ZIP 更新为 1.3.7；macOS 签名及压缩包内容已核验。Windows 仅完成交叉发布，未做原生交互验证。需从菜单“退出纸间”后重新打开应用加载新行为。
