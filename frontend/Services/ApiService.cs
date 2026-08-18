using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WordFormatterUI.Models;
using WordFormatterUI.Models.Files;
using WordFormatterUI.Models.Format;
using WordFormatterUI.Models.History;
using WordFormatterUI.Models.Preview;
using WordFormatterUI.Models.Profile;
using WordFormatterUI.Models.Settings;
using WordFormatterUI.Models.Templates;

namespace WordFormatterUI.Services;

/// <summary>
/// HTTP client wrapper for the WordFormatter backend API.
/// Base address: http://127.0.0.1:8765/api.
/// All JSON uses camelCase naming (aligned with Q1 decision and
/// shared/schemas.py Pydantic alias_generator).
///
/// Responsibilities: HTTP transport + JSON deserialization only.
/// DTO definitions live in WordFormatterUI.Models.*.
/// </summary>
public sealed class ApiService
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ApiService(string baseUrl = "http://127.0.0.1:8765")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
    }

    // ───────────────────────────────────────────────────────────
    //  Low-level helpers
    // ───────────────────────────────────────────────────────────

    private async Task<ApiResponse<T>?> GetAsync<T>(string path)
    {
        var resp = await _http.GetAsync(path);
        return await resp.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOpts);
    }

    private async Task<ApiResponse<T>?> PostAsync<T>(string path, object body)
    {
        var resp = await _http.PostAsJsonAsync(path, body, JsonOpts);
        return await resp.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOpts);
    }

    private async Task<ApiResponse<T>?> PostAsync<T>(string path)
    {
        var resp = await _http.PostAsync(path, null);
        return await resp.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOpts);
    }

    private async Task<ApiResponse<T>?> PutAsync<T>(string path, object body)
    {
        var resp = await _http.PutAsJsonAsync(path, body, JsonOpts);
        return await resp.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOpts);
    }

    private async Task<ApiResponse<T>?> DeleteAsync<T>(string path)
    {
        var resp = await _http.DeleteAsync(path);
        return await resp.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOpts);
    }

    // ═══════════════════════════════════════════════════════════
    //  Health
    // ═══════════════════════════════════════════════════════════

    /// <summary>GET /api/health — returns true if backend is reachable.</summary>
    public async Task<bool> HealthAsync()
    {
        try
        {
            var resp = await _http.GetAsync("/api/health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ═══════════════════════════════════════════════════════════
    //  File Management
    // ═══════════════════════════════════════════════════════════

    // 6.1 — GET /api/files → { files: [...] }
    public Task<ApiResponse<FileListDto>?> GetFilesAsync()
        => GetAsync<FileListDto>("/api/files");

    // 6.2 — POST /api/files/add → { count: N }
    public Task<ApiResponse<AddCountDto>?> AddFilesAsync(IEnumerable<string> paths)
        => PostAsync<AddCountDto>("/api/files/add", new { paths });

    // 6.3 — POST /api/files/add-folder → { count: N }
    public Task<ApiResponse<AddCountDto>?> AddFolderAsync(string folder, bool includeSubdir = true)
        => PostAsync<AddCountDto>("/api/files/add-folder", new { folder, includeSubdir });

    // 6.4 — POST /api/files/remove → { removed_count: N }
    public Task<ApiResponse<AddCountDto>?> RemoveFilesAsync(IEnumerable<string> paths)
        => PostAsync<AddCountDto>("/api/files/remove", new { paths });

    // 6.5 — DELETE /api/files → no data
    public async Task<bool> ClearFilesAsync()
    {
        var resp = await _http.DeleteAsync("/api/files");
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOpts);
        return body?.Success == true;
    }

    // 6.6 — POST /api/files/search → { files: [...] }
    public Task<ApiResponse<FileListDto>?> SearchFilesAsync(string keyword)
        => PostAsync<FileListDto>("/api/files/search", new { keyword });

    // 6.7 — GET /api/files/recent → { recent: [...] }
    public Task<ApiResponse<RecentListDto>?> GetRecentFilesAsync()
        => GetAsync<RecentListDto>("/api/files/recent");

    // 6.8 — POST /api/files/pin → { pinned: [...] }
    // TODO: Add PinnedListDto when needed.
    // public Task<ApiResponse<...>?> PinFolderAsync(string folder)
    //     => PostAsync<...>("/api/files/pin", new { folder });

    // ═══════════════════════════════════════════════════════════
    //  Profile
    // ═══════════════════════════════════════════════════════════

    // 7.1 — GET /api/profile → { profile: { ... } }
    public async Task<ProfileConfigDto?> GetProfileAsync()
    {
        var resp = await GetAsync<ProfileResponseDto>("/api/profile");
        return resp?.Success == true ? resp.Data?.Profile : null;
    }

    // 7.2 — PUT /api/profile
    public async Task<bool> UpdateProfileAsync(ProfileConfigDto profileData)
    {
        var resp = await PutAsync<object>("/api/profile", new { profile = profileData });
        return resp?.Success == true;
    }

    // 7.3 — POST /api/profile/reset
    public async Task<bool> ResetProfileAsync()
    {
        var resp = await PostAsync<object>("/api/profile/reset");
        return resp?.Success == true;
    }

    // ═══════════════════════════════════════════════════════════
    //  Templates
    // ═══════════════════════════════════════════════════════════

    // 8.1 — GET /api/templates → { templates: [...] }
    public Task<ApiResponse<TemplateListDto>?> GetTemplatesAsync()
        => GetAsync<TemplateListDto>("/api/templates");

    // 8.2 — POST /api/templates
    public async Task<TemplateDto?> SaveTemplateAsync(string name, ProfileConfigDto? profile = null)
    {
        var resp = await PostAsync<TemplateDto>("/api/templates", new { name, profile });
        return resp?.Success == true ? resp.Data : null;
    }

    // 8.3 — PUT /api/templates/{id}
    public async Task<bool> UpdateTemplateAsync(string templateId, string? name = null, ProfileConfigDto? profile = null)
    {
        var resp = await PutAsync<object>($"/api/templates/{templateId}", new { name, profile });
        return resp?.Success == true;
    }

    // 8.4 — DELETE /api/templates/{id}
    public async Task<bool> DeleteTemplateAsync(string templateId)
    {
        var resp = await DeleteAsync<object>($"/api/templates/{templateId}");
        return resp?.Success == true;
    }

    // 8.5 — POST /api/templates/import
    public async Task<TemplateDto?> ImportTemplateAsync(string path)
    {
        var resp = await PostAsync<TemplateDto>("/api/templates/import", new { path });
        return resp?.Success == true ? resp.Data : null;
    }

    // 8.6 — POST /api/templates/export → { exportedFile: "..." }
    public async Task<string?> ExportTemplateAsync(string templateId, string targetPath)
    {
        var resp = await PostAsync<ExportResultDto>("/api/templates/export", new { templateId, targetPath });
        return resp?.Success == true ? resp.Data?.ExportedFile : null;
    }

    // 8.7 — POST /api/templates/default
    public async Task<bool> SetDefaultTemplateAsync(string templateId)
    {
        var resp = await PostAsync<object>("/api/templates/default", new { templateId });
        return resp?.Success == true;
    }

    // ═══════════════════════════════════════════════════════════
    //  Format Tasks
    // ═══════════════════════════════════════════════════════════

    // 9.1 — POST /api/format/start → { taskId: "..." }  (HTTP 202)
    public async Task<FormatStartResultDto?> StartFormatAsync(FormatStartRequestDto request)
    {
        var resp = await _http.PostAsJsonAsync("/api/format/start", request, JsonOpts);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<FormatStartResultDto>>(JsonOpts);
        return body?.Success == true ? body.Data : null;
    }

    // 9.2 — GET /api/format/status/{taskId}
    public Task<ApiResponse<TaskStatusDto>?> GetFormatStatusAsync(string taskId)
        => GetAsync<TaskStatusDto>($"/api/format/status/{taskId}");

    // 9.3 — POST /api/format/cancel
    public async Task<bool> CancelFormatAsync(string taskId)
    {
        var resp = await PostAsync<object>("/api/format/cancel", new { taskId });
        return resp?.Success == true;
    }

    // 9.4 — GET /api/format/result/{taskId}
    public Task<ApiResponse<TaskResultDto>?> GetFormatResultAsync(string taskId)
        => GetAsync<TaskResultDto>($"/api/format/result/{taskId}");

    // ═══════════════════════════════════════════════════════════
    //  Preview
    // ═══════════════════════════════════════════════════════════

    // 10.1 — POST /api/preview → { preview: "..." }
    public async Task<string?> GetPreviewAsync(string? file = null, object? profile = null)
    {
        var resp = await _http.PostAsJsonAsync("/api/preview",
            new { file = file ?? "", profile = profile ?? "default" }, JsonOpts);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<PreviewDataDto>>(JsonOpts);
        return body?.Success == true ? body.Data?.Preview : null;
    }

    //  Level 2 — POST /api/preview/pdf → taskId
    public async Task<(PdfPreviewStartDto? Data, string? Error)> StartPdfPreviewAsync(string file, object profile)
    {
        var resp = await _http.PostAsJsonAsync("/api/preview/pdf",
            new { file, profile }, JsonOpts);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<PdfPreviewStartDto>>(JsonOpts);
        if (body?.Success == true)
            return (body.Data, null);
        return (null, body?.Message ?? "无法连接格式化服务。");
    }

    //  Level 2 — GET /api/preview/pdf/{taskId} → { state, pdfPath, error }
    public async Task<PdfPreviewStatusDto?> GetPdfPreviewStatusAsync(string taskId)
    {
        var resp = await _http.GetAsync($"/api/preview/pdf/{taskId}");
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<PdfPreviewStatusDto>>(JsonOpts);
        return body?.Success == true ? body.Data : null;
    }

    //  Level 2 — POST /api/preview/pdf/{taskId}/cancel
    public async Task<bool> CancelPdfPreviewAsync(string taskId)
    {
        var resp = await _http.PostAsync($"/api/preview/pdf/{taskId}/cancel", null);
        return resp.IsSuccessStatusCode;
    }

    // ═══════════════════════════════════════════════════════════
    //  History
    // ═══════════════════════════════════════════════════════════

    // 11.1 — GET /api/history → { history: [...] }
    public Task<ApiResponse<HistoryListDto>?> GetHistoryAsync()
        => GetAsync<HistoryListDto>("/api/history");

    // 11.2 — GET /api/history/{id} → full record detail
    public Task<ApiResponse<HistoryRecordDto>?> GetHistoryDetailAsync(string recordId)
        => GetAsync<HistoryRecordDto>($"/api/history/{recordId}");

    // 11.3 — DELETE /api/history → clear all
    public async Task<bool> ClearHistoryAsync()
    {
        var resp = await DeleteAsync<object>("/api/history");
        return resp?.Success == true;
    }

    // ═══════════════════════════════════════════════════════════
    //  Shutdown — notify backend to exit gracefully
    // ═══════════════════════════════════════════════════════════

    /// <summary>POST /api/shutdown — ask the backend to exit.</summary>
    public async Task ShutdownBackendAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            // ConfigureAwait(false)：此调用常从 UI 线程的 Window.Closed 处理器触发，
            // 且调用方用 .Wait() 同步等待。若保守捕获 DispatcherQueue 上下文，continuation
            // 会排队到被阻塞的 UI 线程 → 死锁直到超时 (sync-over-async)，窗口关闭被拖慢数百 ms。
            // 指定 false 让续体在线程池运行，HTTP 完成后 Task 立即完成，Wait 快速返回。
            var resp = await _http.PostAsync("/api/shutdown", null, cts.Token).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
                System.Diagnostics.Debug.WriteLine("Shutdown request sent to backend");
        }
        catch (Exception ex)
        {
            // The backend may close before responding — that's okay.
            System.Diagnostics.Debug.WriteLine($"Shutdown exception (ignored): {ex.Message}");
        }
    }

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
}
