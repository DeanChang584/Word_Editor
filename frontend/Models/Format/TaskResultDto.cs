using System.Text.Json.Serialization;

namespace WordFormatterUI.Models.Format;

public class TaskResultDto
{
    [JsonPropertyName("success")]
    public int Ok { get; set; }
    [JsonPropertyName("failed")]
    public int Fail { get; set; }
    public int Skipped { get; set; }
    public double Elapsed { get; set; }
    // 后端 schema 该字段 alias 为 outputDirectory（非 camelCase 推导的 outputDir）
    [JsonPropertyName("outputDirectory")]
    public string OutputDir { get; set; } = "";
    public List<FileResultDto> Results { get; set; } = new();
    [JsonPropertyName("failedFiles")]
    public List<FileResultDto> FailedFiles { get; set; } = new();
}
