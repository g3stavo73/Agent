using Agent.Mobile.Models;

namespace Agent.Mobile.Services;

public interface IAgentSettingsStore
{
    AgentSettings Load();
    void Save(AgentSettings settings);
    void Reset();
}
