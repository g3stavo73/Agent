using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace Agent.Core.Protocol;

/// <summary>
/// Minimal typed client for the documented Codex App Server JSONL protocol.
/// It deliberately does not implement or extract OpenAI credentials; authentication
/// is delegated to the documented account/login/start flow in App Server.
/// </summary>
public sealed class CodexAppServerClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement EmptyObject = JsonSerializer.Deserialize<JsonElement>("{}");

    private readonly IJsonLineTransport _transport;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Channel<CodexServerMessage> _incoming = Channel.CreateUnbounded<CodexServerMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private CancellationTokenSource? _lifetime;
    private Task? _readerTask;
    private long _nextRequestId;
    private int _connected;
    private int _connectAttempted;
    private int _messageReaderActive;
    private int _disposed;

    public CodexAppServerClient(IJsonLineTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public bool IsConnected => Volatile.Read(ref _connected) == 1;

    public async Task ConnectAsync(
        string clientName = "agent_mobile",
        string clientTitle = "Agent",
        string clientVersion = "1.0.0",
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref _connectAttempted, 1, 0) != 0)
            throw new InvalidOperationException(
                "A Codex client instance is single-use. Create a new client to reconnect.");
        Volatile.Write(ref _connected, 1);

        try
        {
            await _transport.StartAsync(cancellationToken).ConfigureAwait(false);
            _lifetime = new CancellationTokenSource();
            _readerTask = Task.Run(() => ReadLoopAsync(_lifetime.Token), CancellationToken.None);

            // App Server requires exactly one initialize call before other methods.
            await CallAsync("initialize", new
            {
                clientInfo = new
                {
                    name = clientName,
                    title = clientTitle,
                    version = clientVersion
                }
            }, cancellationToken).ConfigureAwait(false);

            await SendNotificationAsync("initialized", new { }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Exchange(ref _connected, 0);
            if (_lifetime is not null)
                await _lifetime.CancelAsync().ConfigureAwait(false);
            if (_readerTask is not null)
            {
                try { await _readerTask.ConfigureAwait(false); }
                catch { /* Preserve the original connection error. */ }
            }
            try { await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* Preserve the original connection error. */ }
            _lifetime?.Dispose();
            _lifetime = null;
            _readerTask = null;
            throw;
        }
    }

    /// <summary>Starts the documented ChatGPT device-code login flow.</summary>
    public async Task<CodexDeviceCodeLogin> StartDeviceCodeLoginAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        JsonElement result = await CallAsync(
            "account/login/start",
            new { type = "chatgptDeviceCode" },
            cancellationToken).ConfigureAwait(false);

        string loginId = GetRequiredString(result, "loginId");
        string verificationUrl = GetRequiredString(result, "verificationUrl");
        string userCode = GetRequiredString(result, "userCode");

        if (!Uri.TryCreate(verificationUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Codex returned an invalid device verification URL.");
        }

        return new CodexDeviceCodeLogin(loginId, uri, userCode);
    }

    /// <summary>Cancels a pending device-code login through the documented App Server method.</summary>
    public Task CancelLoginAsync(
        string loginId,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrWhiteSpace(loginId);
        return CallAsync("account/login/cancel", new { loginId }, cancellationToken);
    }

    public Task<JsonElement> ReadAccountAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return CallAsync("account/read", new { }, cancellationToken);
    }

    public Task<JsonElement> ReadRateLimitsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return CallAsync("account/rateLimits/read", new { }, cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return CallAsync("account/logout", new { }, cancellationToken);
    }

    public async Task<string> StartThreadAsync(
        string workingDirectory,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var parameters = new Dictionary<string, object?>
        {
            ["cwd"] = workingDirectory
        };
        if (!string.IsNullOrWhiteSpace(model))
            parameters["model"] = model;

        JsonElement result = await CallAsync("thread/start", parameters, cancellationToken)
            .ConfigureAwait(false);
        if (!result.TryGetProperty("thread", out JsonElement thread))
            throw new InvalidDataException("Codex did not return a thread object.");

        return GetRequiredString(thread, "id");
    }

    public Task<string> StartTurnAsync(
        string threadId,
        string prompt,
        CancellationToken cancellationToken = default) =>
        StartTurnAsync(threadId, prompt, options: null, cancellationToken: cancellationToken);

    /// <summary>
    /// Starts a turn with documented, per-turn sandbox and structured-output options.
    /// Callers should prefer a restrictive sandbox for read-only analysis tasks.
    /// </summary>
    public async Task<string> StartTurnAsync(
        string threadId,
        string prompt,
        CodexTurnOptions? options,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var parameters = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["input"] = new[] { new { type = "text", text = prompt } }
        };
        if (!string.IsNullOrWhiteSpace(options?.ApprovalPolicy))
            parameters["approvalPolicy"] = options.ApprovalPolicy;
        if (options?.SandboxPolicy is not null)
            parameters["sandboxPolicy"] = options.SandboxPolicy;
        if (options?.OutputSchema is JsonElement schema)
            parameters["outputSchema"] = schema;

        JsonElement result = await CallAsync("turn/start", parameters, cancellationToken)
            .ConfigureAwait(false);
        if (!result.TryGetProperty("turn", out JsonElement turn))
            throw new InvalidDataException("Codex did not return a turn object.");
        return GetRequiredString(turn, "id");
    }

    public Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);

        return CallAsync("turn/interrupt", new { threadId, turnId }, cancellationToken);
    }

    /// <summary>
    /// Answers a command-execution approval request. The default app posture is to require
    /// an explicit UI action; this method never auto-approves a request.
    /// </summary>
    public Task RespondToCommandApprovalAsync(
        CodexServerMessage message,
        CodexApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        ValidateApprovalRequest(message, "item/commandExecution/requestApproval");
        return RespondToServerRequestAsync(
            message,
            new { decision = ToWireDecision(decision) },
            cancellationToken);
    }

    /// <summary>Answers a file-change approval request after explicit user confirmation.</summary>
    public Task RespondToFileChangeApprovalAsync(
        CodexServerMessage message,
        CodexApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        ValidateApprovalRequest(message, "item/fileChange/requestApproval");
        return RespondToServerRequestAsync(
            message,
            new { decision = ToWireDecision(decision) },
            cancellationToken);
    }

    private static void ValidateApprovalRequest(CodexServerMessage message, string expectedMethod)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!string.Equals(message.Method, expectedMethod, StringComparison.Ordinal))
            throw new ArgumentException($"Expected server request '{expectedMethod}'.", nameof(message));
        if (!message.RequiresResponse)
            throw new ArgumentException("The message is a notification, not a server request.", nameof(message));
    }

    private static string ToWireDecision(CodexApprovalDecision decision) => decision switch
    {
        CodexApprovalDecision.Accept => "accept",
        CodexApprovalDecision.AcceptForSession => "acceptForSession",
        CodexApprovalDecision.Decline => "decline",
        CodexApprovalDecision.Cancel => "cancel",
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown approval decision.")
    };

    /// <summary>
    /// Responds to a server-initiated JSON-RPC request, such as a command or file-change
    /// approval. Notifications have no request id and cannot be answered.
    /// The result shape must match the documented response for <paramref name="message"/>.
    /// </summary>
    public Task RespondToServerRequestAsync(
        CodexServerMessage message,
        object? result,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentNullException.ThrowIfNull(message);
        if (message.RequestId is not JsonElement requestId)
            throw new ArgumentException("The message is a notification, not a server request.", nameof(message));

        return WriteServerResponseAsync(requestId, result ?? new { }, cancellationToken);
    }

    /// <summary>
    /// Rejects a server-initiated JSON-RPC request with a JSON-RPC error response.
    /// Use the method-specific documented rejection decision when one exists; do not
    /// substitute a transport error for an ordinary user rejection.
    /// </summary>
    public Task RespondToServerRequestErrorAsync(
        CodexServerMessage message,
        long errorCode,
        string errorMessage,
        JsonElement? errorData = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        if (message.RequestId is not JsonElement requestId)
            throw new ArgumentException("The message is a notification, not a server request.", nameof(message));

        object error = errorData is JsonElement data
            ? new { code = errorCode, message = errorMessage, data }
            : new { code = errorCode, message = errorMessage };
        return WriteServerErrorAsync(requestId, error, cancellationToken);
    }

    /// <summary>
    /// Reads App Server notifications and server-initiated requests. This channel has one
    /// active consumer; a second reader fails fast rather than silently stealing events.
    /// A future multi-view app should add one dispatcher that fans out typed events.
    /// </summary>
    public async IAsyncEnumerable<CodexServerMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (Interlocked.CompareExchange(ref _messageReaderActive, 1, 0) != 0)
            throw new InvalidOperationException("Codex App Server messages already have an active reader.");

        try
        {
            await foreach (CodexServerMessage message in _incoming.Reader
                .ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return message;
            }
        }
        finally
        {
            Volatile.Write(ref _messageReaderActive, 0);
        }
    }

    private async Task<JsonElement> CallAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        long requestId = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(requestId, completion))
            throw new InvalidOperationException("Could not register the Codex RPC request.");

        try
        {
            string json = JsonSerializer.Serialize(new
            {
                id = requestId,
                method,
                @params = parameters ?? new { }
            }, JsonOptions);

            await WriteSerializedAsync(json, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task SendNotificationAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(new
        {
            method,
            @params = parameters ?? new { }
        }, JsonOptions);
        await WriteSerializedAsync(json, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSerializedAsync(string json, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.WriteLineAsync(json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private Task WriteServerResponseAsync(
        JsonElement requestId,
        object result,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(new
        {
            id = requestId,
            result
        }, JsonOptions);
        return WriteSerializedAsync(json, cancellationToken);
    }

    private Task WriteServerErrorAsync(
        JsonElement requestId,
        object error,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(new
        {
            id = requestId,
            error
        }, JsonOptions);
        return WriteSerializedAsync(json, cancellationToken);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        Exception? terminalError = null;
        try
        {
            await foreach (string line in _transport.ReadLinesAsync(cancellationToken)
                .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Codex returned a non-object JSON message.");

                // A JSON-RPC request has a method, even when its id happens to collide
                // with one of our outbound ids. Classify requests before matching responses.
                if (root.TryGetProperty("method", out JsonElement methodElement) &&
                    methodElement.ValueKind == JsonValueKind.String)
                {
                    string method = methodElement.GetString()!;
                    JsonElement parameters = root.TryGetProperty("params", out JsonElement paramsElement)
                        ? paramsElement.Clone()
                        : EmptyObject;
                    JsonElement? requestId = root.TryGetProperty("id", out JsonElement incomingId)
                        ? incomingId.Clone()
                        : null;

                    await _incoming.Writer.WriteAsync(
                        new CodexServerMessage(method, parameters, requestId),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (root.TryGetProperty("id", out JsonElement idElement) &&
                    idElement.ValueKind == JsonValueKind.Number &&
                    idElement.TryGetInt64(out long responseId) &&
                    _pending.TryRemove(responseId, out TaskCompletionSource<JsonElement>? completion))
                {
                    if (root.TryGetProperty("error", out JsonElement error))
                    {
                        if (error.ValueKind != JsonValueKind.Object)
                        {
                            completion.TrySetException(new InvalidDataException(
                                "Codex returned a malformed JSON-RPC error object."));
                            continue;
                        }

                        long code = error.TryGetProperty("code", out JsonElement codeElement) &&
                                    codeElement.ValueKind == JsonValueKind.Number &&
                                    codeElement.TryGetInt64(out long parsedCode)
                            ? parsedCode : -1;
                        string message = error.TryGetProperty("message", out JsonElement messageElement) &&
                                         messageElement.ValueKind == JsonValueKind.String
                            ? messageElement.GetString() ?? "Unknown RPC error"
                            : "Unknown RPC error";
                        JsonElement? data = error.TryGetProperty("data", out JsonElement dataElement)
                            ? dataElement.Clone() : null;
                        completion.TrySetException(new CodexRpcException(code, message, data));
                    }
                    else if (root.TryGetProperty("result", out JsonElement resultElement))
                    {
                        completion.TrySetResult(resultElement.Clone());
                    }
                    else
                    {
                        completion.TrySetException(new InvalidDataException(
                            "Codex returned a response without either 'result' or 'error'."));
                    }

                    continue;
                }

                // Ignore unmatched late responses (for example, after a caller cancelled).
                // Non-JSON-RPC objects are rejected by the parser above or ignored here.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception exception)
        {
            terminalError = exception;
        }
        finally
        {
            var error = terminalError ?? new EndOfStreamException("Codex App Server transport closed.");
            foreach (var pending in _pending.Values)
                pending.TrySetException(error);
            _pending.Clear();
            _incoming.Writer.TryComplete(terminalError);
            Interlocked.Exchange(ref _connected, 0);
        }
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            return property.GetString()!;
        }

        throw new InvalidDataException($"Codex response is missing '{propertyName}'.");
    }

    private void EnsureConnected()
    {
        ThrowIfDisposed();
        if (!IsConnected)
            throw new InvalidOperationException("Connect to Codex App Server first.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        Interlocked.Exchange(ref _connected, 0);
        if (_lifetime is not null)
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
            if (_readerTask is not null)
            {
                try { await _readerTask.ConfigureAwait(false); }
                catch { /* Reader errors are surfaced through pending calls/messages. */ }
            }
        }

        try { await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false); }
        finally
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
            _lifetime?.Dispose();
            // Do not dispose _writeGate: an in-flight caller may still be unwinding
            // from a transport write as shutdown completes.
        }
    }
}
