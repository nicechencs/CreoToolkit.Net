using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using CreoToolkit.Agent.Client;
using CreoToolkit.Agent.Commands;
using CreoToolkit.Interop.Diagnostics;
using global::Serilog;

namespace CreoToolkit.Agent.Transport;

/// <summary>Named pipe agent 服务端:单客户端串行,JSONL 协议。
/// <para>pipe 名 = ctk-agent-{pid};PipeSecurity 限当前用户;
/// 后台线程 accept → 限长逐块读行 → 解析 → 经 executor 主线程 marshal → 写回响应。</para>
/// <para>控制面 verb(服务端直接应答,不进 router/policy):agent.hello 握手、agent.shutdown 触发
/// <see cref="ShutdownRequested"/> 使宿主泵循环退出。</para>
/// <para>已知边界:pipe 名可被同机进程抢注,客户端无服务端身份验证,属 v1 已知边界;
/// 监听持续失败按 <see cref="FailureBackoffMs"/> 退避,连续失败达阈值后放弃监听并记
/// pipe.listen-abort 事件。</para></summary>
[SupportedOSPlatform("windows")]
public sealed class NamedPipeAgentServer : IDisposable
{
    private const string LogModule = "Agent";
    private const CreoLogLayer LogLayer = CreoLogLayer.L4;

    private static readonly ILogger Log = CreoSerilog.ForContext(LogModule, LogLayer);

    private readonly MainThreadAgentExecutor _executor;
    private readonly string _pipeName;
    private readonly IReadOnlyList<string> _registeredVerbs;
    private readonly bool _useAcl;
    private CancellationTokenSource? _cts;
    private Thread? _listenThread;
    private volatile NamedPipeServerStream? _activePipe;
    private int _disposed;
    private volatile bool _listenAborted;

    /// <summary>pipe 名。</summary>
    public string PipeName => _pipeName;

    /// <summary>监听连续失败达阈值后是否已放弃(pipe 名被抢注等永久性故障)。</summary>
    public bool IsListenAborted => _listenAborted;

    /// <summary>非取消路径监听失败后的退避毫秒数(防零间隔空转);测试可注入小值。</summary>
    internal int FailureBackoffMs { get; set; } = 200;

    /// <summary>连续监听失败放弃阈值;成功 accept 后清零。测试可注入小值。</summary>
    internal int MaxConsecutiveListenFailures { get; set; } = 50;

    /// <summary>收到 agent.shutdown 或服务端 Stop/Dispose 时触发;宿主据此退出泵循环。
    /// 多路径可能重复触发(verb/Stop/Dispose),订阅方须幂等(如 cts.Cancel)。</summary>
    public event Action? ShutdownRequested;

    public NamedPipeAgentServer(
        MainThreadAgentExecutor executor,
        IReadOnlyList<string> registeredVerbs,
        string? pipeName = null,
        bool useAcl = true)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _registeredVerbs = registeredVerbs ?? throw new ArgumentNullException(nameof(registeredVerbs));
        _pipeName = pipeName ?? $"ctk-agent-{Process.GetCurrentProcess().Id}";
        _useAcl = useAcl;
    }

    /// <summary>启动后台监听线程。</summary>
    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(NamedPipeAgentServer));

        _cts = new CancellationTokenSource();
        _listenThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = $"AgentPipe-{_pipeName}",
        };
        _listenThread.Start();

        CreoSerilog.Information(Log, "pipe server started",
            @event: "pipe.listen",
            props: new { pipeName = _pipeName });
    }

    /// <summary>停止监听并通知宿主泵退出;客户端断开。</summary>
    public void Stop()
    {
        _cts?.Cancel();
        InterruptActivePipe();
        RaiseShutdownRequested();
    }

    private void RaiseShutdownRequested() => ShutdownRequested?.Invoke();

    // 取消路径强制中断阻塞中的 pipe I/O(半行慢客户端不能卡死监听线程)
    private void InterruptActivePipe()
    {
        try
        {
            _activePipe?.Dispose();
        }
        catch
        {
            // 已释放/竞态,忽略
        }
    }

    private void ListenLoop()
    {
        var ct = _cts!.Token;
        var consecutiveFailures = 0;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            var failed = false;
            try
            {
                pipe = CreatePipe();
                _activePipe = pipe;
                // 异步等待+同步阻塞,支持取消
                pipe.WaitForConnectionAsync(ct).GetAwaiter().GetResult();

                // 成功 accept:失败计数清零
                consecutiveFailures = 0;

                CreoSerilog.Information(Log, "client connected",
                    @event: "pipe.accept",
                    props: new { pipeName = _pipeName });

                HandleClient(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (IOException ex)
            {
                if (ct.IsCancellationRequested) break;
                failed = true;
                CreoSerilog.Warning(Log, "pipe I/O error, back to accept",
                    @event: "pipe.io-error",
                    props: new { pipeName = _pipeName, error = ex.Message });
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break;
                failed = true;
                CreoSerilog.Error(Log, ex, "pipe listen error",
                    @event: "pipe.listen-error",
                    props: new { pipeName = _pipeName, error = ex.Message });
            }
            finally
            {
                _activePipe = null;
                pipe?.Dispose();
            }

            if (!failed)
                continue;

            // 失败退避:防 pipe 名被抢注等持续故障导致零间隔空转
            consecutiveFailures++;
            if (consecutiveFailures >= MaxConsecutiveListenFailures)
            {
                _listenAborted = true;
                CreoSerilog.Error(Log, "listen aborted after consecutive failures",
                    @event: "pipe.listen-abort",
                    props: new { pipeName = _pipeName, failures = consecutiveFailures });
                break;
            }

            // 可被取消令牌打断的退避等待;cts 竞态释放时直接退出
            try
            {
                ct.WaitHandle.WaitOne(FailureBackoffMs);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private void HandleClient(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var reader = new BoundedLineReader(pipe, AgentWireCodec.MaxLineBytes);
        using var writer = new StreamWriter(pipe, utf8NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        while (!ct.IsCancellationRequested && pipe.IsConnected)
        {
            string? line;
            bool tooLong;
            try
            {
                line = reader.ReadLine(ct, out tooLong);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (tooLong)
            {
                // 超限即断开:防慢客户端无限喂字节耗内存
                var errResp = AgentWireCodec.ErrorResponse("",
                    "wire.line-too-long", $"单行超过 {AgentWireCodec.MaxLineBytes} 字节上限,连接已断开");
                WriteLine(writer, AgentWireCodec.Serialize(errResp));

                CreoSerilog.Warning(Log, "line too long, disconnecting",
                    @event: "pipe.bad-request",
                    props: new { pipeName = _pipeName, reason = "line-too-long" });
                break;
            }

            if (line is null)
                break;

            if (line.Length == 0)
                continue;

            var req = AgentWireCodec.ParseRequest(line, out var parseError);
            if (req is null)
            {
                var errResp = AgentWireCodec.ErrorResponse("",
                    "wire.parse-error", parseError ?? "请求解析失败");
                WriteLine(writer, AgentWireCodec.Serialize(errResp));

                CreoSerilog.Warning(Log, "request parse failed",
                    @event: "pipe.bad-request",
                    props: new { pipeName = _pipeName, reason = parseError });
                continue;
            }

            // 协议版本校验
            if (req.Version != AgentWireCodec.ProtocolVersion)
            {
                var mismatch = AgentWireCodec.VersionMismatchResponse(req.Id, req.Version);
                WriteLine(writer, AgentWireCodec.Serialize(mismatch));
                continue;
            }

            // agent.hello 握手:服务端直接应答,不进 router
            if (req.Verb == "agent.hello")
            {
                var helloData = new Dictionary<string, object?>
                {
                    ["protocolVersion"] = AgentWireCodec.ProtocolVersion,
                    ["verbs"] = _registeredVerbs.ToArray(),
                    ["capabilities"] = new[] { "pipe-jsonl", "cancel-queued", "shutdown" },
                };
                var helloResp = AgentWireCodec.OkResponse(req.Id, helloData);
                WriteLine(writer, AgentWireCodec.Serialize(helloResp));
                continue;
            }

            // agent.shutdown 控制面:应答后通知宿主泵退出,并结束本连接
            if (req.Verb == "agent.shutdown")
            {
                var shutdownResp = AgentWireCodec.OkResponse(req.Id,
                    new Dictionary<string, object?> { ["stopping"] = true });
                WriteLine(writer, AgentWireCodec.Serialize(shutdownResp));

                CreoSerilog.Information(Log, "shutdown requested by client",
                    @event: "pipe.shutdown",
                    props: new { pipeName = _pipeName });

                RaiseShutdownRequested();
                break;
            }

            // 泵未激活:业务命令立即拒收,不给客户端不可诊断的超时
            if (!_executor.IsPumpActive)
            {
                var inactiveResp = AgentWireCodec.ErrorResponse(req.Id,
                    "agent.pump-inactive", "主线程泵未运行,需先在宿主进程启动泵循环");
                WriteLine(writer, AgentWireCodec.Serialize(inactiveResp));

                CreoSerilog.Warning(Log, "pump inactive, command rejected",
                    @event: "pipe.pump-inactive",
                    props: new { pipeName = _pipeName, verb = req.Verb });
                continue;
            }

            // 构造 AgentCommand 投递到主线程
            var args = AgentWireCodec.NormalizeArgs(req.Args);
            var correlationId = req.Id;
            var command = new AgentCommand(req.Verb, args, correlationId);

            // 超时语义:队列等待超时统一回 agent.timeout;agent.canceled 仅保留给显式取消(停机/断连)。
            // cmdCts 只链接服务端停机 token,不做 CancelAfter,消除双超时竞态。
            var timeoutMs = req.TimeoutMs ?? 30000;
            using var cmdCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            AgentResult agentResult;
            var task = _executor.EnqueueAsync(command, cmdCts.Token);
            bool completed;
            try
            {
                completed = task.Wait(timeoutMs, ct);
            }
            catch (OperationCanceledException)
            {
                // 服务端停机:显式取消语义
                agentResult = AgentResult.Fail(correlationId, "agent.canceled", "服务端停机,命令已放弃");
                WriteLine(writer, AgentWireCodec.Serialize(ToWire(req.Id, agentResult)));
                break;
            }

            if (completed)
            {
                agentResult = task.Result;
            }
            else
            {
                // 放弃排队项:泵到达时跳过(其 agent.canceled 结果被丢弃,客户端只见 timeout)
                cmdCts.Cancel();
                agentResult = AgentResult.Fail(correlationId, "agent.timeout",
                    "执行状态未知,副作用可能仍会发生");
            }

            WriteLine(writer, AgentWireCodec.Serialize(ToWire(req.Id, agentResult)));
        }

        CreoSerilog.Information(Log, "client disconnected",
            @event: "pipe.disconnect",
            props: new { pipeName = _pipeName });
    }

    private static AgentWireResponse ToWire(string id, AgentResult result)
    {
        if (result.Ok)
        {
            var data = result.Data is not null
                ? result.Data.ToDictionary(p => p.Key, p => p.Value)
                : null;
            return AgentWireCodec.OkResponse(id, data);
        }

        return AgentWireCodec.ErrorResponse(id,
            result.Error ?? "agent.unexpected",
            result.Reason ?? "未知错误");
    }

    private static void WriteLine(StreamWriter writer, string line)
    {
        try
        {
            writer.WriteLine(line);
        }
        catch (IOException)
        {
            // 客户端已断开
        }
        catch (ObjectDisposedException)
        {
            // pipe 已被取消路径释放
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        if (!_useAcl)
        {
            return new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException(
                "无法解析当前用户 SID,拒绝创建无 ACL 防护的 pipe。");

        var ps = new PipeSecurity();
        ps.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: ps);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts?.Cancel();
        InterruptActivePipe();
        RaiseShutdownRequested();
        _listenThread?.Join(3000);
        _cts?.Dispose();
    }

    /// <summary>限长逐块行读取器:读中计数字节,超上限立即报告(不等换行),防慢客户端 OOM。
    /// 单客户端串行场景,跨行残余字节留在块缓冲内。</summary>
    private sealed class BoundedLineReader
    {
        private readonly Stream _stream;
        private readonly int _maxLineBytes;
        private readonly byte[] _chunk = new byte[8192];
        private readonly MemoryStream _line = new();
        private int _chunkLen;
        private int _chunkPos;

        public BoundedLineReader(Stream stream, int maxLineBytes)
        {
            _stream = stream;
            _maxLineBytes = maxLineBytes;
        }

        /// <summary>读一行(UTF-8,去尾部 \r);EOF 且无残留返 null;超限置 tooLong。</summary>
        public string? ReadLine(CancellationToken ct, out bool tooLong)
        {
            tooLong = false;
            _line.SetLength(0);

            while (true)
            {
                if (_chunkPos >= _chunkLen)
                {
                    // 异步读+同步阻塞:取消令牌可中断(pipe 需 PipeOptions.Asynchronous)
                    _chunkLen = _stream.ReadAsync(_chunk, 0, _chunk.Length, ct).GetAwaiter().GetResult();
                    _chunkPos = 0;
                    if (_chunkLen == 0)
                        return _line.Length == 0 ? null : Decode();
                }

                var idx = Array.IndexOf(_chunk, (byte)'\n', _chunkPos, _chunkLen - _chunkPos);
                if (idx >= 0)
                {
                    _line.Write(_chunk, _chunkPos, idx - _chunkPos);
                    _chunkPos = idx + 1;
                    if (_line.Length > _maxLineBytes)
                    {
                        tooLong = true;
                        return string.Empty;
                    }
                    return Decode();
                }

                _line.Write(_chunk, _chunkPos, _chunkLen - _chunkPos);
                _chunkPos = _chunkLen;
                // 读中即查超限,不等换行
                if (_line.Length > _maxLineBytes)
                {
                    tooLong = true;
                    return string.Empty;
                }
            }
        }

        private string Decode()
        {
            var length = (int)_line.Length;
            var buffer = _line.GetBuffer();
            if (length > 0 && buffer[length - 1] == (byte)'\r')
                length--;
            return Encoding.UTF8.GetString(buffer, 0, length);
        }
    }
}
