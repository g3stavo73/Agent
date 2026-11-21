using Agent.Api.Contracts;

namespace Agent.Api.Services;

public interface IAiStoCompiler
{
    Task<AiStoCompileResponse> CompileAsync(AiStoCompileRequest request, CancellationToken cancellationToken);
}

public sealed class AiStoWorkerUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner)
{
}

public sealed class AiStoWorkerException(string message, Exception? inner = null)
    : Exception(message, inner)
{
}

public sealed class AiStoWorkerTimeoutException(string message) : Exception(message)
{
}
