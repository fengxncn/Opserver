using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Opserver.Helpers;
using Opserver.Models;
using StackExchange.Exceptional;

namespace Opserver.Controllers;

[OnlyAllow(Roles.GlobalAdmin)]
public class AdminController(IOptions<OpserverSettings> _settings) : StatusController(_settings)
{
    [Route("admin/security/purge-cache")]
    public ActionResult Dashboard()
    {
        Current.Security.PurgeCache();
        return TextPlain("Cache Purged");
    }

    /// <summary>
    /// Access our error log.
    /// </summary>
#pragma warning disable ASP0018 // Unused route parameter
    [Route("admin/errors/{resource?}/{subResource?}"), AlsoAllow(Roles.LocalRequest)]
#pragma warning restore ASP0018 // Unused route parameter
    public Task InvokeErrorHandler() => ExceptionalMiddleware.HandleRequestAsync(HttpContext);
}
