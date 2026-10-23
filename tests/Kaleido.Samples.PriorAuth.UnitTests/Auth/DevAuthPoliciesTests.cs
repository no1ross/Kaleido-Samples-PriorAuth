using System.Security.Claims;
using Kaleido.Samples.PriorAuth.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Kaleido.Samples.PriorAuth.UnitTests.Auth;

/// <summary>The sample's authorization policies, evaluated on the principals its dev tokens produce.</summary>
public sealed class DevAuthPoliciesTests
{
    private readonly IAuthorizationService _authorization =
        new ServiceCollection().AddLogging().AddDevAuth().BuildServiceProvider().GetRequiredService<IAuthorizationService>();

    public static TheoryData<string, string, bool> Cases => new()
    {
        { "direct user", DevAuthPolicies.AuthenticatedUser, true },
        { "anonymous", DevAuthPolicies.AuthenticatedUser, false },
        { "direct user", DevAuthPolicies.InternalCaller, false },
        { "on behalf of user", DevAuthPolicies.InternalCaller, true },
        { "service", DevAuthPolicies.InternalCaller, true },
        { "direct user", DevAuthPolicies.OnBehalfOfUser, false },
        { "on behalf of user", DevAuthPolicies.OnBehalfOfUser, true },
        { "service", DevAuthPolicies.OnBehalfOfUser, false }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task APolicyAdmitsOnlyItsCallers(string caller, string policy, bool admitted)
    {
        var result = await _authorization.AuthorizeAsync(Principal(caller), policy);

        Assert.Equal(admitted, result.Succeeded);
    }

    private static ClaimsPrincipal Principal(string caller)
    {
        var token = caller switch
        {
            "direct user" => DevTokenIssuer.Issue("bob", ["radiology"], DevTokenIssuer.DefaultKey),
            "on behalf of user" => DevTokenIssuer.Issue("bob", ["radiology"], DevTokenIssuer.DefaultKey, actor: "intake"),
            "service" => DevTokenIssuer.IssueServiceToken("intake", DevTokenIssuer.DefaultKey),
            _ => null
        };

        if (token is null || !DevTokenIssuer.TryValidate(token, DevTokenIssuer.DefaultKey, out var identity) || identity is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = identity.Roles.Select(role => new Claim(ClaimTypes.Role, role))
            .Append(new Claim(ClaimTypes.Name, identity.Name))
            .Append(new Claim(DevAuthClaims.PrincipalType, identity.PrincipalType));

        if (identity.Actor is not null)
        {
            claims = claims.Append(new Claim(DevAuthClaims.Actor, identity.Actor));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, DevTokenAuthenticationHandler.SchemeName));
    }
}
