using Agent.Mobile.Models;

namespace Agent.Mobile.Services;

public interface IAgentProfileStore
{
    Task<List<AgentProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyCollection<AgentProfile> profiles, CancellationToken cancellationToken = default);
    Task<string> SaveAvatarAsync(Stream image, string fileName, string? contentType, CancellationToken cancellationToken = default);
    void DeleteAvatar(string? path);
}
