# 文件功能清单

> 记录项目中每个文件的作用，保持与项目实际结构同步

---

## memory-bank/（设计文档）

| 文件 | 作用 |
|---|---|
| `design-document.md` | 产品设计规范：布局、交互、视觉、功能模块定义 |
| `architecture.md` | 软件架构文档：分层设计、模块划分、目录结构、通信数据流 |
| `tech-stack.md` | 技术栈文档：技术选型、命名规范、性能目标、开发流程 |
| `API.md` | API 接口文档：全部端点的请求/响应规范、错误码定义 |
| `data model.md` | 数据模型文档：所有数据对象的字段定义和 JSON schema |
| `implementation-plan.md` | AI 开发者实施计划：36 步分阶段指令 |
| `progress.md` | 实施进度记录：每步完成状态 |
| `filesfunction.md` | 本文件 — 文件功能清单 |

---

## 项目根目录

| 文件 | 作用 |
|---|---|
| `requirements.txt` | Python 依赖声明 |
| `run.bat` | 一键启动脚本（后端 + 前端） |
| `README.md` | 项目介绍和快速开始指南 |
| `.gitignore` | Git 忽略规则 |

---

## backend/（FastAPI 后端）

| 文件 | 作用 |
|---|---|
| `server.py` | 入口：FastAPI 应用初始化、CORS、注册 7 个路由器（health + files + profile + templates + format + history + preview）；HOST=127.0.0.1, PORT=8765 |
| `routes/profile.py` | 【legacy，未注册】旧 /api/profile |
| `routes/files.py` | 【legacy，未注册】旧 /api/files |
| `routes/format_tasks.py` | 【legacy，未注册】旧 /api/format |

### backend/api/（新 API 层）

| 文件 | 作用 |
|---|---|
| `__init__.py` | 包标识 |
| `health.py` | GET /health 健康检查，返回 {status:"ok", version} |
| `files.py` | /api/files：8 个文件管理端点（GET 列表、POST add/add-folder/remove/search/pin、GET recent、DELETE 清空） |
| `profile.py` | /api/profile：3 个端点（GET 获取、PUT 部分更新深度合并、POST /reset 恢复默认） |
| `templates.py` | /api/templates：7 个端点（CRUD + import/export + set-default）；持久化 config/templates/ JSON |
| `format.py` | /api/format：4 个端点（POST start→HTTP 202、GET status/{id}、POST cancel、GET result/{id}）；后台 daemon thread 执行 |
| `history.py` | /api/history：3 个端点（GET 列表最近 20 条、GET {id} 详情含 profile/files/results、DELETE 清空） |
| `preview.py` | /api/preview：4 个端点（POST Level 1 文本摘要、POST /pdf Level 2 PDF 预览、GET /pdf/{id} 轮询、POST /pdf/{id}/cancel 取消） |

### backend/formatter/（排版引擎）

| 文件 | 作用 |
|---|---|
| `__init__.py` | 包标识 |
| `data_model.py` | 内部配置 dataclass：PageConfig/BodyConfig/HeadingStyleConfig/DmTableConfig/FormatProfile；FONT_SIZE_MAP/PAPER_SIZES |
| `font.py` | 字体工具：apply_run_font（段落级）、apply_style_font（样式级），XML 中英文分离 |
| `page.py` | 页面规则：apply_page_setup（纸张/方向/边距/页码/文档网格）、apply_header_footer_font（页眉页脚字体/样式/零缩进/支持 fldSimple 页码）、apply_document_grid（文档网格）、_resolve_paper_size（含自定义纸张 fallback） |
| `paragraph.py` | 段落规则：apply_paragraph_format（BodyConfig）、apply_heading_paragraph_format（HeadingStyleConfig）；行距三模式（multiple/fixed/at_least）；首行/悬挂缩进（字符/mm/cm/pt/磅）；段间距支持行/磅单位（_space_to_twips / _write_spacing_xml / beforeLines/afterLines）；_clear_paragraph_indent |
| `heading.py` | 标题规则：apply_heading_style（复用 font + paragraph），HEADING_NAMES 常量 |
| `engine.py` | 排版主控：format_docx / process_file 直接接受 ProfileConfig（shared DTO）；_to_format_profile DTO→dataclass 转换；7 步编排（页面→正文→标题→段落→页眉页脚→表格→图片→压缩→保存）；_is_in_table_cell 表格段落跳过；_is_image_only_paragraph；_zero_paragraph_indent |
| `image.py` | 图片规则：apply_image_format（尺寸/对齐/压缩/缩放/文字环绕），完全实现 |
| `table.py` | 表格规则（600+ 行完整实现）：apply_table_format（对齐/宽度 auto-fixed-percent/自适应/行高/跨页/表头/边框 all-none-horizontal-grid/边距/缩进/单元格对齐 H+V/每页重复表头/背景颜色）；_emu_from_cm / _emu_from_pt / _twips_from_indent |
| `header_footer.py` | 页眉页脚规则（完整实现）：apply_header_footer（遍历所有 section，处理 header/footer/even_page_header/even_page_footer，仅非 linked 时处理） |

### backend/config/（配置管理）

| 文件 | 作用 |
|---|---|
| `__init__.py` | 包标识 |
| `defaults.py` | 默认设置值：DEFAULT_SETTINGS 含 theme/language/defaultOutput/defaultTemplate/recentCount/autoCheckUpdate |
| `manager.py` | 设置管理器：get_setting/get_all_settings/update_settings/reset_settings；线程安全 Lock；持久化 config/settings.json |

### backend/services/（业务服务层）

| 文件 | 作用 |
|---|---|
| `__init__.py` | 包标识 |
| `file_service.py` | FileService：文件列表维护、添加/移除/清空/搜索、文件夹扫描、最近打开记录、固定目录；模块单例 |
| `format_service.py` | FormatService：任务创建/状态管理/进度更新/结果汇总；threading.Event 取消机制；_resolve_profile 模板→ProfileConfig 解析；任务完成自动写入 History |
| `template_service.py` | TemplateService：模板增删改查、JSON 文件读写（config/templates/）、版本校验导入、防覆盖导出、默认模板保护；_seed_presets 预置"默认模板"（id=default）|

### backend/history/（历史记录）

| 文件 | 作用 |
|---|---|
| `__init__.py` | 包标识 |
| `manager.py` | HistoryManager：get_all（最近 20 条摘要）、get_detail（完整详情含 profile/files/results）、save_record（由 FormatService 调用）、clear；持久化 config/history/*.json；线程安全 Lock |

### backend/preview/（预览服务）

| 文件 | 作用 |
|---|---|
| `__init__.py` | 包标识 |
| `generator.py` | generate_preview(profile) → 多行中文摘要文本（全部 7 个配置段：页面/正文/标题1-6/页眉页脚/图片/表格）；字号转中文名 |

### backend/utils/（工具函数）

| 文件 | 作用 |
|---|---|
| `__init__.py` | 包标识 |
| `logger.py` | 日志模块：按用途分文件 app/backend/format/error.log；TimedRotatingFileHandler 午夜滚动；30/90 天两级保留 |
| `response.py` | 统一响应：success_response/error_response；ErrorCode 类（0+1000-1010 共 12 个错误码）|

---

## frontend/（WinUI3 前端）

### 入口与配置

| 文件 | 作用 |
|---|---|
| `App.xaml` | 应用入口 XAML：RequestedTheme=Light；XamlControlsResources 合并；LightTheme.xaml 为 Default+Light ThemeDictionary；自定义 AccentButtonStyle（#005FBA）；InvertBoolToVisibilityConverter 注册 |
| `App.xaml.cs` | 应用入口：ApiService 初始化、MainWindow 创建、ExtendsContentIntoTitleBar、TrayIcon 系统托盘、backend 健康轮询、WPS COM + WebView2 预热 |
| `MainWindow.xaml` | 主窗口：3 行（TitleBar 32px/三栏内容/StatusBar 32px）；三栏（左 NavBar 160px/中 ConfigScroll+SaveBar 3.6\*/右文件+排版 2.4\*）；8 个中间列视图（PageSettings/BodyStyle/HeadingStyle/HeaderFooter/Picture/Table/AdvancedSettings/About）；右列 FileManagementPanel+AdvancedFunctionsPanel 切换；SaveBar（DirtyStatusText+恢复默认+保存配置） |
| `MainWindow.xaml.cs` | 主窗口逻辑（~856 行）：MainViewModel 统一 DataContext、NavigateToSection（无弹窗直接导航）、ShowUnsavedDialogAsync（仅关闭/模板/历史时弹出）、RightPreviewBtn_Click 打开 PreviewWindow 单例、文件管理操作（Add/Remove/DragDrop/RecentFlyout）、响应式左列（4 档：48/160/240/360px）、模板保存/历史复用 |

### frontend/Models/（DTO 定义，按模块拆分）

| 文件 | 作用 |
|---|---|
| `ApiResponse.cs` | `ApiResponse<T>` 泛型包装：{success, code, message, data} |
| **Common/** | |
| `Common/FileItemDto.cs` | FileItemDto：id/name/path/size/modifiedTime/status + SizeText 计算属性 |
| **Files/** | |
| `Files/FileListDto.cs` | FileListDto（files: List<FileItemDto>）/ AddCountDto / RecentListDto + RecentRecordDto |
| **Profile/** | |
| `Profile/ProfileConfigDto.cs` | ProfileConfigDto：page/headerFooter/body/heading(Dict)/picture/table + ProfileResponseDto 解包；_default_headings() 工厂函数（6 级默认值） |
| `Profile/DocumentGridConfigDto.cs` | DocumentGridConfigDto：mode/linesPerPage/charsPerLine/adjustRightIndent/alignToGrid |
| `Profile/PageConfigDto.cs` | PageConfigDto：paperSize/orientation/margins/pageNumber/customWidth/customHeight/documentGrid |
| `Profile/HeaderFooterConfigDto.cs` | HeaderFooterConfigDto：字体/字号/样式/对齐/距离+单位 |
| `Profile/BodyConfigDto.cs` | BodyConfigDto：字体/字号/样式(含 strikethrough)/对齐/行距(模式+值+单位)/缩进(type+value+unit, 含 hanging)/段间距+单位 |
| `Profile/HeadingStyleConfigDto.cs` | HeadingStyleConfigDto：level/字体/字号/样式/颜色/对齐/行距/缩进/间距+单位 |
| `Profile/PictureConfigDto.cs` | PictureConfigDto：sizeMode/width+unit/height+unit/keepAspectRatio/noEnlarge/alignment/wrappingStyle/compressionQuality/maxPixels/autoCompress |
| `Profile/TableConfigDto.cs` | TableConfigDto：tableAlignment/widthMode+value+unit/autoFitColumns/rowHeightMode+height+unit/headerFont*/headerSize/headerBold/headerTextCenter/headerBgColor/fontStyle/indent/cellAlignH+V/borderStyle+borderColor+borderWidth/cellMargin+unit/autoSplit/repeatHeader |
| **其他模块** | |
| `Templates/TemplateDto.cs` | TemplateDto + TemplateListDto |
| `Format/` | FormatStartRequestDto / FormatStartResultDto / TaskStatusDto / FileResultDto / TaskResultDto / ExportResultDto / TaskDto |
| `History/HistoryRecordDto.cs` | HistoryRecordDto（含 files/results/profile 字段）+ HistoryFileItemDto + HistoryListDto |
| `Preview/PreviewResultDto.cs` | PreviewResultDto（Level 1）+ PreviewDataDto |
| `Settings/SettingsDto.cs` | SettingsDto |

### frontend/Views/（页面视图）

| 文件 | 作用 |
|---|---|
| `PageSettingsView.xaml` | ConfigCard 包裹：纸张大小/方向/边距(mm+cm单位)/文档网格(模式+行数+字符数+调整右缩进+对齐到网格) |
| `BodyStyleView.xaml` | ConfigCard 包裹：中英文/字号/4 字形勾选(含 strikethrough)/对齐/行距模式+值+单位/段前段后间距+单位/特殊缩进(firstLine/hanging/none)+值+单位 |
| `HeadingStyleView.xaml` | ConfigCard 包裹：级别选择/字体/字号/3 字形/对齐/行距/段间距+单位/缩进 类型+值+单位；_isLoadingFields 守卫 |
| `HeaderFooterView.xaml` | ConfigCard 包裹：字体/字号/4 字形(含 strikethrough+underline)/对齐/页眉页脚距离+单位(mm/cm) |
| `PictureSettingsView.xaml` | ConfigCard 包裹：尺寸模式/宽高+单位/保持比例/不放大/对齐/文字环绕/压缩质量+最大像素/自动压缩（全部已实现） |
| `TableSettingsView.xaml` | ~20 组控件：对齐/宽度模式+值+%(百分比单位)/自适应列宽/行高模式+值+单位/表头字体字号样式/特殊缩进/单元格对齐H+V/背景色/边框样式+颜色+宽度/单元格边距+单位/表头加粗/表头居中/跨页断行/每页重复表头（全部已实现） |
| `AdvancedSettingsView.xaml` | ConfigCard 包裹：语言(简体中文)/主题(浅色) ComboBox |
| `AboutView.xaml` | ConfigCard 包裹：图标+名称+版本+描述+GitHub HyperlinkButton+邮箱+版权 |
| `PreviewWindow.xaml` | 独立窗口：WebView2 控件 + 加载动画 + 底部工具栏(100%/适应宽度/适应页面/← →翻页/页码/关闭) |
| `FormatControlView.xaml` | 模板选择+保存为模板、输出目录+选择+打开、预览+开始排版+取消、进度条、预览文本区 |
| `ResultHistoryView.xaml` | 结果摘要(成功/失败/耗时)、失败文件展开列表+再次尝试、历史记录折叠面板(懒加载/清空/重新执行) |
| `TemplateManagementView.xaml` | 模板列表(含删除非默认)+ 导入/导出按钮 |

### frontend/Controls/（自定义控件）

| 文件 | 作用 |
|---|---|
| `TitleBar.xaml` | 高度 32px：左图标(32x32 Image，Logo.png) + 右 136px 系统按钮预留区；Transparent 背景 |
| `NavBar.xaml` | 左栏容器：ListBox + 透明项；used by code-behind to populate 8 ItemSelector items |
| `ItemSelector.xaml` | 单个导航项：FontIcon(16px) + TextBlock(14px)；VisualStateManager 驱动 Normal/Hover/Selected 三种状态 |
| `StatusBar.xaml` | 高度 32px：状态指示点(8x8 Ellipse, SystemAccentColor) + 文本 + 模板名 + 文件数 + 版本；5 个 DP（StatusText/StateKind/FileCount/TemplateName/CurrentFile）|
| `ConfigCard.xaml` | 可复用配置卡片：圆角 8px, CardBackgroundFillColorDefaultBrush, Padding=0,16,24,24；标题(SubtitleTextBlockStyle) + ContentPresenter（无底部操作按钮，改为全局 SaveBar）|
| `NumericTextBox.xaml` | 自定义数字输入：TextBox + 上下 RepeatButton；DP: Value/Min/Max/Step/DecimalPlaces/IsReadOnly/PlaceholderText；鼠标滚轮和键盘支持；选择高亮 #005FBA |

### frontend/ViewModels/（MVVM ViewModel）

| 文件 | 作用 |
|---|---|
| `MainViewModel.cs` | 统一 DataContext，聚合 12 个子 VM；SharedProfile(ProfileConfigDto) 唯一真相源；IsDirty+DirtyStatusText+CanSave；SaveProfileCommand/ResetProfileCommand；WireDirtyTracking()；RefreshTemplateName(" \*" 后缀)；InitializeAsync() |
| `PageSettingsViewModel.cs` | PageConfig 属性(12个: 纸张/方向/4边距/页码/5文档网格) + LoadFromSharedProfile/WriteToSharedProfile |
| `BodyStyleViewModel.cs` | BodyConfig 属性(14个: 字体/字号/4样式/对齐/行距3属性/缩进3属性/段间距+单位) + DTO 双向同步 |
| `HeadingStyleViewModel.cs` | HeadingStyleConfig 属性(15+个: 级别/字体/字号/3样式/颜色/对齐/行距/缩进/间距+单位) + CurrentHeadingLevel 切换 |
| `HeaderFooterViewModel.cs` | HeaderFooterConfig 属性(10+个: 字体/字号/4样式/对齐/距离+单位) + DTO 同步 |
| `PictureSettingsViewModel.cs` | PictureConfig 属性(13个: 模式/宽高+单位/比例/不放大/对齐/环绕/压缩) + DTO 同步 |
| `TableSettingsViewModel.cs` | TableConfig 属性(23个: 对齐/宽度/自适应/行高/表头/样式/缩进/对齐H+V/背景/边框/边距/跨页/重复表头) + DTO 同步；fontBold 联动 headerBold |
| `FilesViewModel.cs` | 文件管理: Files(ObservableCollection<FileItemDto>)/SearchKeyword/Recent/SearchCommand/AddFiles/AddFolder/RemoveSelected/Clear/ReloadAsync |
| `FormatViewModel.cs` | 排版控制: Templates/SelectedTemplate/OutputDir/CurrentProfile/StartFormatCommand/PreviewCommand/CancelCommand/Progress/Results/HasFailedFiles |
| `TemplateManagementViewModel.cs` | 模板管理: Templates 列表/ImportTemplateCommand/ExportTemplateCommand |
| `HistoryViewModel.cs` | 历史记录: Records/IsLoading/LoadCommand/ClearCommand/ReuseCommand |
| `SettingsViewModel.cs` | 全局设置: Language/Theme/AutoCheckUpdate（当前 AdvancedSettingsView 未绑定） |

### frontend/Services/（前端服务）

| 文件 | 作用 |
|---|---|
| `ApiService.cs` | HTTP 客户端：18 个 API 方法，HttpClient + JSON，返回强类型 DTO，零 JsonElement/JsonDocument |
| `ThemeService.cs` | 本地主题管理：Apply(light/dark/system)，设置 MainWindow.Content.RequestedTheme |
| `TrayIconService.cs` | 系统托盘图标：最小化到托盘、托盘菜单（显示/退出）、点击恢复 |
| `DocumentPreviewService.cs` | PDF 预览策略：WPS 优先（3 ProgIds）→ Word 备用；WarmUp() 预启动 WPS COM |
| `IDocumentPdfConverter.cs` | PDF 转换器接口：ConvertToPdfAsync(sourceFile) + IsAvailable |
| `WpsPdfConverter.cs` | WPS COM 转换器：单例 Application、late-bound COM(Type.InvokeMember)、ExportAsFixedFormat(WdExportFormatPDF=17) |
| `WordPdfConverter.cs` | Word COM 转换器：每次新建 Application、ExportAsFixedFormat(17) |
| `FontSizeConverter.cs` | 字号转换工具 |

### frontend/Styles/（样式与主题）

| 文件 | 作用 |
|---|---|
| `LightTheme.xaml` | 浅色主题：7 个自定义 SolidColorBrush（NavBar 相关 6 个 + FileListBorderBrush 1 个）；作为 Default+Light ThemeDictionary |

### frontend/Controls/（其他）

| 文件 | 作用 |
|---|---|
| `Converters/BoolToVisibilityConverter.cs` | BoolToVisibilityConverter + InvertBoolToVisibilityConverter（用于模板删除按钮可见性反相） |
| `Utilities/NumericUnitConfig.cs` | NumericUnitConfig：数值+单位+范围配置 |
| `Utilities/NumericUnitConfigProvider.cs` | 提供各字段的 NumericUnitConfig 工厂方法 |
| `Behaviors/NumberBoxBehavior.cs` | NumberBox 行为（旧，可能未使用） |

---

## shared/（前后端共享）

| 文件 | 作用 |
|---|---|
| `schemas.py` | 17 个 Pydantic DTO：FileItem/DocumentGridConfig/PageConfig/HeaderFooterConfig/BodyConfig/HeadingStyleConfig/PictureConfig/TableConfig/ProfileConfig/Template/Task/FileResult/TaskResult/PreviewResult/HistoryRecord/HistoryFileItem/Settings/LogEntry；_default_headings() 工厂函数（6 级独立实例）|
| `constants.py` | 共享常量：FONT_SIZE_MAP/FONT_SIZE_NAMES/font_size_to_name/PAPER_SIZES；ErrorCode(0+1000-1010)；TaskState(7 状态)；FileProcessingStatus(4 状态) |
| `version.py` | VERSION = "2.1" |
| `__init__.py` | 包标识 |

---

> 最后更新：2026-07-15
