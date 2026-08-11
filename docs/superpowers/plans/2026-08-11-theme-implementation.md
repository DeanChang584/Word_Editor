# 主题模式（浅色/深色/跟随系统）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 WordFormatter 增加主题下拉选择（浅色/深色/跟随系统），默认浅色，经后端 settings.json 持久化，跟随系统实时切换。

**Architecture:** 后端新增 `/api/settings` GET/PUT 端点（复用现有 config manager）；前端 `ThemeService.Apply(mode)` 设置各窗口根元素 `ElementTheme`（light/dark/system→Default），新增 `DarkTheme.xaml` 深色画刷字典，`UISettings.ColorValuesChanged` 监听系统主题实时跟随。

**Tech Stack:** FastAPI + Python 3.14（后端）、WinUI 3 / Windows App SDK 2.2.0 + .NET 9（前端）、pytest + httpx（后端测试）。

## Global Constraints

- 默认主题为 `"light"`（spec 需求 2）
- 主题取值仅 `light` / `dark` / `system`（与现有 `settings.json` 键一致）
- 所有 API 响应使用统一信封（`backend.utils.response.success_response`）
- 前端 XAML 新增颜色一律进 ThemeDictionaries（Light/Dark 两字典），禁止 XAML 内硬编码十六进制色
- 每次 commit 只 `git add` 本任务涉及的文件（仓库有无关的未提交删除/新增）
- 后端测试不得写入真实 `%LOCALAPPDATA%\WordFormatter\settings.json`（monkeypatch 到 tmp_path）

---

### Task 1: 后端设置默认值改为 light

**Files:**
- Modify: `backend/config/defaults.py:10`
- Modify: `shared/schemas.py:474`
- Create: `tests/test_settings_defaults.py`

**Interfaces:**
- Produces: `backend.config.defaults.DEFAULT_SETTINGS["theme"] == "light"`；`shared.schemas.Settings().theme == "light"`

- [ ] **Step 1: 写失败测试**

创建 `tests/test_settings_defaults.py`：

```python
"""Unit tests for theme default values (spec: default light)."""

from __future__ import annotations

from backend.config.defaults import DEFAULT_SETTINGS
from shared.schemas import Settings


def test_default_settings_theme_is_light():
    assert DEFAULT_SETTINGS["theme"] == "light"


def test_schema_settings_theme_is_light():
    assert Settings().theme == "light"
```

- [ ] **Step 2: 运行测试确认失败**

Run: `python -m pytest tests/test_settings_defaults.py -v`
Expected: FAIL（`assert "system" == "light"`）

- [ ] **Step 3: 修改默认值**

`backend/config/defaults.py:10`：
```python
    "theme": "light",            # system / light / dark
```

`shared/schemas.py:474`：
```python
    theme: str = "light"             # system / light / dark
```

- [ ] **Step 4: 运行测试确认通过**

Run: `python -m pytest tests/test_settings_defaults.py -v`
Expected: 2 passed

- [ ] **Step 5: 提交**

```bash
git add backend/config/defaults.py shared/schemas.py tests/test_settings_defaults.py
git commit -m "feat: default theme is light"
```

---

### Task 2: /api/settings 端点

**Files:**
- Create: `backend/api/settings.py`
- Modify: `backend/server.py`（注册路由，`app.include_router(health.router...)` 附近）
- Create: `tests/test_settings_api.py`

**Interfaces:**
- Consumes: `backend.config.manager.get_all_settings()`、`update_settings(partial: dict) -> dict`；`backend.utils.response.success_response`
- Produces: `GET /api/settings` → `{"success":True,"code":0,"message":"OK","data":{"settings":{...}}}`
- Produces: `PUT /api/settings`，body `{"settings": {...部分字段...}}` → 同上信封，data.settings 为合并后完整设置

- [ ] **Step 1: 写失败测试**

创建 `tests/test_settings_api.py`（沿用 `tests/test_api.py` 的 ASGI client 模式；monkeypatch 设置文件到 tmp 避免污染真实配置）：

```python
"""Integration tests for GET/PUT /api/settings (theme persistence)."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

PROJECT_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT_ROOT))

from backend.server import app  # noqa: E402
from backend.config import manager  # noqa: E402


@pytest.fixture(autouse=True)
def isolated_settings(tmp_path, monkeypatch):
    """Point the settings manager at a temp file, bypassing the user's real settings."""
    monkeypatch.setattr(manager, "SETTINGS_FILE", tmp_path / "settings.json")
    monkeypatch.setattr(manager, "_cache", None)
    yield


@pytest_asyncio.fixture
async def client():
    transport = ASGITransport(app=app, raise_app_exceptions=False)
    async with AsyncClient(transport=transport, base_url="http://test/api") as ac:
        yield ac


@pytest.mark.asyncio
async def test_get_settings_returns_light_default(client: AsyncClient):
    r = await client.get("/settings")
    assert r.status_code == 200
    body = r.json()
    assert body["success"] is True
    assert body["data"]["settings"]["theme"] == "light"


@pytest.mark.asyncio
async def test_put_settings_persists_theme(client: AsyncClient):
    r = await client.put("/settings", json={"settings": {"theme": "dark"}})
    assert r.status_code == 200
    body = r.json()
    assert body["data"]["settings"]["theme"] == "dark"

    # persisted to disk — a fresh cache read sees it
    manager._cache = None
    assert manager.get_setting("theme") == "dark"


@pytest.mark.asyncio
async def test_put_settings_unknown_key_ignored(client: AsyncClient):
    r = await client.put("/settings", json={"settings": {"bogusKey": 1}})
    assert r.status_code == 200
    body = r.json()
    assert "bogusKey" not in body["data"]["settings"]
```

- [ ] **Step 2: 运行测试确认失败**

Run: `python -m pytest tests/test_settings_api.py -v`
Expected: FAIL（404 — 路由未注册）

- [ ] **Step 3: 实现端点**

创建 `backend/api/settings.py`：

```python
"""Application-wide settings endpoints (theme/language/…).

Implements GET + PUT ``/api/settings`` backed by the config manager
(``backend.config.manager``), which persists to
``%LOCALAPPDATA%\\WordFormatter\\settings.json``.
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter
from pydantic import BaseModel

from backend.config import manager
from backend.utils.logger import get_logger
from backend.utils.response import success_response

logger = get_logger("backend.api.settings", category="backend")

router = APIRouter(prefix="/settings", tags=["settings"])


class UpdateSettingsRequest(BaseModel):
    """Partial settings update — only supplied keys are overwritten."""

    settings: dict[str, Any]


@router.get("")
async def get_settings() -> dict:
    """Return the full settings dict."""
    return success_response({"settings": manager.get_all_settings()})


@router.put("")
async def update_settings(req: UpdateSettingsRequest) -> dict:
    """Merge the supplied keys into settings and persist."""
    merged = manager.update_settings(req.settings)
    logger.info("Settings updated: keys=%s", list(req.settings.keys()))
    return success_response({"settings": merged})
```

`backend/server.py`：按现有风格（`from backend.api import health` + `app.include_router(health.router, ...)`）修改：

```python
from backend.api import history
from backend.api import preview
from backend.api import settings
```

并在 Router registration 块（`app.include_router(preview.router, ...)` 之后）追加：

```python
app.include_router(settings.router, prefix="/api", tags=["settings"])
```

- [ ] **Step 4: 运行测试确认通过**

Run: `python -m pytest tests/test_settings_api.py -v`
Expected: 3 passed

- [ ] **Step 5: 提交**

```bash
git add backend/api/settings.py backend/server.py tests/test_settings_api.py
git commit -m "feat: /api/settings GET+PUT endpoints for theme persistence"
```

---

### Task 3: DarkTheme.xaml + App.xaml 主题字典注册

**Files:**
- Create: `frontend/Styles/DarkTheme.xaml`
- Modify: `frontend/App.xaml`（ThemeDictionaries 加 Dark 键；移除 `RequestedTheme="Light"`）

**Interfaces:**
- Produces: 主题资源键（Task 5 复用）：`NavBarBackground`、`NavItemSelectedBackground`、`NavItemSelectedForeground`、`NavItemHoverBackground`、`NavItemDefaultIcon`、`NavItemDefaultText`、`FileListBorderBrush`（深色变体）

- [ ] **Step 1: 创建 DarkTheme.xaml**

创建 `frontend/Styles/DarkTheme.xaml`（与 LightTheme.xaml 同键名，深色值）：

```xml
<?xml version="1.0" encoding="utf-8"?>
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- ============================================================
         Dark Theme — Office/WPS professional style
         Accent: #6CB8F6 (lighter blue for dark backgrounds)
         Background: #1F1F1F (WinUI dark layer)
         Text: #F5F5F5 / #C8C8C8 / #9E9E9E
         Border: #555555  |  Divider: #383838
         ============================================================ -->

    <!-- ── Navigation Bar ───────────────────────────────────── -->
    <SolidColorBrush x:Key="NavBarBackground" Color="Transparent" />
    <SolidColorBrush x:Key="NavItemSelectedBackground" Color="#2D2D2D" />
    <SolidColorBrush x:Key="NavItemSelectedForeground" Color="#6CB8F6" />
    <SolidColorBrush x:Key="NavItemHoverBackground" Color="#353535" />
    <SolidColorBrush x:Key="NavItemDefaultIcon" Color="#9E9E9E" />
    <SolidColorBrush x:Key="NavItemDefaultText" Color="#F5F5F5" />

    <!-- ── File List Drop Area ──────────────────────────────── -->
    <SolidColorBrush x:Key="FileListBorderBrush" Color="#555555" />

</ResourceDictionary>
```

- [ ] **Step 2: 修改 App.xaml 注册 Dark 字典并移除硬性浅色**

`frontend/App.xaml`：ThemeDictionaries 段改为：

```xml
            <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Default" Source="Styles/LightTheme.xaml" />
                <ResourceDictionary x:Key="Light" Source="Styles/LightTheme.xaml" />
                <ResourceDictionary x:Key="Dark" Source="Styles/DarkTheme.xaml" />
            </ResourceDictionary.ThemeDictionaries>
```

同时把根元素 `<Application ... RequestedTheme="Light">` 中的 `RequestedTheme="Light"` 属性**删除**（否则 `Application.Current.Resources` 的主题查找恒返回浅色值）。

- [ ] **Step 3: 构建确认无 XAML 错误**

Run: `cd frontend && dotnet build WordFormatterUI.csproj`
Expected: Build succeeded（无 XAML 编译错误）

- [ ] **Step 4: 提交**

```bash
git add frontend/Styles/DarkTheme.xaml frontend/App.xaml
git commit -m "feat: dark theme dictionary + register in App.xaml"
```

---

### Task 4: ThemeService 实现（light/dark/system + 系统监听）

**Files:**
- Modify: `frontend/Services/ThemeService.cs`（整体重写）
- Modify: `frontend/Views/PreviewWindow.xaml.cs`（新增 `public static PreviewWindow? Current => _instance;`，约 48 行 `IsOpen` 附近）

**Interfaces:**
- Consumes: `App.MainWindow`（`Window` 静态属性）
- Produces: `ThemeService.Apply(string mode)`（`light`/`dark`/`system`，幂等，可重复调用）；`ThemeService.CurrentMode`（string，get）

- [ ] **Step 1: 为 PreviewWindow 暴露当前实例**

`frontend/Views/PreviewWindow.xaml.cs`，在 `public static bool IsOpen => _instance is not null;` 后加：

```csharp
    /// <summary>Current PreviewWindow instance, or null if none is open (does not create).</summary>
    public static PreviewWindow? Current => _instance;
```

- [ ] **Step 2: 重写 ThemeService**

整体替换 `frontend/Services/ThemeService.cs`：

```csharp
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace WordFormatterUI.Services;

/// <summary>
/// Local theme management — light / dark / system.
/// "system" follows the OS theme and reacts to live changes via
/// <see cref="UISettings.ColorValuesChanged"/>.
/// </summary>
public static class ThemeService
{
    public static string CurrentMode { get; private set; } = "light";

    private static UISettings? _uiSettings;

    public static void Apply(string mode)
    {
        if (mode is not ("light" or "dark" or "system"))
            mode = "light";
        CurrentMode = mode;

        var theme = mode switch
        {
            "dark" => ElementTheme.Dark,
            "light" => ElementTheme.Light,
            _ => ElementTheme.Default,
        };

        ApplyToWindow(App.MainWindow, theme);
        ApplyToWindow(Views.PreviewWindow.Current, theme);

        EnsureSystemWatcher();
    }

    private static void ApplyToWindow(Window? window, ElementTheme theme)
    {
        try
        {
            if (window?.Content is FrameworkElement root)
                root.RequestedTheme = theme;
        }
        catch
        {
            // COM exceptions may occur during shutdown or rapid startup; safe to ignore.
        }
    }

    private static void EnsureSystemWatcher()
    {
        if (_uiSettings is not null) return;
        _uiSettings = new UISettings();
        _uiSettings.ColorValuesChanged += (_, _) =>
        {
            // ColorValuesChanged fires on a background thread; marshal back to UI.
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                if (CurrentMode == "system")
                    ApplyToWindow(App.MainWindow, ElementTheme.Default);
            });
        };
    }
}
```

- [ ] **Step 3: 构建确认通过**

Run: `cd frontend && dotnet build WordFormatterUI.csproj`
Expected: Build succeeded

- [ ] **Step 4: 提交**

```bash
git add frontend/Services/ThemeService.cs frontend/Views/PreviewWindow.xaml.cs
git commit -m "feat: ThemeService light/dark/system with live OS theme watcher"
```

---

### Task 5: 硬编码颜色迁移到主题字典

**Files:**
- Modify: `frontend/App.xaml`（`AccentButtonStyle`：PointerOver/Pressed/Disabled 颜色改为 `{ThemeResource ...}`）
- Modify: `frontend/Styles/LightTheme.xaml`（新增 accent 按钮相关键）
- Modify: `frontend/Styles/DarkTheme.xaml`（新增 accent 按钮相关键）
- Modify: `frontend/MainWindow.xaml`（3 处局部 AccentButton 禁用色资源 → `{ThemeResource ...}`；删除 CheckBox.Resources 局部覆盖）

**Interfaces:**
- Produces: 新主题资源键（Light+Dark 两字典均定义）：`AccentButtonPointerOverBackground`、`AccentButtonPressedBackground`、`AccentButtonDisabledBackground`、`AccentButtonDisabledForeground`、`CheckBoxCheckBackgroundFillChecked`、`CheckBoxCheckBackgroundFillCheckedPointerOver`、`CheckBoxCheckBackgroundFillCheckedPressed`、`CheckBoxCheckBackgroundStrokeChecked`、`CheckBoxCheckBackgroundStrokeCheckedPointerOver`、`CheckBoxCheckBackgroundStrokeCheckedPressed`、`CheckBoxCheckGlyphForegroundChecked`

- [ ] **Step 1: LightTheme.xaml 追加新键**

`frontend/Styles/LightTheme.xaml` 文件末尾（FileListBorderBrush 后）追加：

```xml

    <!-- ── Accent Button states (used by App.xaml AccentButtonStyle) ── -->
    <SolidColorBrush x:Key="AccentButtonPointerOverBackground" Color="#146CBF" />
    <SolidColorBrush x:Key="AccentButtonPressedBackground" Color="#004A91" />
    <SolidColorBrush x:Key="AccentButtonDisabledBackground" Color="#F3F3F3" />
    <SolidColorBrush x:Key="AccentButtonDisabledForeground" Color="#A19F9D" />

    <!-- ── CheckBox checked states (accent blue family) ───────────── -->
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillChecked" Color="#005FBA" />
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillCheckedPointerOver" Color="#146CBF" />
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillCheckedPressed" Color="#004A91" />
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundStrokeChecked" Color="#005FBA" />
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundStrokeCheckedPointerOver" Color="#146CBF" />
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundStrokeCheckedPressed" Color="#004A91" />
    <SolidColorBrush x:Key="CheckBoxCheckGlyphForegroundChecked" Color="#FFFFFF" />
```

- [ ] **Step 2: DarkTheme.xaml 追加对应深色值**

`frontend/Styles/DarkTheme.xaml` 文件末尾追加：

```xml

    <!-- ── Accent Button states (used by App.xaml AccentButtonStyle) ── -->
    <SolidColorBrush x:Key="AccentButtonPointerOverBackground" Color="#4CC2FF" />
    <SolidColorBrush x:Key="AccentButtonPressedBackground" Color="#3FA9FF" />
    <SolidColorBrush x:Key="AccentButtonDisabledBackground" Color="#333333" />
    <SolidColorBrush x:Key="AccentButtonDisabledForeground" Color="#6D6D6D" />

    <!-- ── CheckBox checked states (accent blue family) ───────────── -->
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillChecked" Color="#2D8CD9" />
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillCheckedPointerOver" Color="#4CC2FF" />
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundFillCheckedPressed" Color="#3FA9FF" />
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundStrokeChecked" Color="#2D8CD9" />
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundStrokeCheckedPointerOver" Color="#4CC2FF" />
    <SolidColorBrush x:Key="CheckBoxCheckBackgroundStrokeCheckedPressed" Color="#3FA9FF" />
    <SolidColorBrush x:Key="CheckBoxCheckGlyphForegroundChecked" Color="#FFFFFF" />
```

- [ ] **Step 3: App.xaml AccentButtonStyle 改用主题资源**

`frontend/App.xaml` 的 `AccentButtonStyle` 模板内三处 Setter 改为：

```xml
                                        <VisualState x:Name="PointerOver">
                                            <VisualState.Setters>
                                                <Setter Target="BackgroundRect.Fill" Value="{ThemeResource AccentButtonPointerOverBackground}" />
                                            </VisualState.Setters>
                                        </VisualState>
                                        <VisualState x:Name="Pressed">
                                            <VisualState.Setters>
                                                <Setter Target="BackgroundRect.Fill" Value="{ThemeResource AccentButtonPressedBackground}" />
                                            </VisualState.Setters>
                                        </VisualState>
                                        <VisualState x:Name="Disabled">
                                            <VisualState.Setters>
                                                <Setter Target="BackgroundRect.Fill" Value="{ThemeResource AccentButtonDisabledBackground}" />
                                                <Setter Target="ContentPresenter.Foreground" Value="{ThemeResource AccentButtonDisabledForeground}" />
                                            </VisualState.Setters>
                                        </VisualState>
```

- [ ] **Step 4: MainWindow.xaml 局部资源改主题资源**

`frontend/MainWindow.xaml`：
1. 删除 3 处 `<Button.Resources><SolidColorBrush x:Key="AccentButtonBackgroundDisabled" .../><SolidColorBrush x:Key="AccentButtonForegroundDisabled" .../></Button.Resources>` 块（SaveBar「保存配置」、全选、反选按钮）
2. 删除 `FileListView` 的 `CheckBox.Resources` 块（7 个 SolidColorBrush 键）
3. 上述按钮的禁用态颜色由主题字典的 `AccentButtonDisabledBackground`/`AccentButtonDisabledForeground` 提供——但 AccentButtonStyle 的 Disabled 态在模板里只设置 `BackgroundRect.Fill` 和 `ContentPresenter.Foreground`，模板绑定的 `Background`/`Foreground` 来自 `AccentButtonStyle` 的 `Background="#005FBA"`/`Foreground="#FFFFFF"`。为让禁用态正确，把 `AccentButtonStyle` 的两个 Setter 改为 `{ThemeResource AccentButtonBackground}` / `{ThemeResource AccentButtonForeground}`，并在两个主题字典各加：

```xml
    <SolidColorBrush x:Key="AccentButtonBackground" Color="#005FBA" />
    <SolidColorBrush x:Key="AccentButtonForeground" Color="#FFFFFF" />
```

（Dark 字典：`AccentButtonBackground` 用 `#2D8CD9`，`AccentButtonForeground` 用 `#FFFFFF`。）

- [ ] **Step 5: 构建确认通过**

Run: `cd frontend && dotnet build WordFormatterUI.csproj`
Expected: Build succeeded

- [ ] **Step 6: 提交**

```bash
git add frontend/App.xaml frontend/Styles/LightTheme.xaml frontend/Styles/DarkTheme.xaml frontend/MainWindow.xaml
git commit -m "feat: move hardcoded accent colors into theme dictionaries"
```

---

### Task 6: ApiService 设置方法 + SettingsDto 默认值

**Files:**
- Modify: `frontend/Services/ApiService.cs`（末尾 Settings 区替换占位注释）
- Modify: `frontend/Models/Settings/SettingsDto.cs`（`Theme` 默认 `"system"` → `"light"`）

**Interfaces:**
- Consumes: `SettingsDto`（已有类）
- Produces: `Task<SettingsDto?> GetSettingsAsync()`（GET /api/settings，失败/网络异常返回 null）；`Task<bool> UpdateSettingsAsync(string theme)`（PUT /api/settings，body `{"settings":{"theme":...}}`）

- [ ] **Step 1: 实现 ApiService 方法**

`frontend/Services/ApiService.cs`，替换末尾占位注释区（约 309-311 行）：

```csharp
    // ═══════════════════════════════════════════════════════════
    //  Settings
    // ═══════════════════════════════════════════════════════════

    /// <summary>GET /api/settings — full settings, or null on failure.</summary>
    public async Task<SettingsDto?> GetSettingsAsync()
    {
        try
        {
            var resp = await GetAsync<SettingsResponseDto>("/api/settings");
            return resp?.Success == true ? resp.Data?.Settings : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>PUT /api/settings — persist theme, returns success.</summary>
    public async Task<bool> UpdateSettingsAsync(string theme)
    {
        try
        {
            var resp = await PutAsync<object>("/api/settings", new { settings = new { theme } });
            return resp?.Success == true;
        }
        catch
        {
            return false;
        }
    }
```

在 `frontend/Models/Settings/SettingsDto.cs` 内新增响应包装类（照抄 `ProfileResponseDto` 风格——无 JsonPropertyName，靠默认 camelCase 序列化把 `Settings` 映射为 `settings`）：

```csharp
public class SettingsResponseDto
{
    public SettingsDto? Settings { get; set; }
}
```

- [ ] **Step 2: SettingsDto 默认值改 light**

`frontend/Models/Settings/SettingsDto.cs:9`：
```csharp
    public string Theme { get; set; } = "light";
```

- [ ] **Step 3: 构建确认通过**

Run: `cd frontend && dotnet build WordFormatterUI.csproj`
Expected: Build succeeded

- [ ] **Step 4: 提交**

```bash
git add frontend/Services/ApiService.cs frontend/Models/Settings/SettingsDto.cs
git commit -m "feat: ApiService settings GET/PUT + light default"
```

---

### Task 7: SettingsViewModel 真实实现

**Files:**
- Modify: `frontend/ViewModels/SettingsViewModel.cs`（整体重写）

**Interfaces:**
- Consumes: `ApiService.GetSettingsAsync()`、`UpdateSettingsAsync(theme)`；`ThemeService.Apply(mode)`
- Produces: `SettingsViewModel.InitializeAsync()`（从后端加载，设置 `Theme` 但不标 dirty）；`SettingsViewModel.Theme`（string，set 时立即 Apply + fire-and-forget PUT 持久化）

- [ ] **Step 1: 重写 SettingsViewModel**

整体替换 `frontend/ViewModels/SettingsViewModel.cs`：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using WordFormatterUI.Services;

namespace WordFormatterUI.ViewModels;

/// <summary>
/// Global settings ViewModel (design-document §17).
/// Theme changes apply immediately (ThemeService) and persist to the
/// backend settings.json (fire-and-forget PUT).
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ApiService _api;

    public SettingsViewModel() : this(App.Api) { }

    public SettingsViewModel(ApiService api)
    {
        _api = api;
    }

    [ObservableProperty] private string _language = "zh-CN";
    [ObservableProperty] private bool _autoCheckUpdate = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Theme))]
    private string _theme = "light";

    partial void OnThemeChanged(string value)
    {
        // Apply immediately; persist without blocking the UI thread.
        ThemeService.Apply(value);
        _ = PersistThemeAsync(value);
    }

    private async Task PersistThemeAsync(string theme)
    {
        try
        {
            await _api.UpdateSettingsAsync(theme);
        }
        catch
        {
            // Persistence failure is non-fatal — theme stays applied in-session.
        }
    }

    /// <summary>Load settings from the backend once (startup). Failure keeps defaults.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var settings = await _api.GetSettingsAsync();
            if (settings is null) return;
            if (settings.Theme is "light" or "dark" or "system")
            {
                _theme = settings.Theme;          // bypass OnThemeChanged: no Apply/PUT on load
                OnPropertyChanged(nameof(Theme));
                ThemeService.Apply(settings.Theme);
            }
        }
        catch
        {
            // Keep default light.
        }
    }
}
```

- [ ] **Step 2: 构建确认通过**

Run: `cd frontend && dotnet build WordFormatterUI.csproj`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add frontend/ViewModels/SettingsViewModel.cs
git commit -m "feat: SettingsViewModel loads/persists theme via /api/settings"
```

---

### Task 8: AdvancedSettingsView 下拉接线 + 启动流程 + 删除 SettingsPage

**Files:**
- Modify: `frontend/Views/AdvancedSettingsView.xaml`（ThemeBox 补两项）
- Modify: `frontend/Views/AdvancedSettingsView.xaml.cs`（按保存值回显、SelectionChanged 接线）
- Modify: `frontend/ViewModels/MainViewModel.cs`（`InitializeAsync` 中加载设置；构造函数注入 ApiService 到 SettingsViewModel）
- Delete: `frontend/Pages/SettingsPage.xaml` + `frontend/Pages/SettingsPage.xaml.cs`（死代码）

**Interfaces:**
- Consumes: `SettingsViewModel`（MainViewModel.SettingsVm）
- Produces: 高级设置页主题下拉三项可用；启动时应用已保存主题

- [ ] **Step 1: AdvancedSettingsView.xaml 补全下拉项**

`frontend/Views/AdvancedSettingsView.xaml` 的 ThemeBox 改为：

```xml
                <ComboBox
                    Grid.Column="1"
                    x:Name="ThemeBox"
                    Height="32"
                    HorizontalAlignment="Stretch"
                    SelectionChanged="ThemeBox_SelectionChanged">
                    <ComboBoxItem Tag="light">浅色</ComboBoxItem>
                    <ComboBoxItem Tag="dark">深色</ComboBoxItem>
                    <ComboBoxItem Tag="system">跟随系统</ComboBoxItem>
                </ComboBox>
```

（删除原 `SelectedIndex="0"` 硬编码；`LanguageBox` 保持原样。）

- [ ] **Step 2: AdvancedSettingsView.xaml.cs 接线**

整体替换 `frontend/Views/AdvancedSettingsView.xaml.cs`：

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordFormatterUI.Services;
using WordFormatterUI.ViewModels;

namespace WordFormatterUI.Views;

/// <summary>
/// Advanced application settings (plan §7.7).
/// Language, theme (via <see cref="ThemeService"/>), and auto-check-update.
/// Theme is persisted through <see cref="SettingsViewModel"/>.
/// </summary>
public sealed partial class AdvancedSettingsView : UserControl
{
    // Guard against firing change handlers while pushing initial state
    private bool _isLoading;

    /// <summary>Injected by MainWindow; drives theme persistence.</summary>
    public SettingsViewModel? SettingsVm { get; set; }

    public AdvancedSettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;

        // Language (default zh-CN — no persistence yet)
        LanguageBox.SelectedIndex = 0;

        // Theme — select the item matching the current mode
        var mode = ThemeService.CurrentMode;
        for (int i = 0; i < ThemeBox.Items.Count; i++)
        {
            if (ThemeBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == mode)
            {
                ThemeBox.SelectedIndex = i;
                break;
            }
        }

        _isLoading = false;
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        // No-op until backend /api/settings + i18n resource swapping is available.
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (ThemeBox.SelectedItem is ComboBoxItem item && item.Tag is string mode)
        {
            if (SettingsVm is not null)
                SettingsVm.Theme = mode;
            else
                ThemeService.Apply(mode);   // fallback — apply directly
        }
    }
}
```

- [ ] **Step 3: MainViewModel 接线**

`frontend/ViewModels/MainViewModel.cs` 构造函数中：
```csharp
        // Utility VMs
        HistoryVm = new HistoryViewModel(api);
        SettingsVm = new SettingsViewModel(api);   // 原为 new SettingsViewModel()
```

`InitializeAsync()` 内（`await App.WaitForBackendAsync();` 之后）加：
```csharp
        // Load persisted settings (theme) from backend and apply
        await SettingsVm.InitializeAsync();
```

- [ ] **Step 4: MainWindow 注入 SettingsVm 并删除 SettingsPage**

`frontend/MainWindow.xaml.cs` 构造函数（`ThemeService.Apply("light")` 之后、`_sectionPanels` 赋值之前）加：
```csharp
        AdvancedSettingsView.SettingsVm = ViewModel.SettingsVm;
```
（确保 `ViewModel` 创建在注入之前——现有代码顺序：Apply → new ViewModel → DataContext；将注入行放在 ViewModel 创建之后。）

删除文件：
```bash
git rm frontend/Pages/SettingsPage.xaml frontend/Pages/SettingsPage.xaml.cs
```

- [ ] **Step 5: 构建确认通过**

Run: `cd frontend && dotnet build WordFormatterUI.csproj`
Expected: Build succeeded（且 SettingsPage 不再存在）

- [ ] **Step 6: 提交**

```bash
git add frontend/Views/AdvancedSettingsView.xaml frontend/Views/AdvancedSettingsView.xaml.cs frontend/ViewModels/MainViewModel.cs frontend/MainWindow.xaml.cs
git commit -m "feat: wire theme dropdown in advanced settings + load persisted theme at startup; remove dead SettingsPage"
```

---

### Task 9: 全量验证

**Files:**
- 无（验证任务）

- [ ] **Step 1: 后端测试全量通过**

Run: `python -m pytest tests/test_settings_defaults.py tests/test_settings_api.py -v`
Expected: 5 passed

再跑既有回归：`python -m pytest tests/test_api.py -q`
Expected: 全部通过（或记录与本任务无关的既有失败项）

- [ ] **Step 2: 前端构建通过**

Run: `cd frontend && dotnet build WordFormatterUI.csproj`
Expected: Build succeeded

- [ ] **Step 3: 手动功能验证（run.bat 启动）**

1. 启动后主题为浅色（默认）
2. 高级设置 → 主题设置下拉 → 选「深色」→ 界面立即变深色（导航栏/卡片/输入框/按钮/复选框/状态栏）
3. 选「浅色」→ 立即变回
4. 选「跟随系统」→ 在 Windows 设置中切换 深色/浅色 → 应用实时跟随
5. 重启应用 → 上次选择保留
6. 预览窗口打开时切换主题 → 预览窗口同步变色
7. 后端未启动时启动应用 → 保持浅色，无崩溃

- [ ] **Step 4: 收尾提交（如有修正）**

```bash
git add -A -- frontend backend shared tests docs
git commit -m "chore: theme feature verification fixes"
```
（如无修正则跳过）
