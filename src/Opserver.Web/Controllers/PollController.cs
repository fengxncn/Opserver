using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Opserver.Data;
using Opserver.Helpers;
using Opserver.Models;

namespace Opserver.Controllers;

[OnlyAllow(Roles.Authenticated)]
public class PollController(IOptions<OpserverSettings> _settings, PollingService poller) : StatusController(_settings)
{
    private PollingService Poller { get; } = poller;

    [Route("poll")]
    public async Task<ActionResult> PollNodes(string type, string[] key, Guid? guid = null)
    {
        if (type.IsNullOrEmpty())
            return JsonError("type is missing");
        if (!(key?.Length > 0))
            return JsonError("key is missing");
        try
        {
            var polls = key.Select(k => Poller.PollAsync(type, k, guid));
            var results = await Task.WhenAll(polls);
            return Json(results.Aggregate(true, (current, r) => current && r));
        }
        catch (Exception e)
        {
            return JsonError("Error polling node: " + e.Message);
        }
    }
}
