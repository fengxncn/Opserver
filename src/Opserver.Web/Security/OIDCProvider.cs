using System.Security.Claims;
using Opserver.Models;

namespace Opserver.Security;

/// <summary>
/// <see cref="SecurityProvider"/> that delegates login to an OIDC login flow.
/// </summary>
public class OIDCProvider(OIDCSecuritySettings settings) : SecurityProvider<OIDCSecuritySettings, OIDCToken>(settings)
{
    public const string GroupsClaimType = "groups";
    public override string ProviderName => "OpenID Connect";
    public override SecurityProviderFlowType FlowType => SecurityProviderFlowType.OIDC;

    protected override bool TryValidateToken(OIDCToken token, out ClaimsPrincipal claimsPrincipal)
    {
        // extract the claims we care about
        var claimsToAdd = new List<Claim>();
        foreach (var claim in token.Claims)
        {
            if (string.Equals(claim.Type, Settings.NameClaim, StringComparison.OrdinalIgnoreCase))
            {
                claimsToAdd.Add(new Claim(ClaimTypes.Name, claim.Value));
            }
            else if (string.Equals(claim.Type, Settings.GroupsClaim, StringComparison.OrdinalIgnoreCase))
            {
                claimsToAdd.Add(new Claim(GroupsClaimType, claim.Value));
            }
        }
        claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claimsToAdd, "login"));
        return true;
    }

    protected override bool InGroupsCore(User user, string[] groupNames)
    {
        var groupClaims = user.Principal.FindAll(x => x.Type == GroupsClaimType);
        foreach (var groupClaim in groupClaims)
        {
            if (groupNames.Any(x => string.Equals(groupClaim.Value, x, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
