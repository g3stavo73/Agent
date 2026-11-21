using System.Data;
using Agent.Api.Auth;
using Agent.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Agent.Api.Services;

public sealed class MicrosoftIdentityResolver(AgentDbContext db)
{
    public async Task<Guid?> ResolveAsync(string issuer, string subject, string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("The validated Microsoft token is missing iss or sub.");

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT public.resolve_app_user(@issuer, @subject, @email);", connection);
            command.Parameters.AddWithValue("issuer", issuer);
            command.Parameters.AddWithValue("subject", subject);
            command.Parameters.AddWithValue("email", (object?)email ?? DBNull.Value);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is Guid id && id != Guid.Empty ? id : null;
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }
}
