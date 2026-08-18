using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WordFormatterUI.Models.Common;
using WordFormatterUI.Models.Files;
using WordFormatterUI.Services;

namespace WordFormatterUI.ViewModels;

/// <summary>
/// File-management view model (design-document §7.1).
///
/// Holds the working file list as rich <see cref="FileItemDto"/> objects
/// (name / path / size), the current search keyword, and the recent-open
/// records. Selection state lives in the View (ListView.SelectedItems).
/// </summary>
public partial class FilesViewModel : ObservableObject
{
    private readonly ApiService _api;

    public FilesViewModel(ApiService api)
    {
        _api = api;
    }

    /// <summary>Files currently shown (may be filtered by <see cref="SearchKeyword"/>).</summary>
    [ObservableProperty]
    private ObservableCollection<FileItemDto> _files = new();

    /// <summary>Total files in the backend queue (unfiltered count).</summary>
    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasFiles;

    /// <summary>Live search keyword (empty = show all).</summary>
    [ObservableProperty]
    private string _searchKeyword = "";

    /// <summary>Recent-open records (files & folders), most recent first.</summary>
    [ObservableProperty]
    private ObservableCollection<RecentRecordDto> _recent = new();

    // ── Load ─────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadFilesAsync()
    {
        IsLoading = true;
        try
        {
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Add files ────────────────────────────────────────────────────

    [RelayCommand]
    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var list = (paths ?? Enumerable.Empty<string>()).ToList();
        if (list.Count == 0)
        {
            StatusMessage = "未选择文件";
            return;
        }

        // 前端预校验：立即提示不存在的路径，而不是等后端整批拒绝。
        // 这解决了「添加文档后没反应」——失败信息现在会显示在状态栏。
        var missing = list.Where(p => !System.IO.File.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            var sample = System.IO.Path.GetFileName(missing[0]);
            StatusMessage = missing.Count == 1
                ? $"文件不存在或无法访问：{sample}"
                : $"有 {missing.Count} 个文件不存在或无法访问（如：{sample}）";
            // 仍把有效文件发给后端，避免全部丢失
            list = list.Where(p => System.IO.File.Exists(p)).ToList();
            if (list.Count == 0) return;
        }

        IsLoading = true;
        try
        {
            var resp = await _api.AddFilesAsync(list);
            if (resp?.Success == true)
            {
                await ReloadAsync();
                var count = resp.Data?.Count ?? 0;
                StatusMessage = count > 0
                    ? $"已添加 {count} 个文件"
                    : "未添加新文件（可能已存在或格式不支持）";
            }
            else
            {
                StatusMessage = $"添加失败: {resp?.Message ?? "未知错误"}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Add folder ───────────────────────────────────────────────────

    [RelayCommand]
    public async Task AddFolderAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder))
        {
            StatusMessage = "文件夹不存在或无法访问";
            return;
        }

        IsLoading = true;
        try
        {
            var resp = await _api.AddFolderAsync(folder);
            if (resp?.Success == true)
            {
                await ReloadAsync();
                var count = resp.Data?.Count ?? 0;
                StatusMessage = count > 0
                    ? $"从文件夹添加了 {count} 个文件"
                    : "文件夹中未找到新文件";
            }
            else
            {
                StatusMessage = $"添加失败: {resp?.Message ?? "未知错误"}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Remove selected ──────────────────────────────────────────────

    [RelayCommand]
    public async Task RemoveSelectedAsync(IEnumerable<string> selectedPaths)
    {
        var paths = selectedPaths.ToList();
        if (paths.Count == 0) return;

        IsLoading = true;
        try
        {
            var resp = await _api.RemoveFilesAsync(paths);
            if (resp?.Success == true)
            {
                await ReloadAsync();
                StatusMessage = $"已移除 {paths.Count} 个文件";
            }
            else
            {
                StatusMessage = $"移除失败: {resp?.Message ?? "未知错误"}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"移除失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Clear all ────────────────────────────────────────────────────

    [RelayCommand]
    public async Task ClearAllAsync()
    {
        IsLoading = true;
        try
        {
            var ok = await _api.ClearFilesAsync();
            if (ok)
            {
                Files.Clear();
                FileCount = 0;
                HasFiles = false;
                SearchKeyword = "";
                StatusMessage = "已清空所有文件";
            }
            else
            {
                StatusMessage = "清空失败";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"清空失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Search ───────────────────────────────────────────────────────

    /// <summary>
    /// Filter the file list by <see cref="SearchKeyword"/> via the backend
    /// (case-insensitive name/path match). Empty keyword returns all files.
    /// </summary>
    [RelayCommand]
    public async Task SearchAsync()
    {
        try
        {
            var resp = await _api.SearchFilesAsync(SearchKeyword ?? "");
            if (resp?.Success == true && resp.Data is not null)
            {
                Files = new ObservableCollection<FileItemDto>(resp.Data.Files);
                // Note: FileCount reflects the total queue, not the filtered view;
                // it is refreshed on the next full reload.
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"搜索失败: {ex.Message}";
        }
    }

    // ── Recent ───────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadRecentAsync()
    {
        try
        {
            var resp = await _api.GetRecentFilesAsync();
            if (resp?.Success == true && resp.Data is not null)
                Recent = new ObservableCollection<RecentRecordDto>(resp.Data.Recent);
        }
        catch
        {
            // Non-critical — leave Recent as-is
        }
    }

    // ── Internal helper ──────────────────────────────────────────────

    /// <summary>Reload the full file list from backend and refresh counts.</summary>
    private async Task ReloadAsync()
    {
        var resp = await _api.GetFilesAsync();
        if (resp?.Success == true && resp.Data is not null)
        {
            Files = new ObservableCollection<FileItemDto>(resp.Data.Files);
            FileCount = Files.Count;
            HasFiles = FileCount > 0;
        }
    }
}
