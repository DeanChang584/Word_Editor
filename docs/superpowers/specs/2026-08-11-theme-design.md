# 主题模式（浅色/深色/跟随系统）设计

> 日期：2026-08-11 | 状态：已批准 | 方案：A（窗口级 `ElementTheme` + 主题字典 + 后端 settings 持久化）

## 需求

1. 支持三种主题模式，下拉选择：浅色 / 深色 / 跟随系统
2. 默认浅色
3. 选择经后端 `settings.json` 持久化，重启后保留
4. 「跟随系统」模式下，系统主题实时切换时应用即时跟随

## 现状

- `ThemeService.cs` 是桩实现：`CurrentMode => "light"`、`Apply()` 强制 `ElementTheme.Light`（注释：主题切换曾被移除）
- `App.xaml` 设 `RequestedTheme="Light"`，ThemeDictionaries 仅 `Default`/`Light` 两键，均指向 `Styles/LightTheme.xaml`
- `Styles/LightTheme.xaml`：7 个自定义画刷（NavBar 相关 6 个 + FileListBorderBrush）
- 视图均使用 WinUI 自适应系统资源（`CardBackgroundFillColorDefaultBrush` 等），仅 3 个文件有硬编码颜色：
  - `MainWindow.xaml`（13 处：accent 蓝、禁用按钮色、CheckBox 选中色）
  - `App.xaml` 内 `AccentButtonStyle`（hover/pressed/disabled 硬编码）
  - `NumericTextBox.xaml`（`SelectionHighlightColor="#005FBA"`，accent 色两主题通用，不改）
- `AdvancedSettingsView.xaml` 已有 `ThemeBox` ComboBox（仅「浅色」一项，handler 为占位）
- `SettingsPage.xaml`（RadioButtons system/light/dark）为无人引用的死代码
- 后端 `DEFAULT_SETTINGS["theme"] = "system"`、`shared/schemas.py Settings.theme = "system"`；**无** `/api/settings` 端点
- `SettingsViewModel` 为占位（`SaveAsync` no-op，未绑定任何 UI）
- `PreviewWindow` 为单例（`GetOrCreate()`），需一并应用主题

## 方案 A：窗口级 ElementTheme + 主题字典

### 1. 后端：设置接口与默认值

- `backend/config/defaults.py`：`"theme": "system"` → `"light"`（注释同步）
- `shared/schemas.py`：`Settings.theme` 默认 `"system"` → `"light"`
- 新增 `backend/api/settings.py`：
  - `GET /api/settings` → `success_response({"settings": get_all_settings()})`
  - `PUT /api/settings` → body `{"settings": {...部分字段...}}` → `update_settings(partial)` → 返回合并后完整设置
- `backend/server.py`：`app.include_router(settings.router, prefix="/api", tags=["settings"])`

注意：已存在 `settings.json` 的老用户可能存有 `"theme": "system"`，升级后即表现为「跟随系统」——行为合理，不强制迁移。

### 2. 前端主题资源

- 新增 `frontend/Styles/DarkTheme.xaml`，7 个画刷深色变体（建议值）：
  - `NavBarBackground`: Transparent（不变）
  - `NavItemSelectedBackground`: #2D2D2D
  - `NavItemSelectedForeground`: #6CB8F6（深色下更亮的 accent 蓝）
  - `NavItemHoverBackground`: #353535
  - `NavItemDefaultIcon`: #9E9E9E
  - `NavItemDefaultText`: #F5F5F5
  - `FileListBorderBrush`: #555555
- `App.xaml`：
  - ThemeDictionaries 注册 `Default`/`Light` → LightTheme.xaml，`Dark` → DarkTheme.xaml
  - **移除** `RequestedTheme="Light"`（否则 `Application.Current.Resources` 主题查找恒返回浅色值，深色模式下 SettingsPage/代码查资源会错）
- 硬编码颜色迁移：
  - `App.xaml` `AccentButtonStyle`：PointerOver/Pressed/Disabled 颜色改为主题资源键（新增 `AccentButtonPointerOverBackground`、`AccentButtonPressedBackground`、`AccentButtonDisabledBackground`、`AccentButtonDisabledForeground`，Light/Dark 两字典各给值）
  - `MainWindow.xaml`：3 处局部 `AccentButtonBackgroundDisabled`/`AccentButtonForegroundDisabled` 资源改为 `{ThemeResource ...}`；7 个 CheckBox 选中色键 → 移入主题字典（同名键，Dark 给适配值），删除 CheckBox.Resources 局部覆盖
  - `NumericTextBox.xaml`：不改（accent 蓝两主题通用）

### 3. ThemeService 实现

```csharp
public static class ThemeService
{
    public static string CurrentMode { get; private set; } = "light";
    private static UISettings _uiSettings;   // Windows.UI.ViewManagement

    public static void Apply(string mode);   // light/dark/system → ElementTheme.Light/Dark/Default
                                             // 应用到 MainWindow + PreviewWindow 根元素
    private static void RegisterSystemWatcher(); // UISettings.ColorValuesChanged → DispatcherQueue
                                                 // → CurrentMode=="system" 时重新应用 Default
}
```

- `Apply` 对每个窗口：`w.Content is FrameworkElement fe → fe.RequestedTheme = theme`
- 窗口集合：`App.MainWindow` + `PreviewWindow.IsOpen ? PreviewWindow.GetOrCreate() : null`（仅应用不创建）
- 系统监听常驻注册（首次 `Apply` 时惰性初始化）；`ColorValuesChanged` 任意线程触发，回 UI 线程处理

### 4. 前端设置加载与 UI

- `ApiService`：新增 `GetSettingsAsync()`（GET /api/settings → `ApiResponse<SettingsDto>`）、`UpdateSettingsAsync(theme)`（PUT）
- `SettingsViewModel`：
  - `InitializeAsync()`：从后端加载设置 → 设置 `_theme`（不标 dirty）
  - `OnThemeChanged`：`IsDirty = true` + 立即 `ThemeService.Apply(value)` + fire-and-forget PUT 持久化（主题即时生效，无需保存按钮）
  - 删除不再使用的 `SaveAsync` 占位（无 UI 调用）
- `AdvancedSettingsView`：
  - `ThemeBox` 补两项：`<ComboBoxItem Tag="dark">深色</ComboBoxItem>`、`<ComboBoxItem Tag="system">跟随系统</ComboBoxItem>`（顺序：浅色/深色/跟随系统）
  - `OnLoaded`：按保存值回显 SelectedIndex（Tag 匹配，而非硬编码 0）
  - `ThemeBox_SelectionChanged`：非 loading 时 `SettingsVm.SetTheme(tag)` 或直接属性赋值
  - 注入 SettingsViewModel：新增公共属性，MainWindow 构造时赋值（现有视图均为代码后模式，保持一致）
- **删除** `frontend/Pages/SettingsPage.xaml` + `.xaml.cs`（死代码；其 `Application.Current.Resources` 主题查找在移除 App RequestedTheme 后也不再需要特殊处理）
- `MainWindow.xaml.cs`：`ThemeService.Apply("light")` 保留为启动默认；App 启动后异步初始化 SettingsViewModel

### 5. 启动流程

- `MainWindow` 构造：同步 `Apply("light")`（默认浅色，无闪变风险）
- `App.OnLaunched` 或 MainWindow：fire-and-forget `SettingsVm.InitializeAsync()` → 成功后 `ThemeService.Apply(settings.Theme)`；失败保持浅色
- 后端未就绪时沿用 `App.WaitForBackendAsync()` 机制或直接失败静默（保持浅色）

### 6. 测试

- 后端：curl GET/PUT `/api/settings`；默认 `theme=light`；PUT 后 settings.json 更新；重启保留
- 前端：编译通过；三项切换即时生效；跟随系统模式下切换 Windows 主题应用实时跟随；重启记住选择
- 深色视觉检查：导航栏、卡片、输入框、按钮（含禁用态）、复选框、状态栏、预览窗口均正常

## 范围外（YAGNI）

- 不新增设置「重置」端点
- 不做 Mica/Acrylic 背景
- 不迁移老用户已有 `"system"` 值
