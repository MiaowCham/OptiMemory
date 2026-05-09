using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace OptiMemory;

/// <summary>
/// UAC 自提权服务：非管理员进程通过 ShellExecute "runas" 启动自身的提权副本，
/// 提权进程执行内存优化后通过命名管道将结果回写给调用方，然后静默退出。
/// </summary>
static class ElevationService
{
    /// <summary>传入提权模式的标志参数（由 Program.Main 识别）</summary>
    public const string ElevatedArg = "--elevated";
    /// <summary>传入命名管道名称的参数</summary>
    public const string PipeArg     = "--pipe";

    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ─── 调用方（非管理员进程）────────────────────────────────────────────────

    /// <summary>
    /// 在非管理员进程中调用。通过 UAC 启动自身的提权副本，等待优化结果并返回。
    /// 若用户拒绝 UAC 或通信超时，返回 <c>null</c>。
    /// </summary>
    public static async Task<ElevatedResult?> RequestOptimizeAsync(
        CancellationToken ct = default)
    {
        var pipeName = $"OptiMemory_{Guid.NewGuid():N}";
        Program.Dbg($"提权请求创建: pipe={pipeName}");

        // 必须在启动提权进程之前创建管道服务端
        using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.In,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = GetExePath(),
                Arguments       = $"{ElevatedArg} {PipeArg} {pipeName}",
                UseShellExecute = true,   // 必须为 true 才能触发 UAC
                Verb            = "runas"
            });
            Program.Dbg("提权子进程启动成功，等待管道连接");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 用户在 UAC 对话框中点击了"否"
            Program.Dbg("提权请求被用户取消");
            return null;
        }

        // 最多等待 30 秒让提权进程连接
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await server.WaitForConnectionAsync(linked.Token);
            Program.Dbg("提权子进程已连接命名管道");
        }
        catch (OperationCanceledException)
        {
            Program.Dbg("等待提权子进程连接超时");
            return null;
        }

        try
        {
            using var reader = new StreamReader(server);
            var json = await reader.ReadToEndAsync(ct);
            Program.Dbg($"提权结果接收完成: bytes={json.Length}");
            return JsonSerializer.Deserialize<ElevatedResult>(json, _json);
        }
        catch (Exception ex)
        {
            Program.Dbg($"提权结果解析失败: {ex.Message}");
            return null;
        }
    }

    // ─── 提权子进程────────────────────────────────────────────────────────────

    /// <summary>
    /// 在提权进程中调用。执行完整内存优化并将 JSON 结果写入命名管道，然后返回。
    /// </summary>
    public static void RunWorker(string pipeName)
    {
        Program.Dbg($"提权子进程开始执行: pipe={pipeName}");
        ElevatedResult dto;
        try
        {
            var r = MemoryOptimizer.Optimize();
            dto = new ElevatedResult(
                Success:      true,
                FreedText:    r.FreedText,
                AfterText:    r.AfterText,
                TotalText:    r.TotalText,
                UsagePct:     (int)Math.Round(r.UsagePercent),
                Errors:       r.Errors,
                ErrorMessage: null);
            Program.Dbg("提权子进程优化完成，准备回传结果");
        }
        catch (Exception ex)
        {
            Program.Dbg($"提权子进程优化失败: {ex.Message}");
            dto = new ElevatedResult(
                Success: false, FreedText: null, AfterText: null, TotalText: null,
                UsagePct: 0, Errors: [], ErrorMessage: ex.Message);
        }

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(10_000);
            using var writer = new StreamWriter(client);
            writer.Write(JsonSerializer.Serialize(dto, _json));
            writer.Flush();
            Program.Dbg("提权子进程结果回传完成");
        }
        catch (Exception ex)
        {
            Program.Dbg($"提权子进程结果回传失败: {ex.Message}");
            /* best-effort：调用方超时会自行处理 */
        }
    }

    // ─── 辅助 ─────────────────────────────────────────────────────────────────

    private static string GetExePath() =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("无法确定可执行文件路径");
}

/// <summary>提权进程通过命名管道返回的优化结果。</summary>
public sealed record ElevatedResult(
    bool         Success,
    string?      FreedText,
    string?      AfterText,
    string?      TotalText,
    int          UsagePct,
    List<string> Errors,
    string?      ErrorMessage);
