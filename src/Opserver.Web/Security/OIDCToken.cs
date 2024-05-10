using System.Security.Claims;

namespace Opserver.Security;

/// <summary>
/// <see cref="ISecurityProviderToken"/> that wraps the claims retrieved from an OpenId Connect login flow.
/// </summary>
public class OIDCToken(IEnumerable<Claim> claims) : ISecurityProviderToken
{

    /// <summary>
    /// Gets the claims retrieved as a result of an OpenId Connect login flow.
    /// </summary>
    public IEnumerable<Claim> Claims { get; } = claims ?? throw new ArgumentNullException(nameof(claims));
}
