using System.Diagnostics;
using System.Threading.Channels;
using System.Text.Json;
using Agent.Api.Contracts;

namespace Agent.Api.Services;

/// <summary>
/// Bounded pool of persistent Rust JSON Lines workers.
/// Each process handles one request at a time; independent slots allow concurrent
/// AiSTo compilation without serializing every user behind one process.
/// </summary>
public sealed class RustAiStoCompiler : IAiStoCompiler, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly byte[] NewLine = [(byte)'\n'];

    private readonly string _executablePath;
    private readonly ILogger<RustAiStoCompiler> _logger;
    private readonly TimeSpan _timeout;
    private readonly Channel<WorkerSlot> _availableSlots;
    private readonly WorkerSlot[] _allSlots;
    private int _disposed;

    public RustAiStoCompiler(IConfiguration configuration, ILogger<RustAiStoCompiler> logger)
    {
        _executablePath = configuration["AiStoWorker:ExecutablePath"]
            ?? Path.Combine("workers", "aisto-worker", "target", "release", "aisto-worker");
        _logger = logger;

        var configuredSeconds = configuration.GetValue<int?>("AiStoWorker:TimeoutSeconds") ?? 15;
        _timeout = TimeSpan.FromSeconds(Math.Clamp(configuredSeconds, 1, 60));

        var defaultWorkerCount = Math.Clamp(Environment.ProcessorCount, 1, 4);
        var configuredWorkerCount = configuration.GetValue<int?>("AiStoWorker:WorkerCount") ?? defaultWorkerCount;
        var workerCount = Math.Clamp(configuredWorkerCount, 1, 8);

        _allSlots = Enumerable.Range(0, workerCount)
            .Select(index => new WorkerSlot(index + 1))
            .ToArray();
        _availableSlots = Channel.CreateBounded<WorkerSlot>(new BoundedChannelOptions(workerCount)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        foreach (var slot in _allSlots)
            _availableSlots.Writer.TryWrite(slot);

        _logger.LogInformation(
            "AiSTo Rust worker pool configured with {WorkerCount} process slots and {TimeoutSeconds}s deadline.",
            workerCount, _timeout.TotalSeconds);
    }

    public async Task<AiStoCompileResponse> CompileAsync(
        AiStoCompileRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var workerToken = timeout.Token;

        WorkerSlot slot;
        try
        {
            slot = await _availableSlots.Reader.ReadAsync(workerToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiStoWorkerTimeoutException(
                "The AiSTo worker pool queue exceeded the response deadline.");
        }

        try
        {
            return await CompileOnSlotAsync(slot, request, workerToken, cancellationToken);
        }
        finally
        {
            if (Volatile.Read(ref _disposed) == 0)
                _availableSlots.Writer.TryWrite(slot);
        }
    }

    private async Task<AiStoCompileResponse> CompileOnSlotAsync(
        WorkerSlot slot,
        AiStoCompileRequest request,
        CancellationToken workerToken,
        CancellationToken requestToken)
    {
        try
        {
            EnsureStarted(slot);
            var process = slot.Process
                ?? throw new AiStoWorkerUnavailableException("The Rust worker process is not available.");

            var requestId = Guid.NewGuid().ToString("N");
            var payload = new WorkerRequest(requestId, "compile_aisto",
                request.AgentName, request.Configuration, request.Capabilities);

            // Stream JSON directly to stdin instead of allocating a serialized JSON string.
            var input = process.StandardInput.BaseStream;
            await JsonSerializer.SerializeAsync(input, payload, JsonOptions, workerToken);
            await input.WriteAsync(NewLine, workerToken);
            await input.FlushAsync(workerToken);

            var responseLine = await process.StandardOutput.ReadLineAsync(workerToken);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                StopWorker(slot);
                throw new AiStoWorkerException("The Rust worker exited without a protocol response.");
            }

            WorkerResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<WorkerResponse>(responseLine, JsonOptions);
            }
            catch (JsonException ex)
            {
                StopWorker(slot);
                throw new AiStoWorkerException("The Rust worker returned invalid JSON.", ex);
            }

            if (response is null || !string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
            {
                StopWorker(slot);
                throw new AiStoWorkerException("The Rust worker response did not match the request.");
            }

            if (!response.Ok || response.SystemInstruction is null)
                throw new AiStoWorkerException(
                    response.Error ?? "The Rust worker rejected the AiSTo configuration.");

            return new AiStoCompileResponse(
                "context_compiled_only",
                response.SystemInstruction,
                response.DocumentationSources ?? [],
                response.RequestedCapabilities ?? request.Capabilities,
                "O contexto foi compilado. Nenhuma chamada ao Codex, pesquisa web ou alteração em repositório foi executada.");
        }
        catch (AiStoWorkerUnavailableException)
        {
            throw;
        }
        catch (AiStoWorkerException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
        {
            StopWorker(slot);
            throw new AiStoWorkerTimeoutException("The Rust AiSTo worker exceeded its response deadline.");
        }
        catch (OperationCanceledException)
        {
            StopWorker(slot);
            throw;
        }
        catch (Exception ex)
        {
            StopWorker(slot);
            throw new AiStoWorkerException("The AiSTo Rust worker could not complete the request.", ex);
        }
    }

    private void EnsureStarted(WorkerSlot slot)
    {
        if (slot.Process is { HasExited: false })
            return;

        if (slot.Process is not null)
            StopWorker(slot);

        if (!File.Exists(_executablePath))
            throw new AiStoWorkerUnavailableException(
                "Rust AiSTo worker executable is unavailable. Build the worker and configure AiStoWorker:ExecutablePath.");

        var absolutePath = Path.GetFullPath(_executablePath);
        var start = new ProcessStartInfo
        {
            FileName = absolutePath,
            WorkingDirectory = Path.GetDirectoryName(absolutePath)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                _logger.LogInformation("AiSTo worker {WorkerSlot}: {WorkerMessage}", slot.Id, args.Data);
        };

        try
        {
            if (!process.Start())
                throw new AiStoWorkerUnavailableException("Could not start the Rust AiSTo worker.");

            process.BeginErrorReadLine();
            slot.Process = process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private void StopWorker(WorkerSlot slot)
    {
        var process = slot.Process;
        slot.Process = null;
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogWarning(ex, "Could not stop AiSTo worker slot {WorkerSlot}.", slot.Id);
        }
        finally
        {
            process.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        _availableSlots.Writer.TryComplete();
        foreach (var slot in _allSlots)
            StopWorker(slot);

        return ValueTask.CompletedTask;
    }

    private sealed class WorkerSlot(int id)
    {
        public int Id { get; } = id;
        public Process? Process { get; set; }
    }
}
