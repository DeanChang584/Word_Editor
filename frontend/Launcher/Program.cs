using System.Diagnostics;
using System.Net.Http;

var appDir = AppContext.BaseDirectory;
var backendPath = Path.Combine(appDir, "backend.exe");
var frontendPath = Path.Combine(appDir, "frontend", "WordFormatterUI.exe");
var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "WordFormatter");
Directory.CreateDirectory(logDir);
var logPath = Path.Combine(logDir, "launcher.log");

var sw = new StringWriter();
void Log(string msg) {
    var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
    sw.WriteLine(line);
    try { File.AppendAllText(logPath, line + "\n"); } catch { }
}

Log($"Starting Word Formatter v2.1 from {appDir}");

// 1. Kill any leftover backend
foreach (var proc in Process.GetProcessesByName("backend"))
{
    try { proc.Kill(); proc.WaitForExit(3000); Log($"Killed stale backend PID {proc.Id}"); }
    catch { }
}
Thread.Sleep(1000);

// 2. Start backend
Process? backendProc = null;
if (File.Exists(backendPath))
{
    var psi = new ProcessStartInfo
    {
        FileName = backendPath,
        WorkingDirectory = appDir,
        UseShellExecute = false,
        RedirectStandardOutput = false,
        RedirectStandardError = false,
        CreateNoWindow = true,
    };
    try
    {
        backendProc = Process.Start(psi);
        Log($"Backend started (PID {backendProc?.Id})");
    }
    catch (Exception ex)
    {
        Log($"Failed to start backend: {ex.Message}");
    }
}
else
{
    Log($"Backend not found at {backendPath}");
}

// 3. 不再串行等待后端健康检查 — 立即启动前端，让前端自身的
//    健康轮询 (App.PollBackendHealthAsync) 在后台并行完成。
//    这消除了启动时的最大阻塞 (最多 20s 的白屏等待)。
_ = Task.Run(async () =>
{
    var healthUrl = "http://127.0.0.1:8765/api/health";
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    for (int i = 0; i < 40; i++) // 后台并行轮询, 最多 20s
    {
        try
        {
            var resp = await http.GetAsync(healthUrl);
            if (resp.IsSuccessStatusCode) { Log("Backend ready"); return; }
        }
        catch { }
        await Task.Delay(500);
    }
    Log("Warning: Backend not ready after timeout");
});

// 4. Start frontend
if (File.Exists(frontendPath))
{
    var frontendPsi = new ProcessStartInfo
    {
        FileName = frontendPath,
        WorkingDirectory = appDir,
        UseShellExecute = true,
    };
    var frontendProc = Process.Start(frontendPsi);
    Log($"Frontend started (PID {frontendProc?.Id})");

    // 5. Wait for frontend to close
    if (frontendProc is not null)
    {
        await frontendProc.WaitForExitAsync();
        Log("Frontend exited");
    }
}
else
{
    Log($"Frontend not found at {frontendPath}");
}

// 6. Kill backend
if (backendProc is not null && !backendProc.HasExited)
{
    try { backendProc.Kill(); backendProc.WaitForExit(3000); Log("Backend killed"); }
    catch { }
}

// Final cleanup — kill any orphaned backend.exe
foreach (var proc in Process.GetProcessesByName("backend"))
{
    try { proc.Kill(); proc.WaitForExit(1000); Log($"Cleaned up backend PID {proc.Id}"); }
    catch { }
}

Log("Launcher exiting");
