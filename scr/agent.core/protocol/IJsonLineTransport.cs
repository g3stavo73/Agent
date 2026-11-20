namespace Agent.Core.Protocol;

/// <summary>
/// Bidirectional newline-delimited JSON transport used by Codex App Server.
/// Implementations must return complete UTF-8 JSON lines without the trailing newline.
/// WriteLineAsync must append exactly one newline to each outgoing message.
/// </summary>
public interface IJsonLineTransport : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);

    ValueTask WriteLineAsync(
        string json,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ReadLinesAsync(
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
