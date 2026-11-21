using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Agent.Api.Services;

public sealed class AgentDatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = configuration.GetConnectionString("AgentDatabase")
                ?? throw new InvalidOperationException("Database connection string is missing.");
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT to_regclass('public.agent_profiles') IS NOT NULL", connection);
            var schemaReady = (bool?)await command.ExecuteScalarAsync(cancellationToken) ?? false;
            return schemaReady
                ? HealthCheckResult.Healthy("PostgreSQL is reachable and the Agent schema is installed.")
                : HealthCheckResult.Unhealthy("PostgreSQL is reachable, but the Agent schema is not installed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL readiness check failed.", exception);
        }
    }
}
