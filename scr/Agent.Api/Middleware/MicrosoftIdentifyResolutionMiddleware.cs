using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Agent.Api.Auth;
using Agent.Api.Services;

namespace Agent.Api.Middleware;

public sealed class MicrosoftIdentityResolutionMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context, MicrosoftIdentityResolver resolver, CurrentUserContext currentUser)
    {
        // Health and other AllowAnonymous routes must not trigger account creation
        // or a database lookup merely because a caller supplied a bearer token.
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
            context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var requiredScope = configuration[$"{MicrosoftIdentityOptions.SectionName}:RequiredScope"] ?? "Agent.Access";
            var scopes = context.User.FindFirstValue("scp")?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                ?? Array.Empty<string>();
            if (!scopes.Contains(requiredScope, StringComparer.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var issuer = context.User.FindFirstValue("iss");
            var subject = context.User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) ||
                issuer.Length > 500 || subject.Length > 500)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var email = context.User.FindFirstValue("email");
            var userId = await resolver.ResolveAsync(issuer, subject, email, context.RequestAborted);
            if (userId is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            currentUser.Set(userId.Value, issuer, subject,
                context.User.FindFirstValue("name"), email);
        }

        await next(context);
    }
}
