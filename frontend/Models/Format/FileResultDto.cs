namespace WordFormatterUI.Models.Format;

public class FileResultDto
{
    public string File { get; set; } = "";
    public string Status { get; set; } = "";  // success / error / skipped
    public string Output { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string Message { get; set; } = "";
    /// <summary>完整源路径 —— 仅 failedFiles 列表携带，供失败重试使用。</summary>
    public string Path { get; set; } = "";
}