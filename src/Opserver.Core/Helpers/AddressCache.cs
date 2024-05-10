using System.Net;
using Microsoft.Extensions.Caching.Memory;

namespace Opserver.Helpers;

public class AddressCache(IMemoryCache cache)
{
    public IMemoryCache MemCache { get; } = cache;

    public List<IPAddress> GetHostAddresses(string hostOrIp)
    {
        return MemCache.GetSet<List<IPAddress>>("host-ip-cache-" + hostOrIp, (_, __) =>
            {
                try
                {
                    return IPAddress.TryParse(hostOrIp, out var ip)
                               ? [ip]
                               : Dns.GetHostAddresses(hostOrIp).ToList();
                }
                catch
                {
                    return [];
                }
            }, 10.Hours(), 24.Hours());
    }

    public string GetHostName(string ip)
    {
        return MemCache.GetSet<string>("host-name-cache-" + ip, (_, __) =>
        {
            try
            {
                var entry = Dns.GetHostEntry(ip);
                return entry != null ? entry.HostName.Split(StringSplits.Period)[0] : "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }, 10.Minutes(), 24.Hours());
    }
}
