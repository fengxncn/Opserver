using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Opserver.Data.Cloudflare;

public partial class CloudflareAPI
{
    public Task<bool> PurgeFileAsync(string url)
    {
        var zone = GetZoneFromUrl(url);
        if (zone == null) return Task.FromResult(false);

        var otherUrl = url.StartsWith("http:")
            ? ReplaceHttpRegex().Replace(url, "https:")
            : ReplaceHttpsRegex().Replace(url, "http:");

        return Module.PurgeFilesAsync(zone, [url, otherUrl]);
    }

    [GeneratedRegex("^http:")]
    private static partial Regex ReplaceHttpRegex();
    [GeneratedRegex("^https:")]
    private static partial Regex ReplaceHttpsRegex();
}
