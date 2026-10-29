# 纸间 · PaperNote

用 Avalonia 开发的 macOS / Windows 桌面便签。启动直接恢复桌面上的小便签，随手记录；需要整理和搜索时，再打开便签库。

## 已实现

- 原生 Avalonia 窗口、macOS 窗口按钮、便签列表、全文搜索、纸张颜色、置顶。
- 桌面便签优先：启动恢复上次打开的便签、位置、尺寸、置顶和收起状态，兼容已有便签数据。
- 小窗顶部直接新建、置顶、打开更多选项；底部换色、格式工具与便签库。格式栏默认隐藏。
- 双击标题条收起 / 展开；更多选项内可调字号、不透明度、源码、导出与放回便签库。
- 记住每张便签的字号、格式栏和不透明度；断开外接显示器后自动将窗口移回可见区域。
- 便签库采用侧栏 + 自适应卡片布局，支持搜索、收藏、最近编辑 / 标题排序；点击卡片打开桌面便签，右键管理。
- macOS Dock 右键、菜单栏 / 系统托盘、Windows 任务栏右键任务提供新建便签、便签库和设置；再次启动会把操作交给已有进程。
- 设置页支持登录后自动启动、恢复桌面布局、默认纸张颜色和自定义全局快捷键；快捷键占用时显示原因，保留原快捷键。
- macOS 明确启用 Dock、设置应用级图标，发布包包含完整图标和常规应用配置。
- 独立 Markdown 文档窗口：默认所见即所得，直接编辑正文；支持新建、打开 `.md` / `.markdown` / `.mdown` / `.mkd` / `.txt`、保存原文件与另存为，可同时打开多个文档。
- macOS 访达 / Windows 资源管理器“打开方式”接入 Markdown 文档，支持文件启动、运行中接收文件及重复窗口复用。
- 文档栏与格式栏合并精简，正文居中、限制行宽，调整系统字体、留白与中性色配色。
- 文档未保存标记、关闭 / 打开其他文件 / 退出前的保存提示；原子写入与外部修改检测，保存失败时保留当前内容。
- Markdown 所见即所得：标题、粗体、斜体、删除线、有序/无序列表、可勾选待办、引用、代码、链接、分隔线、表格和图片。
- 输入 `# `、`## `、`- `、`1. `、`[] `、`> ` 等标记即时转换；Markdown 文本粘贴转换。
- 源码与富文本模式切换、格式工具栏、撤销/重做；便签之间隔离撤销历史。
- Markdown / TXT 多文件导入、Markdown 导出、最近删除、恢复与确认后永久删除。
- 400ms 自动保存、原子替换、上一版 `.bak` 备份；损坏数据保留 `.damaged-*` 副本并提示。

## 桌面便签隐藏 / 显示

在“设置与快捷键 → 隐藏 / 显示桌面便签”中点击组合键录制新快捷键，点击“保存快捷键”生效。Windows 默认 `Ctrl + Alt + H`，macOS 默认 `⌘ + ⌥ + H`。支持单独关闭、恢复默认；与新建便签快捷键冲突或被其他应用占用时保留原设置。

按一次暂时隐藏桌面便签，再按一次恢复原窗口，不改变便签内容与打开状态；便签库右上角和系统托盘也可切换。便签库移除颜色筛选，使用系统字体、淡紫侧栏、紫色强调和浅色纸张卡片，支持浅色、深色与跟随系统。

## 开发运行

需要 **.NET SDK 10**。编辑器生成文件已包含在源码中，首次运行无需安装 Node.js。

```sh
dotnet run --project src/PaperNote
```

macOS / Linux 也可以使用 `./scripts/run.sh`，脚本在 PATH 中找不到 dotnet 时会查找 `~/.dotnet/dotnet`。

修改编辑器源码需要 Node.js 22 或更新版本：

```sh
cd editor
npm ci
npm run build
```

生成的 `src/PaperNote/Editor/editor.html` 是单文件离线资源，嵌入 .NET 程序。没有 CDN 依赖；HTTPS 远程图片需要网络，其他书写功能可以离线使用。

## 作为 Markdown 编辑器使用

在便签库左下角点击“Markdown 编辑器”，或从应用菜单、Dock / 托盘菜单打开。桌面便签的“便签选项”中也有“新建 Markdown 文档…”入口。

文档默认进入**所见即所得**：直接在标题、列表、表格和正文里编辑，使用格式工具栏或 Markdown 输入规则设置格式。没有独立预览区。需要查看原始文本时可点“源码”，或按 `⌘/Ctrl Shift M`。

- `⌘/Ctrl O`：打开本地文档。
- `⌘/Ctrl S`：保存到原文件；新文档先选择保存位置。
- `⌘/Ctrl Shift S`：另存为；也可点击右上角“文档选项”中的“另存为…”。
- `⌘/Ctrl N`：在文档窗口内新建另一个文档。

文件文档通过“保存”写入磁盘，便签仍自动保存到便签库。文件被其他程序修改或移走时，保存会提示冲突，可另存为保留当前修改。源码模式保留原始 Markdown 及 CRLF 换行；UTF-8 文件的 BOM 会保留，其他带 BOM 的 Unicode 文本保存为 UTF-8。也可用 `dotnet run --project src/PaperNote -- --markdown` 直接打开文档窗口。

## 系统打开方式

macOS 发布脚本会注册 Markdown 文档类型。右键 `.md` / `.markdown` / `.mdown` / `.mkd` 文件，选择“打开方式 → 纸间”即可直接编辑。应用同时声明 `public.markdown` 和 `net.daringfireball.markdown`，适配系统与已有应用使用的 Markdown 类型。文件打开事件在冷启动和应用运行中均可接收；再次打开同一文件会激活已有窗口。

Windows 首次运行发布包时，在当前用户下注册 Markdown 的“打开方式”候选项，无需管理员权限。也可在资源管理器中手动选择发布包内的 `PaperNote.exe`。

命令行可直接传入文件路径，或使用 `--open`；中文、空格、文件 URI 和多个文档路径均受支持：

```sh
dotnet run --project src/PaperNote -- --open "/path/to/我的文档.md"
```

## 发布

macOS（在 macOS 运行）：

```sh
./scripts/publish-macos.sh osx-arm64
# Intel Mac：./scripts/publish-macos.sh osx-x64
open artifacts/osx-arm64/纸间.app
```

Windows（PowerShell）：

```powershell
./scripts/publish-windows.ps1
# ARM64：./scripts/publish-windows.ps1 -Runtime win-arm64
```

发布包自带 .NET 运行时。macOS 产物是 `.app`；Windows 产物是文件夹和 ZIP，运行其中的 `PaperNote.exe`。Windows 需要 Microsoft Edge WebView2 Runtime（通常在 Windows 11 中已安装）；macOS 使用系统 WKWebView。macOS 发布脚本执行本地 ad-hoc 签名，正式对外分发还需要开发者签名和公证。当前没有安装器或自动更新。

## 验证

```sh
dotnet build src/PaperNote
dotnet run --project tests/PaperNote.Tests
# 原生窗口生命周期检查（需要桌面会话，不操作键盘和鼠标）
dotnet run --project tests/PaperNote.UiTests -c Release
cd editor
npx playwright install chromium webkit
npm test
```

若使用本机 Edge 验证，可跳过浏览器下载：

```sh
cd editor
PAPERNOTE_EDGE=1 npm test -- --project=chromium
```

存储与集成测试含 134 项检查，另有 11 项原生窗口生命周期检查，覆盖保存、中文、删除保护、损坏数据保护、旧数据迁移、桌面会话、多屏位置恢复、快捷键验证、设置保存、文件启动参数与真正的跨进程命令及文件路径转发，以及摘要内存上限、BOM 编码兼容、卡片复用和隐藏后恢复；浏览器测试覆盖 Markdown 渲染、任务勾选、源码往返、表格、输入规则、撤销隔离、只读删除状态、格式工具、图片及任务行布局，以及文档默认所见即所得、源码工具栏、快捷键、换行保留和跨文件撤销隔离。GitHub Actions 配置为分别在 macOS / Windows 构建和验证；本地跨编译不能代替 Windows 真机测试。

性能基准与复现命令见 [性能优化记录](docs/performance.md)。

在便签库左下角点击“设置与快捷键”。macOS 自启使用系统登录项服务（需要 macOS 13+），Windows 使用当前用户的启动项；不会在首次启动时自动开启。建议先把 macOS 应用放入“应用程序”，Windows 应用解压到固定目录，再开启自启，移动应用后应重新开启一次。

## 快捷键

| 操作 | macOS | Windows |
| --- | --- | --- |
| 全局新建便签（可在设置中修改） | ⌥ ⌘ N | Ctrl Alt N |
| 新建便签 | ⌘ N | Ctrl N |
| 搜索便签 | ⌘ F | Ctrl F |
| 立即保存 | ⌘ S | Ctrl S |
| Markdown 源码 | ⌘ Shift M | Ctrl Shift M |
| 展开 / 收起格式工具 | ⌘ Shift F | Ctrl Shift F |
| 打开便签库 | ⌘ Shift O | Ctrl Shift O |
| 打开设置 | ⌘ , | Ctrl , |
| 沉浸阅读（只读） / 退出阅读 | ⌘ Shift R / Esc | Ctrl Shift R / Esc |
| 收起 / 展开便签 | ⌘ Shift L | Ctrl Shift L |
| 关闭当前窗口（便签库 / 便签 / 文档） | ⌘ Q / ⌘ W | Ctrl W |
| 退出应用 | 菜单“退出纸间” | Ctrl Q |
| 粗体 / 斜体 | ⌘ B / I | Ctrl B / I |
| 撤销 / 重做 | ⌘ Z / ⌘ Shift Z | Ctrl Z / Ctrl Shift Z |

## 存储与边界

数据是本地 JSON，正文保存为 Markdown，不上传到服务端。

- macOS：`~/Library/Application Support/PaperNote/notes.json`
- Windows：`%LOCALAPPDATA%\PaperNote\notes.json`

便签库的卡片提供直接删除和更多操作；“管理便签”可多选、全选并批量移到最近删除，回收列表支持批量恢复和确认后永久删除。设置中的“外观”支持跟随系统、白天和黑夜模式，已打开的窗口同步更新。便签和 Markdown 文档可进入只读沉浸阅读，隐藏编辑工具，通过“退出阅读”或 Esc 返回。空白桌面便签关闭后直接丢弃；未命名文档只有空格或换行时不弹保存提示，已有文件清空仍会提示保存。

通用偏好保存在同目录的 `settings.json`。自启状态从系统读取；快捷键支持 Control、Option / Alt、Command / Win、Shift 与字母、数字或空格的组合。

源码模式保留原始 Markdown；在富文本中修改后会以编辑器支持的 Markdown 语法重新序列化，空白和列表标记可能规范化。原始 HTML、脚本、Mermaid、数学公式和脚注尚不支持富文本编辑；此类文档请使用源码模式，避免不支持的节点在富文本修改后被移除。表格可以粘贴 Markdown 或通过源码模式创建。远程图片只允许 HTTPS，暂不提供本地附件管理和云同步。除全局新建组合键外，快捷键在应用窗口获得焦点时生效。macOS 按 `⌘ Q` 或 `⌘ W` 关闭当前聚焦的窗口：便签库收起到后台，桌面便签保存后放回便签库，未保存文档先提示保存；其他窗口保持打开。可从菜单重新打开便签库。关闭桌面便签会将它放回便签库；使用“退出纸间”保留当前桌面布局并退出。

## 代码结构

```text
src/PaperNote/          Avalonia 桌面应用（原生界面 + C#）
  Models/              便签模型与纸张配色
  Services/            本地保存、桌面生命周期、会话与多屏位置恢复
  Views/               主窗口、浮窗、WebView 桥接
  Editor/editor.html   嵌入式编辑器生成文件
editor/src/            Tiptap / ProseMirror 编辑器及样式
editor/tests/          Chromium / WebKit 交互测试
tests/PaperNote.Tests/ 存储行为测试
scripts/               运行与发布脚本
packaging/             macOS 信息文件、应用图标
```

技术依赖：[Avalonia](https://github.com/AvaloniaUI/Avalonia)、[NativeWebView](https://github.com/AvaloniaUI/Avalonia.Controls.WebView)、[Tiptap](https://github.com/ueberdosis/tiptap)，均采用 MIT 许可。Markdown 集成使用 Tiptap 官方 `@tiptap/markdown`（当前标记为 beta），版本已锁定在 package-lock.json。

1.3 版本文件打开与排版验证见 [Markdown 编辑器检查](docs/markdown-editor.md)。1.2 版本的界面与系统集成检查见 [新版检查记录](docs/redesign-v12.md)，上一轮记录见 [桌面便签体验检查](docs/desktop-experience.md)。原生 UI 验证使用独立测试数据目录；可通过 `PAPERNOTE_DATA_DIR` 指定测试数据位置。
