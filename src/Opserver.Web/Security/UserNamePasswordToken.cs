namespace Opserver.Security;

public class UserNamePasswordToken(string userName, string password) : ISecurityProviderToken
{
    public string UserName { get; } = userName;
    public string Password { get; } = password;
}
