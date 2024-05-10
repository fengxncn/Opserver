﻿using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Opserver.Data;
using Opserver.Models;

namespace Opserver.Security;

public abstract class SecurityProvider
{
    // The theory here is that there won't be that many unique combinations here.
    private readonly ConcurrentDictionary<string, string[]> _parsedGroups = new();

    /// <summary>
    /// Gets the name of this provider.
    /// </summary>
    public abstract string ProviderName { get; }
    /// <summary>
    /// Gets a <see cref="SecurityProviderFlowType"/> value indicating how to handle the providers
    /// authentication flow.
    /// </summary>
    public abstract SecurityProviderFlowType FlowType { get; }
    /// <summary>
    /// Gets a <see cref="List{T}"/> of <see cref="IPNet"/> objects representing
    /// the networks that are allowed to access this instance.
    /// </summary>
    public List<IPNet> InternalNetworks { get; }
    /// <summary>
    /// Gets a value indicating whether the provider is currently configured.
    /// </summary>
    public virtual bool IsConfigured => true;
    /// <summary>
    /// Gets the <see cref="SecuritySettings"/> used by this instance.
    /// </summary>
    public SecuritySettings Settings { get; }

    protected SecurityProvider(SecuritySettings settings)
    {
        Settings = settings;
        InternalNetworks = [];
        if (settings.InternalNetworks != null)
        {
            foreach (var n in settings.InternalNetworks)
            {
                if ((n.CIDR.HasValue() || (n.IP.HasValue() && n.Subnet.IsNullOrEmpty())) && IPNet.TryParse(n.CIDR.IsNullOrEmptyReturn(n.IP), out var net))
                {
                    InternalNetworks.Add(net);
                }
                else if (n.IP.HasValue() && n.Subnet.HasValue() && IPNet.TryParse(n.IP, n.Subnet, out net))
                {
                    InternalNetworks.Add(net);
                }
            }
        }
    }

    public bool InGroups(User user, string[] groupNames)
    {
        if (groupNames.Length == 0) return false;
        if (groupNames.Any(g => g == "*")) return true;
        return InGroupsCore(user, groupNames);
    }

    protected virtual bool InGroupsCore(User user, string[] groupNames) => false;

    public abstract bool TryValidateToken(ISecurityProviderToken token, out ClaimsPrincipal claimsPrincipal);

    public virtual void PurgeCache() { }

    internal virtual bool InReadGroups(User user, StatusModule module)
    {
        var settings = module.SecuritySettings;
        // User has direct access, global access, or is an admin
        return settings != null && (InGroups(user, Parse(settings.ViewGroups))
                                    || InGroups(user, Parse(Settings.ViewEverythingGroups))
                                    || InAdminGroups(user, module));
    }

    public bool IsGlobalAdmin(User user) => InGroups(user, Parse(Settings.AdminEverythingGroups));

    public bool IsInternalIP(string ip) =>
        IPAddress.TryParse(ip, out var addr) && InternalNetworks.Any(n => n.Contains(addr));

    /// <summary>
    /// Creates a <see cref="ClaimsPrincipal"/> representing an anonymous user.
    /// </summary>
    protected ClaimsPrincipal CreateAnonymousPrincipal() => new(new ClaimsIdentity());

    /// <summary>
    /// Creates a <see cref="ClaimsPrincipal"/> for the given username.
    /// </summary>
    protected ClaimsPrincipal CreateNamedPrincipal(string userName)
    {
        List<Claim> claims = [new(ClaimTypes.Name, userName)];
        var identity = new ClaimsIdentity(claims, "login");
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Checks if a request's API key is valid.
    /// </summary>
    /// <remarks>
    /// Tons to do here: roles, multiple keys, encryption, etc.
    /// </remarks>
    internal bool IsValidApiKey(StringValues? key) =>
        Settings.ApiKey.HasValue() && string.Equals(Settings.ApiKey, key);

    internal virtual bool InAdminGroups(User user, StatusModule module)
    {
        var settings = module.SecuritySettings;
        // User either has direct access or via global
        return settings != null && (InGroups(user, Parse(settings.AdminGroups)) ||
                                    InGroups(user, Parse(Settings.AdminEverythingGroups)));
    }

    private string[] Parse(string groups) =>
        groups.IsNullOrEmpty()
            ? []
            : _parsedGroups.GetOrAdd(groups, groups => groups?.Split(StringSplits.Comma_SemiColon) ?? []);
}

public abstract class SecurityProvider<TSettings, TToken>(TSettings settings) : SecurityProvider(settings) where TSettings : SecuritySettings
{
    public new TSettings Settings => (TSettings)base.Settings;

    protected abstract bool TryValidateToken(TToken token, out ClaimsPrincipal claimsPrincipal);

    public sealed override bool TryValidateToken(ISecurityProviderToken token, out ClaimsPrincipal claimsPrincipal)
    {
        if (token is not TToken typedToken)
        {
            claimsPrincipal = CreateAnonymousPrincipal();
            return false;
        }

        return TryValidateToken(typedToken, out claimsPrincipal);
    }
}
