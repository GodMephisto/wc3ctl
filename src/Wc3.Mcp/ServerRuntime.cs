// src/Wc3.Mcp/ServerRuntime.cs
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Wc3.Mcp;

/// <summary>
/// The stdio host side of a tool call, kept out of the tool bodies. OBS-02 and OBS-04 name one slow
/// call and one failed call in a single line each, TRN-03 holds the input open until a call in
/// flight has written its response, and PRF-05 puts the working set on the start line when a folder
/// tool has pushed the process past a gigabyte. Both hooks are the SDK 1.4.1 request and message
/// filters, registered on the options at tool build time in <see cref="Wc3McpServer.CreateOptions"/>.
/// No line here carries a parameter value or any map content.
/// </summary>
internal static class ServerRuntime
{
    /// <summary>The logger category the two per call lines use. Program keeps it at information.</summary>
    public const string Category = "Wc3.Mcp.ToolCalls";

    /// <summary>A call longer than this gets its own line even when it succeeded.</summary>
    public static readonly TimeSpan SlowCall = TimeSpan.FromSeconds(2);

    /// <summary>How long the read side waits for calls in flight once stdin has reported end of stream.</summary>
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromMinutes(5);

    /// <summary>After the handlers are idle, how long a response still gets to reach the pipe.</summary>
    public static readonly TimeSpan ResponseGrace = TimeSpan.FromSeconds(5);

    /// <summary>PRF-05, one gibibyte of working set, the point where the start line gains the note.</summary>
    public const long MemoryLimitBytes = 1024L * 1024 * 1024;

    /// <summary>The JSON-RPC code the SDK uses for a request the client cancelled.</summary>
    private const int RequestCancelled = -32800;

    private static readonly object Gate = new();
    private static readonly Dictionary<RequestId, PendingCall> Calls = new();

    private static TaskCompletionSource _handlersIdle = CompletedSource();
    private static TaskCompletionSource _responsesIdle = CompletedSource();
    private static int _handlers;
    private static int _responses;
    private static string? _currentTool;
    private static int _memoryNoteWritten;

    /// <summary>PRF-05. The tool the newest call started, or null when nothing is running.</summary>
    public static string? CurrentTool
    {
        get { lock (Gate) return _currentTool; }
    }

    /// <summary>PRF-05. True once the process working set is over the line.</summary>
    public static bool IsOverMemoryLimit() => Environment.WorkingSet > MemoryLimitBytes;

    /// <summary>PRF-05. The working set in whole mebibytes, for the start line note.</summary>
    public static long MemoryMegabytes() => Environment.WorkingSet / (1024 * 1024);

    /// <summary>
    /// OBS-02 and OBS-04. The call tool filter the server registers on its options. It starts the
    /// clock, names the tool while it runs, and on a failure logs the code and the elapsed time
    /// once before rethrowing. A success is left to the outgoing message filter, which sees the
    /// finished response and can count its bytes.
    /// </summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> ObserveToolCalls(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return async (request, cancellationToken) =>
        {
            string tool = request.Params?.Name ?? "unknown";
            RequestId id = request.JsonRpcRequest.Id;
            long started = Stopwatch.GetTimestamp();
            Begin(id, tool);
            NoteMemoryPressure();

            try
            {
                CallToolResult result = await next(request, cancellationToken).ConfigureAwait(false);
                End(id, Stopwatch.GetElapsedTime(started).TotalMilliseconds, failed: false);
                return result;
            }
            catch (Exception error)
            {
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                End(id, elapsed, failed: true);
                // A shutdown cancellation is not a failed call the operator can act on, and the SDK
                // drops the reply for it, so the line would be noise.
                if (!cancellationToken.IsCancellationRequested)
                {
                    LoggerFor(request).Log(
                        LogLevel.Warning,
                        "wc3 tool {Tool} failed with code {Code} after {Elapsed} ms",
                        tool,
                        ErrorCode(error),
                        (long)elapsed);
                }
                throw;
            }
        };
    }

    /// <summary>
    /// OBS-02. The outgoing message filter. It runs after the response has been written, so the
    /// drain in TRN-03 only releases the input once the bytes are on the pipe, and it is the place
    /// where a slow call over two seconds gets its line with the response size.
    /// </summary>
    public static McpMessageHandler ObserveOutgoingResponses(McpMessageHandler next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return async (context, cancellationToken) =>
        {
            try
            {
                await next(context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                FinishResponse(context, context.JsonRpcMessage);
            }
        };
    }

    /// <summary>
    /// TRN-03. Waits for the calls in flight to finish and their responses to be written. The
    /// stdin wrapper calls this before it reports end of stream, so a client that closes the pipe
    /// during a write still gets its response and the host exits 0 afterwards.
    /// </summary>
    public static async Task WaitForDrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Task handlers;
        lock (Gate)
        {
            handlers = _handlers == 0 ? Task.CompletedTask : _handlersIdle.Task;
        }
        await WaitQuietlyAsync(handlers, timeout, cancellationToken).ConfigureAwait(false);

        Task responses;
        lock (Gate)
        {
            responses = _responses == 0 ? Task.CompletedTask : _responsesIdle.Task;
        }
        await WaitQuietlyAsync(responses, ResponseGrace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// TRN-03. Standard input with one difference. When the inner stream reports end of stream and
    /// a call is still running, the read blocks until the response is written and only then lets
    /// the transport see the end of stream and disconnect.
    /// </summary>
    public sealed class DrainingStdinStream : Stream
    {
        private readonly Stream _inner;

        public DrainingStdinStream(Stream inner) =>
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            if (read == 0) WaitForDrainAsync(DrainTimeout).GetAwaiter().GetResult();
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) await WaitForDrainAsync(DrainTimeout, cancellationToken).ConfigureAwait(false);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            if (read == 0) await WaitForDrainAsync(DrainTimeout, cancellationToken).ConfigureAwait(false);
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class PendingCall
    {
        public PendingCall(string tool) => Tool = tool;

        public string Tool { get; }
        public double ElapsedMs { get; set; }
        public bool Failed { get; set; }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private static void Begin(RequestId id, string tool)
    {
        lock (Gate)
        {
            if (_handlers == 0) _handlersIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_responses == 0) _responsesIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _handlers++;
            _responses++;
            _currentTool = tool;
            Calls[id] = new PendingCall(tool);
        }
    }

    private static void End(RequestId id, double elapsedMs, bool failed)
    {
        lock (Gate)
        {
            if (Calls.TryGetValue(id, out PendingCall? call))
            {
                call.ElapsedMs = elapsedMs;
                call.Failed = failed;
            }
            if (_handlers > 0) _handlers--;
            if (_handlers == 0)
            {
                _currentTool = null;
                _handlersIdle.TrySetResult();
            }
        }
    }

    private static void FinishResponse(MessageContext context, JsonRpcMessage message)
    {
        // A server initiated request can reuse a client id value, so only a response or an error
        // may end a pending tool call.
        if (message is not (JsonRpcResponse or JsonRpcError)) return;
        JsonRpcMessageWithId withId = (JsonRpcMessageWithId)message;

        PendingCall? call;
        lock (Gate)
        {
            if (!Calls.Remove(withId.Id, out call)) return;
            if (_responses > 0) _responses--;
            if (_responses == 0) _responsesIdle.TrySetResult();
        }

        // A failed call already has its warning line, and a fast call is not worth a line.
        if (call.Failed || call.ElapsedMs < SlowCall.TotalMilliseconds) return;

        LoggerFor(context).Log(
            LogLevel.Information,
            "wc3 tool {Tool} finished in {Elapsed} ms with a {Bytes} byte response",
            call.Tool,
            (long)call.ElapsedMs,
            WireBytes(message));
    }

    /// <summary>
    /// The bytes the stdio transport will write for this message, counted the same way it
    /// serializes, plus the newline it appends. The serializer writes into a counting stream rather
    /// than a byte array, so measuring a large response does not double its footprint. Only called
    /// for a slow call, so the second serialization costs nothing on the common path.
    /// </summary>
    private static int WireBytes(JsonRpcMessage message)
    {
        try
        {
            var counter = new CountingStream();
            JsonSerializer.Serialize(counter, message, typeof(JsonRpcMessage), McpJsonUtilities.DefaultOptions);
            return (int)Math.Min(int.MaxValue, counter.Count + 1);
        }
        catch (Exception)
        {
            // A byte count is not worth breaking a response that is already written.
            return 0;
        }
    }

    /// <summary>A sink that keeps only the count, used to measure a response without holding it twice.</summary>
    private sealed class CountingStream : Stream
    {
        public long Count { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Count;
        public override long Position
        {
            get => Count;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Count += count;
        public override void Write(ReadOnlySpan<byte> buffer) => Count += buffer.Length;
        public override void WriteByte(byte value) => Count++;
    }

    /// <summary>
    /// PRF-05. When the process is already over a gigabyte as a call starts, the start line is
    /// repeated on stderr with the working set and the tool in flight. Once per process, so a
    /// folder tool that stays over the line does not fill the log.
    /// </summary>
    private static void NoteMemoryPressure()
    {
        if (!IsOverMemoryLimit()) return;
        if (Interlocked.Exchange(ref _memoryNoteWritten, 1) != 0) return;
        Console.Error.WriteLine(Wc3McpServer.BuildStartLine());
    }

    private static ILogger LoggerFor(MessageContext context) =>
        context.Services?.GetService(typeof(ILoggerFactory)) is ILoggerFactory factory
            ? factory.CreateLogger(Category)
            : NullLogger.Instance;

    private static int ErrorCode(Exception error) => error switch
    {
        McpProtocolException protocol => (int)protocol.ErrorCode,
        McpException => (int)McpErrorCode.InternalError,
        OperationCanceledException => RequestCancelled,
        _ => (int)McpErrorCode.InternalError,
    };

    private static async Task WaitQuietlyAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (task.IsCompleted) return;
        try
        {
            await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The bound is the point. The call still finishes, the host just stops waiting for it.
        }
    }
}
