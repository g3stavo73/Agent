using System.Text.Json;
using Agent.Api.Auth;
using Agent.Api.Contracts;
using Agent.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Agent.Api.Services;

public sealed class AgentProfileService(UserDataSession session, CurrentUserContext currentUser)
{
    public Task<IReadOnlyList<AgentProfileResponse>> ListAsync(CancellationToken cancellationToken) =>
        session.ExecuteAsync<IReadOnlyList<AgentProfileResponse>>(async (db, ct) =>
        {
            var rows = await db.AgentProfiles.AsNoTracking()
                .OrderByDescending(x => x.UpdatedAtUtc).Take(100).ToListAsync(ct);
            return rows.Select(AgentProfileResponse.FromRecord).ToList();
        }, cancellationToken);

    public Task<AgentProfileResponse> CreateAsync(AgentProfileRequest request, CancellationToken cancellationToken) =>
        session.ExecuteAsync(async (db, ct) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) throw new AgentValidationException(errors);
            var now = DateTimeOffset.UtcNow;
            var record = ToRecord(request, currentUser.Id, Guid.NewGuid(), now, now);
            db.AgentProfiles.Add(record);
            await db.SaveChangesAsync(ct);
            return AgentProfileResponse.FromRecord(record);
        }, cancellationToken);

    public Task<AgentProfileResponse?> UpdateAsync(Guid id, AgentProfileRequest request, CancellationToken cancellationToken) =>
        session.ExecuteAsync<AgentProfileResponse?>(async (db, ct) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) throw new AgentValidationException(errors);
            var record = await db.AgentProfiles.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (record is null) return null;
            Apply(record, request);
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return AgentProfileResponse.FromRecord(record);
        }, cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        session.ExecuteAsync(async (db, ct) =>
        {
            var record = await db.AgentProfiles.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (record is null) return false;
            db.AgentProfiles.Remove(record);
            await db.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);

    private static List<string> Validate(AgentProfileRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name) || AiStoSpecializationRequest.CharacterCount(request.Name.Trim()) > 80)
            errors.Add("Name must contain between 1 and 80 characters.");
        if (AiStoSpecializationRequest.CharacterCount(request.Description) > 1600)
            errors.Add("Description cannot exceed 1600 characters.");
        if (!string.Equals(request.Engine, "codex", StringComparison.OrdinalIgnoreCase))
            errors.Add("Only the Codex engine is supported by this version.");
        if (request.CanWriteRepositories && !request.CanReadRepositories)
            errors.Add("Repository write requires repository read to be enabled.");
        if (AiStoSpecializationRequest.CharacterCount(request.AvatarReference) > 500)
            errors.Add("AvatarReference cannot exceed 500 characters.");
        if (request.AiSto is not null) errors.AddRange(request.AiSto.ValidateAndNormalize());
        return errors;
    }

    private static AgentProfileRecord ToRecord(AgentProfileRequest request, Guid userId, Guid id,
        DateTimeOffset created, DateTimeOffset updated)
    {
        var record = new AgentProfileRecord { Id = id, UserId = userId, CreatedAtUtc = created, UpdatedAtUtc = updated };
        Apply(record, request);
        return record;
    }

    private static void Apply(AgentProfileRecord record, AgentProfileRequest request)
    {
        record.Name = request.Name.Trim();
        record.Description = (request.Description ?? string.Empty).Trim();
        record.AvatarReference = request.AvatarReference;
        record.Engine = "codex";
        record.CanReadRepositories = request.CanReadRepositories;
        record.CanWriteRepositories = request.CanWriteRepositories;
        record.CanBrowseWeb = request.CanBrowseWeb;
        record.UseSemanticNavigation = request.UseSemanticNavigation;
        record.PreferUpdatedDocumentation = request.PreferUpdatedDocumentation;
        record.ApplyArchitectureRules = request.ApplyArchitectureRules;
        record.AiStoConfiguration = request.AiSto is null
            ? null
            : JsonSerializer.Serialize(request.AiSto, JsonOptions.Default);
    }
}

public sealed class AgentValidationException(IReadOnlyList<string> errors) : Exception("The agent profile is invalid.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
