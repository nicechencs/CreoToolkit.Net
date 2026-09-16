using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CreoToolkit.Agent.Client;

/// <summary>Agent pipe 客户端:连接 Creo 进程内的 NamedPipeAgentServer,发送 JSONL 命令、接收响应。
/// 外部进程视角:只见 JSON DATA + token 字符串,不依赖 Sdk/Interop。</summary>
public sealed class NamedPipeAgentClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private int _disposed;

    private NamedPipeAgentClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _reader = new StreamReader(pipe, utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        _writer = new StreamWriter(pipe, utf8NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
    }

    /// <summary>按 pipe 名连接。</summary>
    public static NamedPipeAgentClient Connect(string pipeName, int timeoutMs = 5000)
    {
        ThrowUtil.IfNullOrWhiteSpace(pipeName);

        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(timeoutMs);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
        return new NamedPipeAgentClient(pipe);
    }

    /// <summary>按进程 pid 连接(pipe 名 = ctk-agent-{pid})。</summary>
    public static NamedPipeAgentClient ConnectByPid(int pid, int timeoutMs = 5000)
        => Connect($"ctk-agent-{pid}", timeoutMs);

    /// <summary>发送 verb + args,等待响应。</summary>
    public async Task<AgentWireResponse> ExecuteAsync(
        string verb,
        Dictionary<string, object?>? args = null,
        int timeoutMs = 30000,
        CancellationToken ct = default)
    {
        ThrowUtil.IfNullOrWhiteSpace(verb);
        ThrowUtil.IfDisposed(Volatile.Read(ref _disposed) != 0, this);

        var request = new AgentWireRequest
        {
            Version = AgentWireCodec.ProtocolVersion,
            Id = Guid.NewGuid().ToString("N"),
            Verb = verb,
            Args = args,
            TimeoutMs = timeoutMs,
        };

        var line = AgentWireCodec.Serialize(request);
        await _writer.WriteLineAsync(line).ConfigureAwait(false);

        // 客户端超时略宽于服务端:正常情况先收到服务端 agent.timeout 响应,本地取消只兜底
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs + 2000);

        while (true)
        {
            string? responseLine;
            try
            {
                var readTask = _reader.ReadLineAsync();
                var completed = await Task.WhenAny(readTask, Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);
                if (completed != readTask)
                    throw new OperationCanceledException(timeoutCts.Token);
                responseLine = await readTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return AgentWireCodec.ErrorResponse(request.Id, "agent.timeout",
                    "客户端等待超时;执行状态未知,副作用可能仍会发生");
            }

            if (responseLine is null)
            {
                return AgentWireCodec.ErrorResponse(request.Id, "pipe.disconnected",
                    "服务端关闭了连接");
            }

            var resp = AgentWireCodec.ParseResponse(responseLine, out var parseError);
            if (resp is null)
            {
                return AgentWireCodec.ErrorResponse(request.Id, "wire.parse-error",
                    parseError ?? "无法解析响应");
            }

            // 丢弃前一请求超时后迟到的陈旧响应,只认本请求 id(id 为空=服务端解析错误,直接上抛)
            if (resp.Id.Length == 0 || resp.Id == request.Id)
                return resp;
        }
    }

    /// <summary>发送 agent.hello 握手。</summary>
    public async Task<HelloResult> HelloAsync(CancellationToken ct = default)
    {
        var resp = await ExecuteAsync("agent.hello", timeoutMs: 5000, ct: ct).ConfigureAwait(false);
        if (!resp.Ok)
            throw new InvalidOperationException($"agent.hello 失败: {resp.Error} - {resp.Reason}");

        var data = resp.Data ?? throw new InvalidOperationException("agent.hello 响应缺少 data");

        var protocolVersion = data.TryGetValue("protocolVersion", out var pv)
            ? Convert.ToInt32(pv is JsonElement je ? je.GetInt32() : pv)
            : 0;

        var verbs = ExtractStringList(data, "verbs");
        var capabilities = ExtractStringList(data, "capabilities");

        return new HelloResult(protocolVersion, verbs, capabilities);
    }

    private static List<string> ExtractStringList(Dictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var val) || val is null)
            return new List<string>();

        if (val is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in je.EnumerateArray())
                list.Add(item.GetString() ?? string.Empty);
            return list;
        }

        return new List<string>();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _reader.Dispose();
        _writer.Dispose();
        _pipe.Dispose();
    }
}
