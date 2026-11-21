using Agent.Api.Contracts;
using Agent.Api.Services;

namespace Agent.Api.Endpoints;

public static class AiStoEndpoints
{
    public static RouteGroupBuilder MapAiStoEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/aisto/compile", async Task<IResult> (AiStoCompileRequest request,
            IAiStoCompiler compiler, CancellationToken ct) =>
        {
            if (request.Configuration is null || request.Capabilities is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["Configuration and capabilities are required."]
                });

            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(request.AgentName) || AiStoSpecializationRequest.CharacterCount(request.AgentName.Trim()) > 80)
                errors.Add("AgentName must contain between 1 and 80 characters.");
            errors.AddRange(request.Configuration.ValidateAndNormalize());
            if (request.Capabilities.CanWriteRepositories && !request.Capabilities.CanReadRepositories)
                errors.Add("Repository write requires repository read to be enabled.");
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors.Select((x, i) => (x, i))
                    .ToDictionary(x => $"configuration_{x.i + 1}", x => new[] { x.x }));
            }

            try
            {
                var result = await compiler.CompileAsync(request, ct);
                return Results.Ok(result);
            }
            catch (AiStoWorkerTimeoutException ex)
            {
                return Results.Problem(title: "AiSTo compilation timed out", detail: ex.Message,
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (AiStoWorkerUnavailableException ex)
            {
                return Results.Problem(title: "AiSTo compiler unavailable", detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (AiStoWorkerException ex)
            {
                return Results.Problem(title: "AiSTo compilation failed", detail: ex.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .WithName("CompileAiStoContext")
        .WithDescription("Validates and assembles an AiSTo context preview. It does not execute a Codex turn.");

        return api;
    }
}
