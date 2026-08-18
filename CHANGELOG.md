# Changelog

## v2.1 (2026-08-18)

> 稳定性与体验优化版本。修复模板切换崩溃、添加文档静默失败、关闭延迟；优化启动速度与界面层次。

### 修复

- **模板切换崩溃**：修复「调整字号 → 保存模板 → 选择刚保存的模板 → 软件退出」。
  - 后端模板列表补齐 profile 字段，选择模板可真正生效。
  - 前端 ComboBox 下拉回填加重入锁，消除 SelectionChanged 内修改 Items 导致的 WinUI 崩溃。
  - 模板应用链路 (ApplySelectedTemplate / SwitchTemplateAsync) 加 try-catch，失败显示到状态栏而非退出进程。
- **添加文档没反应**：`FilesViewModel.StatusMessage` 此前只赋值从不显示，任何失败都静默。现同步到状态栏，并增加前端路径预校验（文件/文件夹不存在时立即提示具体名称）。
- **关闭软件延迟**：关闭时不再 `await` 网络请求后才退出；后端退出最多等 100ms，WPS COM 单例后台释放，点击关闭后立即退出。`/api/shutdown` 调用加 `ConfigureAwait(false)`，消除 UI 线程 sync-over-async 死锁（实测窗口销毁 326ms → 22ms）。
- **界面层次**：内容区内边距对齐（左 8 / 右 8），ConfigCard 左内边距修复（文字不再溢出白色卡片），窗口/卡片/导航栏配色对齐 v2.0.1 参考设计，输入框边框加深保证框体清晰可辨。

### 优化

- **启动速度**：Launcher 不再串行等待后端健康检查（最多 20s 白屏），改为并行启动前端 + 后台轮询；主窗口先渲染壳层、数据后台加载。

## v2.0 (2026-07-16)

> 从 PyQt5 架构全面迁移到 WinUI 3 + FastAPI 架构，完全重写前后端。本次更新改动量极大，涉及超过 120 个文件、近 20000 行代码。

### 架构变更

- **前端架构**：PyQt5 → **WinUI 3 (.NET 9 / C# 13)**，原生 Windows 界面，三栏自适应布局，MVVM 模式（CommunityToolkit.Mvvm 源代码生成器）
- **后端架构**：PyQt5 内置服务 → **FastAPI (Python 3.14)**，独立进程 HTTP REST API
- **通信方式**：进程内调用 → **HTTP REST + JSON (camelCase)**，前后端完全解耦
- **打包方式**：PyInstaller 单体 → **Inno Setup 安装包**（后端 PyInstaller + 前端 dotnet publish 自包含）

### 新增功能

#### 排版引擎（完整重写，8 个模块）

- **段落规则**：行距三模式（多倍/fixed/at_least）、首行/悬挂缩进支持（字符/pt/mm/cm 单位）、段间距支持行/磅单位（`beforeLines`/`afterLines` XML 写入）
- **标题规则**：Heading 1-6 级独立配置，修改 Word 样式实现
- **页面设置**：标准纸张（A4/A3/B5/Letter/Legal）+ 自定义纸张、portrait/landscape 自动宽高校正、边距 clamp 保护、页码 footer 注入（PAGE field code）、文档网格设置
- **页眉页脚**：遍历所有 section（含偶数页），字体/字号/字形/对齐设置，fldSimple 页码字段检测保护
- **图片规则**（600+ 行完整实现）：尺寸模式（固定宽度/高度/尺寸/原始）、文字环绕（inline/square/tight/through/topBottom/behindText/inFrontOfText）、对齐（left/center/right）、压缩（质量+最大像素+自动压缩）
- **表格规则**（600+ 行完整实现）：对齐/宽度（auto/fixed/percent）+ EMU 转换、行高（auto/fixed/at_least）、边框（none/all/horizontal/grid）、表头字体/加粗/居中/背景色、单元格对齐（H+V）、边距、缩进、重复表头、跨页断行
- **字体规则**：中英文分离设置（apply_run_font / apply_style_font）

#### 模板系统

- 模板 CRUD（创建/读取/更新/删除/导入/导出）
- 默认模板预置（首次启动自动创建）
- 版本号校验（导入时）
- 默认模板删除保护
- 持久化到 `%LOCALAPPDATA%\WordFormatter\templates\`

#### 历史记录

- 排版任务完成后自动保存历史
- 最近 50 条记录管理（自动淘汰最旧）
- 历史详情含完整 profile/files/results
- 一键复用历史配置和文件列表

#### PDF 预览

- Level 1 文本预览：参数摘要（覆盖全部 7 个配置段）
- Level 2 PDF 预览（完整实现）：
  - WPS/Word COM 自动检测（WPS 优先，3 个 ProgID 探测）
  - COM 策略模式转换（`IDocumentPdfConverter` → `WpsPdfConverter` / `WordPdfConverter`）
  - WebView2 + PDF.js 4.x 渲染
  - 底部工具栏：100%/适应宽度/适应页面/翻页/页码
  - WPS 单例预热 + 前端 WarmUpAsync

#### 前端控件

- **NumericTextBox**：自定义数字输入控件（替代 WinUI NumberBox），支持上下步进按钮、鼠标滚轮、键盘（Up/Down/PageUp/Down）、Min/Max/Step/DecimalPlaces
- **TitleBar**：自定义标题栏，左侧 Logo 32x32，系统按钮预留区
- **NavBar**：8 项导航（支持 Ctrl+1~8 快捷键），响应式折叠（<800px 仅图标）
- **ItemSelector**：导航项控件，VisualStateManager 驱动 Normal/Hover/Selected 状态
- **StatusBar**：状态指示点（颜色编码）、状态文本、模板名、文件数、版本
- **ConfigCard**：可复用配置卡片（圆角 8px，主题色跟随系统）

### UI 布局

- **三栏布局**：左导航（160px，4 档响应式）/ 中配置（3.6\*）/ 右文件与排版（2.4\*）
- **全局 SaveBar**：中栏底部固定保存区（DirtyStatusText + 恢复默认 + 保存配置），页面切换直接导航无弹窗
- **响应式左栏**：4 档宽度（48/160/240/360px），<800px 自动折叠为仅图标
- **右栏面板**：文件管理 + 排版控制 + 结果历史三卡片，导航到高级设置切换为模板管理
- **关于页面**：隐藏右栏、中栏跨满显示
- **浅色主题**：Office/WPS 专业风格（accent #005FBA），自定义 7 个 ThemeResource 画笔

### MVVM 架构

- **MainViewModel**：统一 DataContext，聚合 12 个子 ViewModel
- **SharedProfile**：全局排版配置唯一真相源，6 个 section VM 双向同步（`SetSharedProfile` / `LoadFromSharedProfile` / `WriteToSharedProfile`）
- **全局 IsDirty**：任何 section VM 属性变更自动标记，SaveBar 实时反映
- `_isLoading` 守卫：防止 `LoadFromSharedProfile` 回写时触发循环
- 6 个 section VM 各自独立（PageSettings / BodyStyle / HeadingStyle / HeaderFooter / Picture / Table）

### 后端 API（27 个端点）

| 端点 | 数量 | 功能 |
|------|------|------|
| `/api/health` | 1 | 健康检查 |
| `/api/files` | 8 | 文件管理 CRUD + 搜索/最近/固定 |
| `/api/profile` | 3 | 配置获取/更新/重置（深度合并） |
| `/api/templates` | 7 | 模板 CRUD + 导入/导出/默认 |
| `/api/format` | 4 | 排版任务启动/状态/取消/结果 |
| `/api/preview` | 4 | 文本预览 + PDF 预览（启动/轮询/取消） |
| `/api/history` | 3 | 历史记录列表/详情/清空 |

### 数据目录迁移

所有运行时数据从 `{app}\config\` 迁移到 `%LOCALAPPDATA%\WordFormatter\`：
- 模板、历史、设置、日志均使用本地应用数据目录
- 解决 Program Files 写权限问题
- 卸载时自动清理

### 打包与发布

- **后端**：PyInstaller `--onefile` 打包为 `backend.exe`（~22MB）
- **前端**：dotnet publish 自包含发布（含 .NET 9 运行时 + WinUI 3 + WebView2）
- **启动器**：C# 程序 `WordFormatter.exe`（~14MB，启动后端 → 等待就绪 → 启动前端 → 退出清理）
- **安装包**：Inno Setup 6 封装为 `Word Formatter v2.0.exe`（~55MB，LZMA2 压缩）
- **安装功能**：桌面快捷方式、开始菜单、卸载入口、WebView2 检测

### 性能与测试

- 后端全接口集成测试：**66 passed / 3 skipped / 0 failed**
- 覆盖 27 个 API 端点 + 段落缩进专项测试 + 端到端流程测试
- 前端 Build：0 errors
- 排版引擎 7 步编排，支持 1000+ 文件连续排版

### Bug 修复

- 安装后模板加载失败：`JsonSerializerIsReflectionEnabledByDefault=true` + `TrimmerRoots.xml`
- WinRT COM 崩溃：trimmer roots 保留 `WinRT.Runtime` + `Microsoft.Windows.SDK.NET`
- 模板状态空列表：`_initialised=True` 移到 seed 成功后 + 全路径 `WaitForBackendAsync()`
- 后端进程死锁：取消 stdout/stderr 重定向
- 图片对齐/环绕不生效：inline 图片通过段落 `w:jc` 控制
- 表格宽度百分比不生效：`GetVm()` 返回独立 VM 实例问题
- 表头首行缩进泄漏：Normal 样式不加 indent，表格单元格显式清零
- 页脚内容被删除：fldSimple 页码字段检测保护
- 标题默认值修正：Level 2 小二 18pt / Level 3 三号 16pt
- 图片设置 WidthUnit/HeightUnit 缺失变更处理器
- 配置界面默认值不显示：ProfileRefreshed 事件时序修复 + TableSettingsView.RefreshUI()

### 移除了（旧 PyQt5 架构）

- 所有 PyQt5 文件（`gui/`、`pages/`、`controls/`）
- PyQt5 依赖（QtWidgets、QtCore 等）
- legacy routes（`backend/routes/profile.py`、`routes/files.py`、`routes/format_tasks.py` — 未注册但保留）
- mammoth HTML 预览（替换为 PDF.js + WebView2）
- 旧 `engine.py` / `models.py` / `worker.py`（根目录 → `backend/formatter/`）

---

## v1.3 (2026-03)

PyQt5 最后版本。Add pre-comit and fix license email.

## v1.2.2

Engine and UI updates, PyQt5 documentation.

## v1.2.1

UI refinements and model updates.

## v1.2.0

First PyQt5 release with Word formatting engine.

## v1.0.0

Initial release.
