using System.Security.Claims;

namespace Opserver.Security;

/// <summary>
/// Used when no valid configuration is present, as a default to slam the door
/// If people want read-all access...it should be explicit. Default is no access.
/// </summary>
public class UnconfiguredProvider(SecuritySettings settings) : SecurityProvider(settings)
{
    public override string ProviderName => "Unconfigured";
    public override SecurityProviderFlowType FlowType => SecurityProviderFlowType.None;
    public override bool IsConfigured => false;

    public override bool TryValidateToken(ISecurityProviderToken token, out ClaimsPrincipal claimsPrincipal)
    {
        claimsPrincipal = CreateAnonymousPrincipal();
        return false;
    }
}
