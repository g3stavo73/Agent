using Agent.Api.Auth;
using Microsoft.EntityFrameworkCore;

namespace Agent.Api.Data;

/// <summary>
/// Executes a short database unit-of-work with PostgreSQL RLS bound to the
/// authenticated internal user. SET LOCAL is transaction-scoped and cannot
/// leak through Npgsql connection pooling.
/// </summary>
public sealed class UserDataSession(AgentDbContext db, CurrentUserContext currentUser)
{
    public async Task<T> ExecuteAsync<T>(
        Func<AgentDbContext, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsResolved)
            throw new InvalidOperationException("An authenticated Agent user is required.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.user_id', {currentUser.Id.ToString()}, TRUE);",
            cancellationToken);

        var result = await operation(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
