using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using System.IdentityModel.Tokens.Jwt;

namespace Agent.Api.Auth;

/// <summary>
/// Restricts the common Microsoft identity metadata endpoint to tenant-specific
/// v2 issuers. The JWT signature is still validated against Microsoft's OIDC keys.
/// </summary>
public static class MicrosoftIssuerValidator
{
    private const string TrustedHost = "login.microsoftonline.com";

    public static string Validate(string issuer, SecurityToken token, TokenValidationParameters parameters)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, TrustedHost, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new SecurityTokenInvalidIssuerException("The token issuer is not a trusted Microsoft identity issuer.");

        var path = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var tenantId = token switch
        {
            JsonWebToken json => json.Claims.FirstOrDefault(c => c.Type == "tid")?.Value,
            JwtSecurityToken jwt => jwt.Claims.FirstOrDefault(c => c.Type == "tid")?.Value,
            _ => null
        };

        if (path.Length != 2 || !string.Equals(path[1], "v2.0", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(path[0], out var pathTenant) ||
            !Guid.TryParse(tenantId, out var claimTenant) || pathTenant != claimTenant)
            throw new SecurityTokenInvalidIssuerException("The Microsoft tenant issuer does not match the tid claim.");

        return issuer;
    }
}
