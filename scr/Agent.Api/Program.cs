using System.Threading.RateLimiting;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Agent.Api.Auth;
using Agent.Api.Data;
using Agent.Api.Endpoints;
using Agent.Api.Middleware;
using Agent.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 128 * 1024);
var databaseConnectionString = builder.Configuration.GetConnectionString("AgentDatabase");
if (string.IsNullOrWhiteSpace(databaseConnectionString))
    throw new InvalidOperationException("ConnectionStrings:AgentDatabase is required.");

builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserContext>();
builder.Services.AddScoped<MicrosoftIdentityResolver>();
builder.Services.AddScoped<UserDataSession>();
builder.Services.AddScoped<AgentProfileService>();
builder.Services.AddSingleton<IAiStoCompiler, RustAiStoCompiler>();
builder.Services.AddDbContext<AgentDbContext>(options =>
    options.UseNpgsql(databaseConnectionString));

var microsoft = builder.Configuration.GetSection(MicrosoftIdentityOptions.SectionName)
    .Get<MicrosoftIdentityOptions>() ?? new MicrosoftIdentityOptions();
if (string.IsNullOrWhiteSpace(microsoft.ApiAudience))
    throw new InvalidOperationException("Configuration MicrosoftIdentity:ApiAudience is required.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://login.microsoftonline.com/common/v2.0";
        options.Audience = microsoft.ApiAudience;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "name",
            ClockSkew = TimeSpan.FromMinutes(2),
            IssuerValidator = MicrosoftIssuerValidator.Validate
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api", context =>
    {
        // Authentication has already validated the JWT. Use its stable external
        // identity to partition limits without querying PostgreSQL first.
        var issuer = context.User.FindFirstValue("iss");
        var subject = context.User.FindFirstValue("sub");
        var partitionSource = context.User.Identity?.IsAuthenticated == true &&
                              !string.IsNullOrWhiteSpace(issuer) &&
                              !string.IsNullOrWhiteSpace(subject)
            ? $"user:{issuer}:{subject}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        var partition = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(partitionSource)));
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});
builder.Services.AddHealthChecks().AddCheck<AgentDatabaseHealthCheck>("postgresql");

var app = builder.Build();
app.UseExceptionHandler();
app.UseRouting();
app.UseAuthentication();
// Endpoint rate limits run before the identity resolver touches PostgreSQL.
app.UseRateLimiter();
app.UseMiddleware<MicrosoftIdentityResolutionMiddleware>();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "Agent.Api" }))
    .AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

var api = app.MapGroup("/api/v1").RequireAuthorization().RequireRateLimiting("api");
api.MapGet("/me", (CurrentUserContext user) => Results.Ok(new
{
    user.Id,
    user.Issuer,
    user.Subject,
    user.DisplayName,
    user.Email
}));
api.MapAgentEndpoints();
api.MapAiStoEndpoints();

app.Run();

public partial class Program { }
