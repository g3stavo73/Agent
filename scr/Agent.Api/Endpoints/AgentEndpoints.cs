using Agent.Api.Contracts;
using Agent.Api.Services;

namespace Agent.Api.Endpoints;

public static class AgentEndpoints
{
    public static RouteGroupBuilder MapAgentEndpoints(this RouteGroupBuilder api)
    {
        var agents = api.MapGroup("/agents").WithTags("Agents");
        agents.MapGet("/", async (AgentProfileService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)))
            .WithName("ListAgents");

        agents.MapPost("/", async Task<IResult> (AgentProfileRequest request, AgentProfileService service, CancellationToken ct) =>
        {
            try
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"/api/v1/agents/{created.Id}", created);
            }
            catch (AgentValidationException ex) { return Results.ValidationProblem(ToErrors(ex)); }
        }).WithName("CreateAgent");

        agents.MapPut("/{id:guid}", async Task<IResult> (Guid id, AgentProfileRequest request,
            AgentProfileService service, CancellationToken ct) =>
        {
            try
            {
                var updated = await service.UpdateAsync(id, request, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (AgentValidationException ex) { return Results.ValidationProblem(ToErrors(ex)); }
        }).WithName("UpdateAgent");

        agents.MapDelete("/{id:guid}", async Task<IResult> (Guid id, AgentProfileService service, CancellationToken ct) =>
            await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteAgent");

        return api;
    }

    private static Dictionary<string, string[]> ToErrors(AgentValidationException exception) =>
        exception.Errors.Select((message, index) => (message, index))
            .ToDictionary(x => $"validation_{x.index + 1}", x => new[] { x.message });
}
