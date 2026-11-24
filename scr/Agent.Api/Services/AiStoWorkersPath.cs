using Microsoft.Extensions.Hosting;

namespace Agent.Api.Services;

/// <summary>Resolves the Rust worker binary in local repository and container layouts.</summary>
public static class AiStoWorkerPath
{
    private const string DefaultRelativePath = "workers/aisto-workers/target/release/aisto-worker";

    public static string Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["AiStoWorker:ExecutablePath"];
        var isDefault = string.IsNullOrWhiteSpace(configured);
        configured = isDefault ? DefaultRelativePath : configured!.Trim();

        if (Path.IsPathRooted(configured))
            return Path.GetFullPath(configured);

        var candidates = new List<string>
        {
            Path.GetFullPath(configured, environment.ContentRootPath),
            Path.GetFullPath(configured, Directory.GetCurrentDirectory())
        };

        // `dotnet run --project scr/Agent.Api` commonly sets ContentRoot to scr/Agent.Api,
        // while developers may also launch the API from the repository root.
        if (isDefault)
        {
            candidates.Add(Path.GetFullPath(Path.Combine("..", "..", configured), environment.ContentRootPath));
            candidates.Add(Path.GetFullPath(Path.Combine("..", "..", configured), Directory.GetCurrentDirectory()));
        }

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
