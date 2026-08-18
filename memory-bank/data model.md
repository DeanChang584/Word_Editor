# Word Formatter 数据模型文档（Data Model）

> **Version**：2.1
> **Last Update**：2026-07-15
> **Architecture**：WinUI 3 + Python Backend

---

## 目录

1. [设计目标](#1-设计目标)
2. [数据模型总览](#2-数据模型总览)
3. [文件模型](#3-文件模型-file)
4. [排版配置模型](#4-排版配置模型-profile)
    - 4.1 [页面设置](#41-页面设置-page)
    - 4.2 [页眉页脚](#42-页眉页脚-headerfooter)
    - 4.3 [正文样式](#43-正文样式-body)
    - 4.4 [文档网格](#44-文档网格-documentgrid)
5. [标题样式模型](#5-标题样式模型-headingstyle)
6. [图片设置模型](#6-图片设置模型-picture)
7. [表格设置模型](#7-表格设置模型-table)
8. [模板模型](#8-模板模型-template)
9. [排版任务模型](#9-排版任务模型-task)
10. [排版结果模型](#10-排版结果模型-result)
11. [预览模型](#11-预览模型-preview)
12. [历史记录模型](#12-历史记录模型-history)
13. [软件设置模型](#13-软件设置模型-settings)
14. [日志模型](#14-日志模型-log)
15. [JSON 命名规范](#15-json-命名规范)
16. [数据版本管理](#16-数据版本管理)
17. [附录](#附录)

---

## 1. 设计目标

本文档定义 Word Formatter 系统中全部数据对象（Data Model），作为以下模块的唯一数据契约：
- API 请求与响应
- JSON 配置文件
- 模板文件结构
- 历史记录存储
- 前后端数据交换

所有系统模块必须严格遵守本模型定义，任何增删改字段均需同步更新本文档。

---

## 2. 数据模型总览

系统包含以下核心数据对象：

| 对象               | 说明                     |
| ------------------ | ------------------------ |
| File               | 待处理 Word 文件         |
| Profile            | 排版配置（参数集）       |
| DocumentGrid       | 文档网格设置             |
| HeadingStyle       | 标题样式（1~6 级）       |
| Picture            | 图片设置                 |
| Table              | 表格设置                 |
| Template           | 模板（封装 Profile）     |
| Task               | 排版任务                 |
| TaskResult         | 任务执行结果             |
| Preview            | 排版预览信息             |
| Preview (PDF)      | 真实 PDF 预览（Level 2） |
| History            | 历史任务记录             |
| Settings           | 软件全局设置             |
| Log                | 运行日志条目             |

---

## 3. 文件模型（File）

```json
{
  "id": "uuid",
  "name": "document.docx",
  "path": "C:/Users/.../document.docx",
  "size": 102400,
  "modifiedTime": "2026-07-01T12:00:00Z",
  "status": "waiting"
}
```

| 字段         | 类型   | 说明                                                  |
| ------------ | ------ | ----------------------------------------------------- |
| id           | string | 文件唯一标识符（UUID）                                |
| name         | string | 文件名（含扩展名）                                    |
| path         | string | 文件完整路径                                          |
| size         | int    | 文件大小（字节）                                      |
| modifiedTime | string | 最后修改时间（ISO 8601）                              |
| status       | enum   | 处理状态：`waiting` / `running` / `done` / `error`   |

---

## 4. 排版配置模型（Profile）

Profile 是全部排版参数的集合，结构如下：

```
Profile
├── Page             # 页面设置
├── HeaderFooter     # 页眉页脚
├── Body             # 正文样式
├── Heading[1..6]    # 1~6 级标题样式
├── Picture          # 图片设置
└── Table            # 表格设置
```

### 4.1 页面设置（Page）

```json
{
  "paperSize": "A4",
  "orientation": "portrait",
  "marginTop": 25.4,
  "marginBottom": 25.4,
  "marginLeft": 31.7,
  "marginRight": 31.7,
  "pageNumber": true,
  "customWidth": 210.0,
  "customHeight": 297.0,
  "documentGrid": {
    "mode": "无网格",
    "linesPerPage": 30,
    "charsPerLine": 35,
    "adjustRightIndent": true,
    "alignToGrid": true
  }
}
```

| 字段         | 类型           | 说明                                     |
| ------------ | -------------- | ---------------------------------------- |
| paperSize    | string         | 纸张规格（A4/A3/B5/Letter/Legal/custom） |
| orientation  | enum           | 方向：`portrait` / `landscape`           |
| marginTop    | float          | 上边距（mm）                             |
| marginBottom | float          | 下边距（mm）                             |
| marginLeft   | float          | 左边距（mm）                             |
| marginRight  | float          | 右边距（mm）                             |
| pageNumber   | boolean        | 是否显示页码                             |
| customWidth  | float          | 自定义纸张宽度（mm，仅 paperSize=custom）|
| customHeight | float          | 自定义纸张高度（mm）                     |
| documentGrid | DocumentGrid   | 文档网格配置（见 4.4）                   |

### 4.2 页眉页脚（HeaderFooter）

```json
{
  "fontCn": "宋体",
  "fontEn": "Times New Roman",
  "fontSize": 10.5,
  "fontStyle": "normal",
  "alignment": "center",
  "headerDistance": 15.0,
  "footerDistance": 17.5,
  "headerDistanceUnit": "mm",
  "footerDistanceUnit": "mm"
}
```

| 字段               | 类型   | 说明                                                   |
| ------------------ | ------ | ------------------------------------------------------ |
| fontCn             | string | 中文字体名                                             |
| fontEn             | string | 西文字体名                                             |
| fontSize           | float  | 字体大小（pt）                                         |
| fontStyle          | string | 空格分隔：`normal` / `bold` / `italic` / `underline` / `strikethrough` |
| alignment          | enum   | 对齐：`left` / `center` / `right`                      |
| headerDistance     | float  | 页眉距顶端距离（mm 或 cm，由 headerDistanceUnit 决定） |
| footerDistance     | float  | 页脚距底端距离（mm 或 cm，由 footerDistanceUnit 决定） |
| headerDistanceUnit | string | 页眉距离单位：`mm` / `cm`                              |
| footerDistanceUnit | string | 页脚距离单位：`mm` / `cm`                              |

### 4.3 正文样式（Body）

```json
{
  "fontCn": "宋体",
  "fontEn": "Times New Roman",
  "fontSize": 12.0,
  "fontStyle": "normal",
  "alignment": "justify",
  "lineSpacing": 1.5,
  "lineSpacingMode": "multiple",
  "lineSpacingUnit": "pt",
  "indentType": "firstLine",
  "indentValue": 2.0,
  "indentUnit": "字符",
  "spaceBefore": 0.0,
  "spaceAfter": 0.0,
  "spaceBeforeUnit": "行",
  "spaceAfterUnit": "行"
}
```

| 字段            | 类型   | 说明                                                   |
| --------------- | ------ | ------------------------------------------------------ |
| fontCn          | string | 中文字体                                               |
| fontEn          | string | 西文字体                                               |
| fontSize        | float  | 字号（pt）                                             |
| fontStyle       | string | 空格分隔：`normal` / `bold` / `italic` / `underline` / `strikethrough` |
| alignment       | enum   | 对齐：`left` / `center` / `right` / `justify`          |
| lineSpacing     | float  | 行距值（倍数模式下为 1.0/1.5/2.0，固定值模式下为 pt）  |
| lineSpacingMode | enum   | 行距模式：`multiple` / `fixed` / `at_least`            |
| lineSpacingUnit | string | 固定/最小值模式下的单位：`pt`                           |
| indentType      | enum   | 缩进类型：`firstLine` / `hanging` / `none`             |
| indentValue     | float  | 缩进量（单位由 indentUnit 决定，字符模式下 2 表示 2 字符）|
| indentUnit      | string | 缩进单位：`字符` / `pt` / `mm` / `cm`                  |
| spaceBefore     | float  | 段前间距                                               |
| spaceAfter      | float  | 段后间距                                               |
| spaceBeforeUnit | string | 段前间距单位：`pt` / `行`                               |
| spaceAfterUnit  | string | 段后间距单位：`pt` / `行`                               |

### 4.4 文档网格（DocumentGrid）

```json
{
  "mode": "无网格",
  "linesPerPage": 30,
  "charsPerLine": 35,
  "adjustRightIndent": true,
  "alignToGrid": true
}
```

| 字段              | 类型    | 说明                                        |
| ----------------- | ------- | ------------------------------------------- |
| mode              | enum    | 网格模式：`无网格` / `只指定行网格` / `指定行和字符网格` |
| linesPerPage      | int     | 每页行数                                    |
| charsPerLine      | int     | 每行字符数                                  |
| adjustRightIndent | boolean | 调整右缩进                                  |
| alignToGrid       | boolean | 对齐到网格                                  |

---

## 5. 标题样式模型（HeadingStyle）

标题样式用于 Heading 1 ~ Heading 6，每级独立配置。Profile 中使用字符串键 "1"~"6"。

```json
{
  "level": 1,
  "fontCn": "黑体",
  "fontEn": "Times New Roman",
  "fontSize": 22.0,
  "fontStyle": "bold",
  "fontColor": "#000000",
  "alignment": "left",
  "lineSpacing": 1.5,
  "lineSpacingMode": "multiple",
  "lineSpacingUnit": "pt",
  "indentType": "none",
  "indentValue": 0.0,
  "indentUnit": "字符",
  "spaceBefore": 1.0,
  "spaceAfter": 1.0,
  "spaceBeforeUnit": "行",
  "spaceAfterUnit": "行"
}
```

| 字段            | 类型   | 说明                                                   |
| --------------- | ------ | ------------------------------------------------------ |
| level           | int    | 标题级别（1~6）                                        |
| fontCn          | string | 中文字体                                               |
| fontEn          | string | 西文字体（默认 "Times New Roman"）                     |
| fontSize        | float  | 字号（pt）                                             |
| fontStyle       | string | 空格分隔：`normal` / `bold` / `italic` / `underline`   |
| fontColor       | string | 字体颜色（hex，默认 "#000000"）                        |
| alignment       | enum   | 对齐方式：`left` / `center` / `right` / `justify`      |
| lineSpacing     | float  | 行距值                                                 |
| lineSpacingMode | enum   | 行距模式：`multiple` / `fixed` / `at_least`            |
| lineSpacingUnit | string | 固定/最小值模式下的单位                                |
| indentType      | enum   | 缩进类型：`firstLine` / `hanging` / `none`             |
| indentValue     | float  | 缩进值                                                 |
| indentUnit      | string | 缩进单位：`字符` / `pt` / `mm` / `cm`                  |
| spaceBefore     | float  | 段前间距                                               |
| spaceAfter      | float  | 段后间距                                               |
| spaceBeforeUnit | string | 段前间距单位：`pt` / `行`                               |
| spaceAfterUnit  | string | 段后间距单位：`pt` / `行`                               |

各级默认值：

| 级别 | 字号(中) | 字号(pt) | 字体 | 加粗 | 对齐 | 段前(行) | 段后(行) | 行距    |
| ---- | -------- | -------- | ---- | ---- | ---- | -------- | -------- | ------- |
| 1    | 二号     | 22.0     | 黑体 | 是   | 左   | 1.0      | 1.0      | 1.5 倍  |
| 2    | 三号     | 16.0     | 黑体 | 是   | 左   | 1.0      | 1.0      | 1.5 倍  |
| 3    | 小三     | 15.0     | 黑体 | 是   | 左   | 0.5      | 0.5      | 1.5 倍  |
| 4    | 四号     | 14.0     | 黑体 | 是   | 左   | 0.5      | 0.5      | 1.5 倍  |
| 5    | 小四     | 12.0     | 黑体 | 是   | 左   | 0.25     | 0.25     | 1.5 倍  |
| 6    | 五号     | 10.5     | 黑体 | 是   | 左   | 0.25     | 0.25     | 1.5 倍  |

---

## 6. 图片设置模型（Picture）

```json
{
  "sizeMode": "fixedWidth",
  "width": 12.0,
  "widthUnit": "cm",
  "height": 8.0,
  "heightUnit": "cm",
  "keepAspectRatio": true,
  "noEnlarge": false,
  "alignment": "center",
  "wrappingStyle": "inline",
  "compressionQuality": 220,
  "maxPixels": 1200,
  "autoCompress": true
}
```

| 字段               | 类型    | 说明                                                   |
| ------------------ | ------- | ------------------------------------------------------ |
| sizeMode           | enum    | 尺寸模式：`fixedWidth` / `fixedHeight` / `fixedSize` / `originalSize` |
| width              | float   | 统一宽度（单位由 widthUnit 决定）                      |
| widthUnit          | string  | 宽度单位：`cm` / `mm` / `%` / `px`                     |
| height             | float   | 统一高度                                               |
| heightUnit         | string  | 高度单位：`cm` / `mm` / `%` / `px`                     |
| keepAspectRatio    | boolean | 保持纵横比（alias: keepRatio）                         |
| noEnlarge          | boolean | 不放大图片（仅缩小）                                   |
| alignment          | enum    | 对齐方式：`left` / `center` / `right`                  |
| wrappingStyle      | enum    | 文字环绕方式：`inline` / `square` / `tight` / `behindText` / `inFrontOfText` / `topAndBottom` |
| compressionQuality | int     | 压缩质量（PPI，默认 220）                              |
| maxPixels          | int     | 最大像素数（压缩阈值）                                 |
| autoCompress       | boolean | 自动压缩图片                                           |

---

## 7. 表格设置模型（Table）

```json
{
  "tableAlignment": "center",
  "widthMode": "auto",
  "widthValue": 0.0,
  "widthUnit": "cm",
  "autoFitColumns": true,
  "rowHeightMode": "auto",
  "rowHeight": 0.8,
  "rowHeightUnit": "cm",
  "headerFontCn": "宋体",
  "headerFontEn": "Times New Roman",
  "headerSize": 10.5,
  "headerBold": true,
  "headerTextCenter": true,
  "headerBgColor": "",
  "fontBold": false,
  "fontItalic": false,
  "fontUnderline": false,
  "indentType": "none",
  "indentValue": 0.0,
  "indentUnit": "字符",
  "cellAlignH": "left",
  "cellAlignV": "middle",
  "borderStyle": "all",
  "borderColor": "#000000",
  "borderWidth": 0.5,
  "cellMargin": 0.19,
  "cellMarginUnit": "cm",
  "autoSplit": true,
  "repeatHeader": false
}
```

| 字段             | 类型    | 说明                                                       |
| ---------------- | ------- | ---------------------------------------------------------- |
| tableAlignment   | enum    | 表格对齐：`left` / `center` / `right`                      |
| widthMode        | enum    | 宽度模式：`auto` / `fixed` / `percent`                     |
| widthValue       | float   | 表格宽度值                                                 |
| widthUnit        | string  | 宽度单位：`cm` / `mm` / `%`                                |
| autoFitColumns   | boolean | 自动调整列宽                                               |
| rowHeightMode    | enum    | 行高模式：`auto` / `fixed` / `at_least`                    |
| rowHeight        | float   | 行高值                                                     |
| rowHeightUnit    | string  | 行高单位：`cm` / `mm`                                      |
| headerFontCn     | string  | 表头中文字体                                               |
| headerFontEn     | string  | 表头西文字体                                               |
| headerSize       | float   | 表头字号（pt）                                             |
| headerBold       | boolean | 表头加粗                                                   |
| headerTextCenter | boolean | 表头文字居中                                               |
| headerBgColor    | string  | 表头背景色（hex，空字符串 = 无）                           |
| fontBold         | boolean | 全表加粗（自动联动 headerBold）                            |
| fontItalic       | boolean | 全表倾斜                                                   |
| fontUnderline    | boolean | 全表下划线                                                 |
| indentType       | enum    | 缩进类型：`firstLine` / `hanging` / `none`                 |
| indentValue      | float   | 缩进量                                                     |
| indentUnit       | string  | 缩进单位：`字符` / `cm` / `mm`                             |
| cellAlignH       | enum    | 水平对齐：`left` / `center` / `right`                      |
| cellAlignV       | enum    | 垂直对齐：`top` / `middle` / `bottom`                      |
| borderStyle      | enum    | 边框样式：`none` / `all` / `horizontal` / `grid`           |
| borderColor      | string  | 边框颜色（hex，默认 "#000000"）                            |
| borderWidth      | float   | 边框宽度（磅，默认 0.5）                                   |
| cellMargin       | float   | 单元格边距                                                 |
| cellMarginUnit   | string  | 单元格边距单位：`cm` / `mm`                                |
| autoSplit        | boolean | 允许表格跨页断行                                           |
| repeatHeader     | boolean | 每页重复表头                                               |

---

## 8. 模板模型（Template）

模板是 Profile 的封装，包含元数据。

```json
{
  "id": "tpl_001",
  "name": "默认模板",
  "version": "2.1",
  "author": "",
  "description": "",
  "createTime": "2026-07-01T08:00:00Z",
  "updateTime": "2026-07-05T16:30:00Z",
  "profile": { },
  "isDefault": true
}
```

系统仅预置 **一份默认模板**（id="default"，名称"默认模板"），用户可自行创建、导入导出。

---

## 9. 排版任务模型（Task）

```json
{
  "taskId": "task_20260701_001",
  "status": "running",
  "createTime": "2026-07-01T10:00:00Z",
  "startTime": "2026-07-01T10:00:05Z",
  "finishTime": null,
  "files": []
}
```

| 字段       | 类型   | 说明                                                                  |
| ---------- | ------ | --------------------------------------------------------------------- |
| taskId     | string | 任务唯一标识符                                                        |
| status     | enum   | 7 种状态：`idle` / `preparing` / `running` / `saving` / `completed` / `failed` / `cancelled` |
| createTime | string | 任务创建时间                                                          |
| startTime  | string | 开始处理时间                                                          |
| finishTime | string | 完成时间                                                              |
| files      | array  | 待处理文件列表                                                        |

---

## 10. 排版结果模型（Result）

**汇总结果**：

```json
{
  "ok": 998,
  "fail": 2,
  "total": 1000,
  "elapsed": 125.6,
  "outputDir": "C:/Output",
  "results": [],
  "failedFiles": []
}
```

| 字段        | 类型   | 说明         |
| ----------- | ------ | ------------ |
| ok          | int    | 成功处理的文件数 |
| fail        | int    | 失败文件数     |
| total       | int    | 总文件数       |
| elapsed     | float  | 总耗时（秒）   |
| outputDir   | string | 输出目录路径   |
| results     | array  | 所有文件结果   |
| failedFiles | array  | 失败文件详情   |

**单文件结果**：

```json
{
  "file": "A.docx",
  "status": "success",
  "output": "A-R.docx",
  "message": ""
}
```

| 字段    | 类型   | 说明                              |
| ------- | ------ | --------------------------------- |
| file    | string | 源文件名                          |
| status  | enum   | `success` / `error` / `skipped`   |
| output  | string | 输出文件名（成功时有效）          |
| message | string | 错误原因                          |

---

## 11. 预览模型（Preview）

### Level 1：参数摘要

```json
{
  "preview": "【纸张】A4 纵向\n【正文】宋体 小四，行距1.5倍，首行缩进2字符\n..."
}
```

### Level 2：PDF 真实预览

```json
{
  "taskId": "preview_xxx",
  "state": "running",
  "previewPath": "C:/temp/preview_xxx.pdf"
}
```

Level 2 预览通过 WPS/Word COM 将格式化后的 .docx 转为 PDF，再通过 WebView2 + PDF.js 渲染。

---

## 12. 历史记录模型（History）

**列表摘要**（GET /api/history 返回）：

```json
{
  "id": "task_20260701_001",
  "time": "2026-07-01T10:30:00Z",
  "duration": 45.2,
  "success": 20,
  "failed": 0,
  "template": "默认模板",
  "fileCount": 20
}
```

**记录详情**（GET /api/history/{id} 返回，含完整 profile/files/results）：

```json
{
  "id": "task_20260701_001",
  "time": "2026-07-01T10:30:00Z",
  "duration": 45.2,
  "success": 20,
  "failed": 0,
  "skipped": 0,
  "template": "默认模板",
  "fileCount": 20,
  "profile": { },
  "files": [
    { "name": "A.docx", "path": "C:/A.docx", "status": "success" }
  ],
  "results": {
    "ok": 20, "fail": 0, "total": 20
  }
}
```

**HistoryFileItem**（历史记录中的单文件）：

```json
{
  "name": "A.docx",
  "path": "C:/A.docx",
  "status": "success"
}
```

---

## 13. 软件设置模型（Settings）

```json
{
  "theme": "system",
  "language": "zh-CN",
  "defaultOutput": "sameFolder",
  "defaultTemplate": "default",
  "recentCount": 20,
  "autoCheckUpdate": true
}
```

| 字段            | 类型    | 说明                               |
| --------------- | ------- | ---------------------------------- |
| theme           | string  | 界面主题：`system` / `light` / `dark` |
| language        | string  | 界面语言                           |
| defaultOutput   | string  | 默认输出方式                       |
| defaultTemplate | string  | 默认模板 ID                        |
| recentCount     | int     | 最近记录保留条数                   |
| autoCheckUpdate | boolean | 是否自动检查更新                   |

---

## 14. 日志模型（Log）

```json
{
  "time": "2026-07-01T10:30:15.123Z",
  "level": "INFO",
  "module": "Formatter",
  "message": "Successfully formatted 20 files."
}
```

| 字段    | 类型   | 说明                                  |
| ------- | ------ | ------------------------------------- |
| time    | string | 日志时间戳（ISO 8601 含毫秒）         |
| level   | enum   | `INFO` / `WARNING` / `ERROR` / `DEBUG`|
| module  | string | 产生日志的模块名                      |
| message | string | 详细消息                              |

---

## 15. JSON 命名规范

**camelCase**。前端 `JsonNamingPolicy.CamelCase`，后端 Pydantic `alias_generator` 转换。

正确：`paperSize`, `fontSize`, `spaceBefore`, `headerDistance`, `keepAspectRatio`
禁止：`paper_size`（snake_case）、`PaperSize`（PascalCase）

布尔字段：`is`/`has`/`enable` 前缀，如 `isRunning`, `keepAspectRatio`。
枚举值：全部小写，如 `left`, `justify`, `firstLine`, `multiple`。

---

## 16. 数据版本管理

所有持久化数据包含 `version` 字段（当前 "2.1"）。

- 向前兼容：新版本软件必须能读取旧版本数据
- 字段新增：为新字段提供默认值
- 字段废弃：至少在一个主版本内保留读取能力
- 模板导入：必须校验版本号

---

## 附录 A：数据对象关系图

```
Template
   │
   ▼
Profile
   ├── Page (含 DocumentGrid)
   ├── HeaderFooter
   ├── Body
   ├── Heading[1..6]
   ├── Picture
   └── Table
   │
   ▼
Task
 ├── File[]
 ├── Preview (Level 1 text / Level 2 PDF)
 ├── TaskResult
 └── History (由 Task 生成)

Settings（独立全局配置）
Log（独立运行时记录）
```

## 附录 B：设计原则

- 平台无关：数据模型独立于 UI 框架及底层存储方式
- 字段一致：同一业务概念在所有位置使用相同字段名
- 高内聚：每个模型仅包含自身相关属性
- 向后兼容：字段新增/变更不得破坏已有客户端
- 独立演进：模型版本独立于软件版本
