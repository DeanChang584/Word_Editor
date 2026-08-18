# 实施进度

> 对照 implementation-plan.md 记录每个步骤的完成状态
> 最后更新：2026-07-15

## 阶段 0：项目基础设施

- [x] Step 0.1 — 创建目标目录结构（2026-07-12）
- [x] Step 0.2 — 创建共享数据模型文件（2026-07-12）
- [x] Step 0.3 — 创建后端日志模块（2026-07-12）
- [x] Step 0.4 — 初始化 Git 并更新 .gitignore（2026-07-12）

## 阶段 1：后端核心

- [x] Step 1.1 — 创建后端应用入口（2026-07-12）
- [x] Step 1.2 — 创建统一响应格式工具（2026-07-12）
- [x] Step 1.3 — 创建后端配置管理模块（2026-07-12）
- [x] Step 1.4 — 创建共享 DTO 定义（2026-07-12）
- [x] Step 1.5 — 创建错误码定义（2026-07-12）
- [x] Step 1.6 — 创建文件管理 API（2026-07-12）
- [x] Step 1.7 — 创建排版配置 API（2026-07-12）
- [x] Step 1.8 — 创建模板管理 API（2026-07-12）
- [x] Step 1.9 — 创建排版任务 API（2026-07-12）

## 阶段 2：排版引擎重构

- [x] Step 2.1 + 2.2 — 规则引擎架构迁移 & 数据结构对齐（2026-07-12）
- [x] Step 2.3 — 页面设置规则（A4/A3/B5/custom、portrait/landscape、页码、文档网格）（2026-07-12）
- [x] Step 2.4 — 段落与标题排版规则（行距三模式、首行/悬挂缩进、多单位支持、段间距）（2026-07-12）
- [x] table.py 完整实现（对齐/宽度/行高/边框/表头/单元格对齐/缩进/边距/跨页/重复表头）（2026-07-12）
- [x] image.py 完整实现（尺寸/对齐/压缩/缩放/文字环绕）（2026-07-12）
- [x] header_footer.py 完整实现（apply_header_footer 遍历所有 section，偶数页页眉页脚）（2026-07-12）
- [x] page.py apply_header_footer_font（engine.py 实际调用的实现，含 fldSimple 页码字段检测）（2026-07-12）

## 阶段 3：Service 层

- [x] Step 3.1 — 文件管理 Service（2026-07-12）
- [x] Step 3.2 — 排版任务 Service（2026-07-12）
- [x] Step 3.3 — 模板管理 Service（2026-07-12）

## 阶段 4：历史记录 + 预览

- [x] Step 4.1 — 历史记录模块（2026-07-12）
- [x] Step 4.2 — 参数摘要预览 Level 1（2026-07-12）
- [x] PDF 预览 Level 2（2026-07-15）
  - POST /api/preview/pdf + GET /pdf/{id} + POST /pdf/{id}/cancel
  - WebView2 + PDF.js 渲染
  - WPS/Word COM 策略模式转换（DocumentPreviewService / IDocumentPdfConverter / WpsPdfConverter / WordPdfConverter）
  - 单例 PreviewWindow + WPS COM 预热 + WarmUpAsync

## 阶段 5：前端基础

- [x] Step 5.1 — 初始化 WinUI3 项目（2026-07-12）
- [x] Step 5.2 — 创建 ApiService（18 个 API 方法，返回强类型 DTO）（2026-07-12）
- [x] Step 5.3 — 创建前端 DTO 类（22 个文件 7 个子目录）（2026-07-12）

## 阶段 6：主窗口框架

- [x] Step 6.1 — 三栏主窗口布局（2026-07-12）
- [x] Step 6.2 — 标题栏 TitleBar（2026-07-12，icon-only 无文字）
- [x] Step 6.3 — 左侧导航栏 NavBar + ItemSelector（2026-07-12）
  - 8 项导航（含关于软件）、Ctrl+1~8 快捷键、compact 模式（<800px 折叠仅图标）
- [x] Step 6.4 — 状态栏 StatusBar（2026-07-12）

## 阶段 7：中间配置区域

- [x] Step 7.1 — 配置卡片框架 ConfigCard（2026-07-12，圆角 8px 无底部按钮）
- [x] Step 7.2 — 页面设置 PageSettingsView（纸张/方向/边距/文档网格）（2026-07-12）
- [x] Step 7.3 — 正文样式 BodyStyleView（2026-07-12，含 strikethrough/hanging）
- [x] Step 7.4 — 标题样式 HeadingStyleView（2026-07-12，级别切换+_isLoadingFields 守卫）
- [x] Step 7.5 — 页眉页脚 HeaderFooterView（2026-07-12，含 strikethrough+underline，mm/cm 单位）
- [x] Step 7.6 — 图片/表格设置 PictureSettingsView + TableSettingsView
  - PictureSettingsView：尺寸模式/宽高+单位/保持比例/不放大/对齐/文字环绕/压缩质量/自动压缩（全部已实现）
  - TableSettingsView：23 个属性，~20 组控件（对齐/宽度%/自适应/行高/表头/样式/缩进/单元格对齐 H+V/背景色/边框样式+颜色+宽度/边距/跨页/重复表头）（全部已实现）
- [x] Step 7.7 — 高级设置 AdvancedSettingsView（语言+主题 ComboBox）+ AboutView（图标/版本/描述/GitHub/邮箱/版权）（2026-07-12）

## 阶段 8：右侧工作区域

- [x] Step 8.1 — 文件管理卡片（2026-07-12，CheckBox 列表+拖拽+全选/反选+最近打开+搜索）
- [x] Step 8.2 — 排版控制卡片 FormatControlView（模板选择+输出目录+预览+开始/取消+进度+结果+重试+打开输出文件夹）（2026-07-12）
- [x] Step 8.3 — 结果与历史卡片 ResultHistoryView（结果摘要+失败文件展开+历史记录折叠/懒加载/重新执行/清空）（2026-07-12）

## 阶段 9：MVVM 绑定

- [x] Step 9.1 — 主窗口 MainViewModel（统一 DataContext，聚合 12 子 VM，WireSubVmSync）（2026-07-12）
- [x] Step 9.2 — 各页面独立 ViewModel（12 个 VM：Page/Body/Heading/HF/Picture/Table/Files/Format/TemplateMgt/History/Settings + Profile legacy）（2026-07-12）
- [x] Step 9.3 — SharedProfile 双向 DTO 同步（SetSharedProfile → LoadFromSharedProfile ↔ WriteToSharedProfile）（2026-07-12）
- [x] Step 9.4 — 配置修改标记 IsDirty（2026-07-12）
- [x] Step 9.5 — 全局配置保存区 SaveBar
  - 中栏底部固定 SaveBar（DirtyStatusText + 恢复默认 + 保存配置）
  - 页面切换直接导航（无弹窗）
  - 开始排版自动保存配置
  - 仅关闭软件/切换模板/加载历史弹出保存确认

## 阶段 10：集成测试

- [x] Step 10.1 — 后端全接口集成测试（61 passed, 0 failed, 3 skipped）（2026-07-12）
- [ ] Step 10.2 — 前后端联调（Automation Complete ✅, Manual Acceptance Pending ⏳）
- [ ] Step 10.3 — 性能验证（Pending ⏳）

## 阶段 11：打包发布

- [ ] Step 11.1 — PyInstaller 打包
- [ ] Step 11.2 — dotnet publish
- [ ] Step 11.3 — 一键启动脚本

## 额外完成项（超出原计划范围）

- [x] NumericTextBox 自定义控件（替代 WinUI NumberBox：上下步进按钮、鼠标滚轮、键盘、Min/Max/Step/DecimalPlaces）
- [x] LightTheme.xaml 完整自定义主题资源（7 个 SolidColorBrush + AccentButtonStyle #005FBA）
- [x] App.xaml InvertBoolToVisibilityConverter 全局注册
- [x] TemplateManagementView + TemplateManagementViewModel（模板列表/删除/导入/导出）
- [x] TrayIconService 系统托盘（最小化到托盘、点击恢复）
- [x] 响应式左列（4 档：48/160/240/360px，<800px compact 仅图标）
- [x] 右列 Preview+Start 二级按钮（RightColumnGrid 底部）
- [x] 导航到"高级设置"时右列切换为模板管理面板
- [x] 导航到"关于软件"时右列隐藏、中栏 ColumnSpan=2 跨满
- [x] 首行/悬挂缩进多单位支持（字符/pt/mm/cm/磅）
- [x] 段间距行单位支持（beforeLines/afterLines XML 写入）
- [x] 文档网格设置（无网格/只指定行/指定行和字符）
- [x] 页眉页脚 fldSimple 页码字段检测
- [x] WPS COM 单例预热 + 3 ProgID 检测策略
- [x] PDF.js 驻留模式（首次加载 viewer.html，后续切换 JS open()）

## 待实现项

- [ ] 深色主题 DarkTheme.xaml
- [ ] SettingsViewModel 绑定到 AdvancedSettingsView（当前硬编码 ComboBox 项）
- [ ] 列间拖拽分隔条 GridSplitter（现为静态 1px 分隔线）
- [ ] 自动更新机制
- [ ] 多语言国际化
- [ ] 打包发布（PyInstaller + dotnet publish + Inno Setup）

---

> 最后更新：2026-07-15

---

## 阶段 12：v2.1 稳定性与体验优化（2026-08-18）

> 版本号 2.0 → 2.1。本轮修复三个线上问题 + 启动优化 + 界面层次对齐。

### 修复

- [x] **模板切换崩溃**：修复「调整字号 → 保存模板 → 选择刚保存的模板 → 软件退出」。
  - 后端 `get_all_templates()` 补齐 profile 字段 → 前端 `ApplySelectedTemplate` 可真正应用模板。
  - `FormatControlView.PopulateTemplateBox` 加重入锁 `_isPopulating`，消除 SelectionChanged 内改 Items 的 WinUI 崩溃。
  - `ApplySelectedTemplate` / `SwitchTemplateAsync` 加 try-catch，异常显示到状态栏而非退出进程。
- [x] **添加文档没反应（静默失败）**：`FilesViewModel.StatusMessage` 从未被 UI 显示。
  - `MainViewModel.WireSubVmSync` 增加 `FilesVm.StatusMessage → StatusText` 转发。
  - 前端预校验文件/文件夹路径，不存在时立即提示具体名称。
- [x] **关闭软件延迟**：`Closed` 处理器原为 `async void` 且 `await ShutdownBackendAsync()`（2s 超时），
  WinUI 3 窗口关闭后 await 续体可能被丢弃导致进程残留。改为同步：后端退出最多等 300ms、WPS COM 后台释放、立即 `Environment.Exit(0)`。
- [x] **界面层次**：ConfigScroll 内边距 8/8、ConfigCard 左内边距 0→16（修复文字溢出白卡片）、
  窗口/卡片/导航配色对齐 v2.0.1 参考设计（窗口纯白、导航 #FAFAFA、选中 #E8F0FE、输入框边框加深 #8A8A8A）。

### 优化

- [x] **启动速度**：Launcher 不再串行等待后端健康检查（最多 20s 白屏），改为并行启动前端 + 后台轮询；
  MainWindow `Page_Loaded` 先渲染壳层、数据后台加载。

### 待实现项（更新后）

- [ ] 列间拖拽分隔条 GridSplitter（现为静态 1px 分隔线）
- [ ] 自动更新机制
- [ ] 多语言国际化
- [ ] 打包发布（PyInstaller + dotnet publish + Inno Setup）— 已更新 setup.iss 版本号 2.1

