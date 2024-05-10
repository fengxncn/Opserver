using System.Security.Claims;
using Opserver.Models;

namespace Opserver.Security;

/// <summary>
/// Does this REALLY need an explanation?
/// </summary>
public class EveryonesAnAdminProvider(SecuritySettings settings) : SecurityProvider<SecuritySettings, UserNamePasswordToken>(settings)
{
    public override string ProviderName => "Everyone's an Admin!";
    public override SecurityProviderFlowType FlowType => SecurityProviderFlowType.Username;

    internal override bool InAdminGroups(User user, StatusModule module) => true;
    protected override bool InGroupsCore(User user, string[] groupNames) => true;

    protected override bool TryValidateToken(UserNamePasswordToken token, out ClaimsPrincipal claimsPrincipal)
    {
        claimsPrincipal = CreateNamedPrincipal(token.UserName);
        return true;
    }
}
