using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace Agent.Core.Protocol;

/// <summary>
/// Adapter for the C ABI defined in docs/NATIVE_BACKEND_CONTRACT.md.
/// The native library owns the Codex App Server process and exposes its stdin/stdout
/// as newline-delimited UTF-8 messages. It does not parse JSON-RPC or handle auth.
/// </summary>
public sealed class NativeCodexLineTransport : IJsonLineTransport
{
    private const string NativeLibraryName = "codex_native_host";
    private const int InitialBufferSize = 64 * 1024;
    private const int MaximumMessageSize = 8 * 1024 * 1024;
    private const uint ReadTimeoutMilliseconds = 200;

    private readonly string _executablePath;
    private readonly string _workingDirectory;
    private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private IntPtr _handle;
    private CancellationTokenSource? _readerCancellation;
    private Task? _readerTask;
    private int _started;
    private int _stopped;

    public NativeCodexLineTransport(string executablePath, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        _executablePath = executablePath;
        _workingDirectory = workingDirectory;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            throw new InvalidOperationException("Native Codex transport has already been started.");

        try
        {
            _handle = NativeMethods.Create(_executablePath, _workingDirectory);
            if (_handle == IntPtr.Zero)
                throw new IOException("Native Codex host could not be created.");

            int startResult = NativeMethods.Start(_handle);
            if (startResult != 0)
                throw CreateNativeException("start", startResult);

            _readerCancellation = new CancellationTokenSource();
            _readerTask = Task.Run(() => ReadLoopAsync(_readerCancellation.Token), CancellationToken.None);
            return Task.CompletedTask;
        }
        catch
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.Destroy(_handle);
                _handle = IntPtr.Zero;
            }
            Interlocked.Exchange(ref _started, 0);
            throw;
        }
    }

    public ValueTask WriteLineAsync(
        string json,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        ArgumentNullException.ThrowIfNull(json);
        if (json.Contains('\n') || json.Contains('\r'))
            throw new ArgumentException("A JSONL message must contain exactly one line.", nameof(json));

        byte[] bytes = Encoding.UTF8.GetBytes(json);
        int result = NativeMethods.WriteLine(_handle, bytes, (nuint)bytes.Length);
        if (result != 0)
            throw CreateNativeException("write", result);

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        await foreach (string line in _incoming.Reader.ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return line;
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        Exception? terminalError = null;
        try
        {
            byte[] buffer = new byte[InitialBufferSize];
            while (!cancellationToken.IsCancellationRequested)
            {
                int result = NativeMethods.ReadLine(
                    _handle,
                    buffer,
                    (nuint)buffer.Length,
                    out nuint requiredLength,
                    ReadTimeoutMilliseconds);

                if (result == 0)
                    continue; // Read timeout; check cancellation and poll again.

                if (result == -2)
                {
                    ulong required = (ulong)requiredLength;
                    if (required == 0 || required > MaximumMessageSize)
                        throw new InvalidDataException("Codex message exceeded the native transport limit.");
                    buffer = new byte[checked((int)required)];
                    continue; // The native contract keeps the line queued on buffer-too-small.
                }

                if (result != 1)
                    throw CreateNativeException("read", result);

                int length = checked((int)(ulong)requiredLength);
                if (length < 0 || length > buffer.Length)
                    throw new InvalidDataException("Native host returned an invalid line length.");

                string line = Encoding.UTF8.GetString(buffer, 0, length);
                await _incoming.Writer.WriteAsync(line, cancellationToken).ConfigureAwait(false);
                if (buffer.Length > InitialBufferSize)
                    buffer = new byte[InitialBufferSize];
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
            _incoming.Writer.TryComplete(terminalError);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        // Once shutdown starts, cleanup must finish even if the caller's token is cancelled.
        _readerCancellation?.Cancel();
        IOException? stopError = null;
        if (_handle != IntPtr.Zero)
        {
            int result = NativeMethods.Stop(_handle);
            if (result != 0)
                stopError = CreateNativeException("stop", result);
        }

        if (_readerTask is not null)
        {
            try { await _readerTask.ConfigureAwait(false); }
            catch (Exception exception)
            {
                stopError ??= new IOException("Native Codex reader did not shut down cleanly.", exception);
            }
        }

        if (stopError is not null)
            throw stopError;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.Destroy(_handle);
                _handle = IntPtr.Zero;
            }
            _readerCancellation?.Dispose();
        }
    }

    private void EnsureStarted()
    {
        if (_handle == IntPtr.Zero || Volatile.Read(ref _started) != 1)
            throw new InvalidOperationException("Native Codex transport is not started.");
        if (Volatile.Read(ref _stopped) == 1)
            throw new InvalidOperationException("Native Codex transport has been stopped.");
    }

    private IOException CreateNativeException(string operation, int status)
    {
        string detail = "No native error detail was provided.";
        if (_handle != IntPtr.Zero)
        {
            byte[] errorBuffer = new byte[2048];
            int errorResult = NativeMethods.LastError(
                _handle, errorBuffer, (nuint)errorBuffer.Length, out nuint length);
            if (errorResult == 0 && (ulong)length <= (ulong)errorBuffer.Length && (ulong)length > 0)
                detail = Encoding.UTF8.GetString(errorBuffer, 0, checked((int)(ulong)length));
        }
        return new IOException($"Native Codex host {operation} failed (status {status}): {detail}");
    }

    private static class NativeMethods
    {
        [DllImport(NativeLibraryName, EntryPoint = "codex_host_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Create(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string executablePath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string workingDirectory);

        [DllImport(NativeLibraryName, EntryPoint = "codex_host_start", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Start(IntPtr host);

        [DllImport(NativeLibraryName, EntryPoint = "codex_host_write_line", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int WriteLine(IntPtr host, byte[] utf8Json, nuint length);

        [DllImport(NativeLibraryName, EntryPoint = "codex_host_read_line", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ReadLine(
            IntPtr host,
            [Out] byte[] buffer,
            nuint capacity,
            out nuint requiredLength,
            uint timeoutMilliseconds);

        [DllImport(NativeLibraryName, EntryPoint = "codex_host_stop", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Stop(IntPtr host);

        [DllImport(NativeLibraryName, EntryPoint = "codex_host_last_error", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LastError(
            IntPtr host,
            [Out] byte[] buffer,
            nuint capacity,
            out nuint requiredLength);

        [DllImport(NativeLibraryName, EntryPoint = "codex_host_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Destroy(IntPtr host);
    }
}
