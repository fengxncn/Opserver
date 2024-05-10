using System.Security.Claims;
using Opserver.Models;

namespace Opserver.Security;

/// <summary>
/// Does this REALLY need an explanation?
/// </summary>
public class EveryonesReadOnlyProvider(SecuritySettings settings) : SecurityProvider<SecuritySettings, UserNamePasswordToken>(settings)
{
    public override string ProviderName => "Everyone's Read-only";
    public override SecurityProviderFlowType FlowType => SecurityProviderFlowType.Username;

    internal override bool InAdminGroups(User user, StatusModule module) => false;
    internal override bool InReadGroups(User user, StatusModule module) => true;
    protected override bool TryValidateToken(UserNamePasswordToken token, out ClaimsPrincipal claimsPrincipal)
    {
        claimsPrincipal = CreateNamedPrincipal(token.UserName);
        return true;
    }
}
