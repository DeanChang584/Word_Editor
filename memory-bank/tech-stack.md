# Word Formatter — 技术栈与架构文档

> Version: 2.1 | Platform: Windows 11 | Architecture: WinUI 3 + FastAPI + Python Engine | Updated: 2026-07

---

# 1. 产品定位

Word Formatter 是一款 **Windows 原生 Word 批量排版软件**，不是脚本工具。目标用户为教师、学生、公务员、编辑等非技术人群。

设计约束：

- 长期维护（模板、预览、规则引擎持续迭代）
- 上百个独立配置项，支持 1000+ 文件批量处理
- 非技术用户可操作，界面体验优先
- Windows 专用，无需跨平台

---

# 2. 总体架构

前后端分离。WinUI3 客户端通过 HTTP/JSON 与本地 Python 后端通信。

```
┌──────────────────────────────────────────────────────┐
│                   WinUI 3 (C# / .NET 9)              │
│  Views ──▶ ViewModels ──▶ Services ──▶ HttpClient   │
└──────────────────────┬───────────────────────────────┘
                       │  HTTP REST / JSON
                       │  127.0.0.1:8765
┌──────────────────────▼───────────────────────────────┐
│                 FastAPI (Python 3.14)                 │
│  Routes ──▶ Services ──▶ Engine ──▶ python-docx      │
│                              ──▶ SQLite               │
└──────────────────────────────────────────────────────┘
```

---

# 3. 技术栈

## 3.1 前端 — WinUI 3

| 技术                    | 版本                  | 用途           |
| --------------------- | ------------------- | ------------ |
| WinUI 3               | Windows App SDK 2.2 | Windows 原生界面 |
| .NET                  | 9                   | Runtime      |
| C#                    | 13                  | 开发语言         |
| XAML                  | WinUI 3             | UI 描述        |
| CommunityToolkit.Mvvm | 8.4                 | MVVM 源代码生成   |
| WebView2              | (WinUI 内置)        | PDF 预览渲染     |
| PDF.js                | 4.x                 | PDF 查看器       |

负责：窗口管理、页面布局、文件选择器、拖放、模板管理、进度显示、动画、标题栏、键盘快捷键、PDF 预览（WPS/Word COM + PDF.js）。

禁止：Word 排版、文档解析、HTTP 请求细节。

## 3.2 后端 — Python + FastAPI

| 技术          | 版本    | 用途              |
| ----------- | ----- | --------------- |
| Python      | 3.14  | 主语言             |
| FastAPI     | 最新稳定版 | HTTP API        |
| Uvicorn     | 最新稳定版 | ASGI 服务器        |
| python-docx | 最新稳定版 | Word 排版         |
| pywin32     | 最新稳定版 | COM 调用 Word/WPS |

负责：排版引擎、模板读写、文件扫描、批量处理、历史记录、配置持久化。

禁止：直接操作 UI、直接访问 HTTP。

## 3.3 通信 — HTTP REST + JSON

- 协议：HTTP，端口 8765
- 格式：JSON（camelCase，UTF-8）。后端 Python 内部使用 snake_case，通过 Pydantic `alias_generator` 转换输出。
- DTO 定义：`shared/schemas.py`（前后端保持完全一致）

## 3.4 数据存储 — SQLite

保存：最近文件、最近目录、固定目录、模板信息、最近任务、用户设置、窗口状态。无需安装数据库。

## 3.5 配置 — JSON（带版本号）

```json
{
  "version": 2,
  "page": { "margin_top": 25.4, "paper_size": "A4" },
  "body": { "font_cn": "宋体", "font_size": 12.0 },
  "headings": { "1": { "font_cn": "黑体", "font_size": 22.0 } }
}
```

版本号用于模板升级时的迁移兼容，避免旧模板全部失效。

---

# 4. 项目目录

```
WordFormatter/
├── frontend/                     # WinUI 3 前端 (.NET 9)
│   ├── MainWindow.xaml           # 主窗口（三栏自适应布局）
│   ├── MainWindow.xaml.cs        # 事件处理 + ViewModel 绑定
│   ├── ViewModels/               # MVVM ViewModel 层（12 个 VM）
│   ├── Services/                 # ApiService, ThemeService, TrayIconService
│   │                             # DocumentPreviewService, IDocumentPdfConverter
│   │                             # WpsPdfConverter, WordPdfConverter
│   ├── Models/                   # 前端 DTO 定义（7 个子目录）
│   ├── Assets/                   # 图标 + PDF.js (pdfjs/)
│   ├── Styles/                   # LightTheme.xaml
│   ├── Controls/                 # 自定义控件（7 个：TitleBar/NavBar/ItemSelector/
│   │                             #   StatusBar/ConfigCard/NumericTextBox/TitleBar）
│   ├── Converters/               # 值转换器
│   ├── Views/                    # 页面视图（12 个）
│   └── App.xaml / App.xaml.cs    # 应用入口 + 健康检测 + WPS/WebView2 预热
│
├── backend/                      # FastAPI 后端
│   ├── server.py                 # 入口 + CORS + 7 个路由注册
│   ├── api/                      # 新 API 层（8 个文件：health/files/profile/
│   │                             #   templates/format/history/preview/__init__）
│   ├── services/                 # 业务服务（file/format/template）
│   ├── formatter/                # 排版引擎（7 个模块：engine/page/paragraph/
│   │                             #   heading/image/table/header_footer/font/data_model）
│   ├── history/                  # 历史记录管理器
│   ├── preview/                  # 预览文本生成器
│   ├── config/                   # 配置管理（defaults/manager）
│   └── utils/                    # 工具（logger/response）
│
├── shared/                       # 前后端共享契约
│   ├── schemas.py                # 17 个 Pydantic DTO
│   ├── constants.py              # 常量（字号/纸张/错误码/任务状态）
│   └── version.py                # VERSION = "2.1"
│
├── memory-bank/                  # 设计文档（8 个文件）
├── logs/                         # 日志（滚动保存，30+90 天保留）
├── config/                       # 运行时配置（settings.json + templates/ + history/）
├── tests/                        # 测试（61 passed）
└── run.bat                       # 一键启动脚本
```

---

# 5. 分层设计

## 5.1 UI（Views）

负责：页面显示、控件布局、动画、用户交互。

禁止：排版逻辑、文件操作、HTTP 请求。

## 5.2 ViewModel

负责：页面状态、ICommand、属性绑定。

禁止：操作 Word、业务计算。

## 5.3 ApiService

负责：HTTP（GET/POST/PUT/DELETE），返回 DTO。

## 5.4 Routes

负责：接收 HTTP 请求，参数校验。

禁止：业务逻辑（委托给 Services）。

## 5.5 Services

负责：真正的业务逻辑。例如：开始排版、删除文件、加载配置。

## 5.6 Engine（排版引擎）

负责：Word 排版。采用规则引擎模式：

```
Engine ──▶ Rule Engine
               ├── PageRule        (纸张、边距、方向)
               ├── ParagraphRule   (行距、缩进、对齐)
               ├── HeadingRule     (标题 1-6 级)
               ├── ImageRule       (统一尺寸、对齐)
               ├── TableRule       (边框、居中、字体)
               └── HeaderRule      (页眉页脚)
```

每个排版元素拆为独立 Rule。增加新功能只需新增规则，不改核心代码。

禁止：HTTP、UI、数据库。

## 5.7 Repository

负责：数据存储（SQLite、JSON、Memory）。

## 5.8 任务队列（规划中）

```
Task Manager
    ├── Pending
    ├── Running
    ├── Success
    ├── Failed
    └── Cancelled
```

暂停、取消、重试、统计统一管理。不再 `for file in files` 裸循环。

---

# 6. 数据流

```
用户 ──▶ WinUI ──▶ ViewModel ──▶ ApiService
                                      │
                               HTTP POST/GET
                                      │
                               FastAPI Route
                                      │
                                 Service
                                      │
                                 Engine ──▶ python-docx ──▶ Word
                                      │
                                 Repository ──▶ SQLite
```

---

# 7. 排版流程

```
POST /api/format/start {files, profile}
        │
        ▼
  Task Manager ──▶ Pending ──▶ Running
        │                         │
        │              ┌──────────┴──────────┐
        │              ▼                     ▼
        │         Success              Failed / Cancelled
        │              │                     │
        │              └──────────┬──────────┘
        ▼                         ▼
  GET /api/format/{id}/progress  (每 500ms 轮询)
        │
        ▼
  GET /api/format/{id}/result
        │
        ▼
  UI 显示结果汇总（✓/✗ 逐文件）
```

---

# 8. 设计模式 — MVVM

```
View (XAML)
    │  Binding
    ▼
ViewModel
    │  ICommand / async
    ▼
ApiService
    │  HTTP
    ▼
Backend (FastAPI)
```

约束：

- View 不允许直接访问 Backend
- ViewModel 不允许访问 Engine
- Service 不允许访问 UI
- Route 不写业务
- UI 不写业务

---

# 9. DTO

统一放在 `shared/` 目录。主要 DTO：

```
ProfileDTO ── PageDTO, BodyDTO, ParagraphDTO, HeadingDTO
FilesResponse
FormatStartRequest / FormatStartResponse
FormatProgressResponse / FormatResultResponse
ThemeResponse / ThemeRequest
HealthResponse
```

前后端保持完全一致（camelCase 命名，见 Q1 决议）。

---

# 10. 日志

目录：`logs/`

| 日志文件          | 内容           |
| ------------- | ------------ |
| `app.log`     | 应用启动、关闭、主题切换 |
| `backend.log` | API 请求、响应时间  |
| `format.log`  | 排版进度、文件处理    |
| `error.log`   | 异常堆栈         |

禁止 `print()`，统一使用 `logging` 模块。日志滚动保存。

---

# 11. 配置

统一放在 `config/` 目录：

- `default_profile.py` — 默认排版参数
- `theme.py` — 主题颜色定义
- `constants.py` — 常量（字号映射等）

不要把默认值写死在代码里。

---

# 12. 缓存

```
cache/   — 索引、缩略图等可重建数据
temp/    — 格式转换中间文件（.doc → .docx）
output/  — 排版输出文件
```

禁止把缓存放项目根目录。

---

# 13. API

以下路径对齐 API.md v2.0 及当前后端实现。

| 方法     | 路径                            | 说明         |
| ------ | ----------------------------- | ---------- |
| GET    | `/api/health`                 | 健康检查       |
| GET    | `/api/files`                  | 获取文件列表     |
| POST   | `/api/files/add`              | 添加文件       |
| POST   | `/api/files/add-folder`       | 导入文件夹      |
| POST   | `/api/files/remove`           | 移除文件       |
| DELETE | `/api/files`                  | 清空文件列表     |
| POST   | `/api/files/search`           | 搜索文件       |
| GET    | `/api/files/recent`           | 最近打开记录     |
| POST   | `/api/files/pin`              | 固定常用目录     |
| GET    | `/api/profile`                | 获取排版配置     |
| PUT    | `/api/profile`                | 更新排版配置     |
| POST   | `/api/profile/reset`          | 恢复默认配置     |
| GET    | `/api/templates`              | 获取模板列表     |
| POST   | `/api/templates`              | 创建模板       |
| PUT    | `/api/templates/{id}`         | 更新模板       |
| DELETE | `/api/templates/{id}`         | 删除模板       |
| POST   | `/api/templates/import`       | 导入模板       |
| POST   | `/api/templates/export`       | 导出模板       |
| POST   | `/api/templates/default`      | 设为默认模板     |
| POST   | `/api/format/start`           | 启动排版（HTTP 202） |
| GET    | `/api/format/status/{taskId}` | 查询任务状态     |
| POST   | `/api/format/cancel`          | 取消任务       |
| GET    | `/api/format/result/{taskId}` | 获取任务结果     |
| POST   | `/api/preview`                | 生成预览       |
| GET    | `/api/history`                | 获取历史记录列表   |
| GET    | `/api/history/{id}`           | 获取历史记录详情   |
| DELETE | `/api/history`                | 清空历史记录     |
| GET    | `/api/settings`               | 获取软件设置（待实现） |
| PUT    | `/api/settings`               | 更新软件设置（待实现） |

---

# 14. 主题

支持三种模式：Light、Dark、Follow System。

WinUI `ThemeService` 统一管理，前端本地切换，不依赖后端。

---

# 15. 命名规范

| 语言         | 规范                      | 示例                              |
| ---------- | ----------------------- | ------------------------------- |
| Python     | snake_case              | `margin_top`                    |
| C# 类/方法    | PascalCase              | `GetFilesAsync()`               |
| C# 私有字段    | camelCase               | `_fileCount`                    |
| DTO (JSON) | camelCase               | `"filePath"`                    |
| 文件名        | snake_case 或 PascalCase | `files.py`, `FilesViewModel.cs` |

---

# 16. 异步

所有耗时操作必须 async，禁止阻塞 UI：

| 操作      | 方式        |
| ------- | --------- |
| 扫描目录    | async     |
| 读取模板    | async     |
| Word 排版 | 后台线程 + 轮询 |
| 预览      | async     |
| 历史查询    | async     |
| HTTP 请求 | async     |

---

# 17. 异常处理

所有异常：统一捕获、统一日志、统一提示（ContentDialog）。不得直接崩溃。

- 前端：`App.UnhandledException` 全局捕获
- 后端：`try/except` + FastAPI 异常中间件

---

# 18. AI 开发规范

AI Agent 修改代码必须遵守：

- 不得修改 API
- 不得修改 DTO
- 不得修改 Engine 接口
- Route 不写业务
- UI 不写业务
- ViewModel 不访问 Engine
- Service 不访问 UI
- 保持 MVVM
- 新增功能优先扩展，不重写
- 每次修改一个模块
- 不得随意移动项目目录
- 所有公共接口保持向后兼容

---

# 19. 性能目标

| 指标          | 目标       |
| ----------- | -------- |
| 冷启动         | < 2 秒    |
| 配置面板切换      | < 100 ms |
| 100 个 .docx | < 10 秒   |
| 1000 个文件    | UI 保持流畅  |

---

# 20. 开发流程

```
设计 ──▶ Backend ──▶ API ──▶ ViewModel ──▶ UI ──▶ 联调 ──▶ 测试 ──▶ 发布
```

---

# 21. 发布流程

```
Backend ──▶ PyInstaller ──▶ WordFormatterBackend.exe
Frontend ──▶ dotnet publish ──▶ WordFormatter.exe
                  │
                  ▼
          Inno Setup ──▶ 安装包
```

---

# 22. 开发原则

- UI 与业务彻底解耦
- 一个模块只负责一件事
- 所有业务逻辑进入 Service
- 所有排版逻辑进入 Engine
- 所有配置统一管理
- 所有默认值集中维护
- 所有 API 保持稳定
- 所有代码便于 AI Agent 阅读与修改
- 优先可维护，其次性能，最后才是代码量

---

# 23. Roadmap

| 版本   | 内容                                    |
| ---- | ------------------------------------- |
| V2.0 | WinUI3 + FastAPI 基础架构、Word 排版、深浅色主题 ✅ |
| V2.1 | SQLite 持久化、配置模板、最近打开                  |
| V2.2 | 排版预览、图片/表格排版、页眉页脚编辑器、自动更新             |
| V2.3 | 规则引擎重构、模板市场                           |

当前重心：**规则引擎、预览系统、批处理稳定性**。
