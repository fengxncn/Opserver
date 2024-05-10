using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Opserver.Helpers;
using Opserver.Models;
using Opserver.Views.Shared;

namespace Opserver.Controllers;

[AlsoAllow(Roles.Anonymous)]
public class MiscController(IOptions<OpserverSettings> _settings) : StatusController(_settings)
{
    [Route("no-config")]
    public ViewResult NoConfig() => View("NoConfiguration");

    [Route("404")]
    public ViewResult PageNotFound(string title = null, string message = null)
    {
        Response.StatusCode = (int)HttpStatusCode.NotFound;

        var vd = new PageNotFoundModel
        {
            Title = title,
            Message = message
        };
        return View("PageNotFound", vd);
    }

    [AllowAnonymous]
    [Route("denied")]
    public ActionResult AccessDenied()
    {
        Response.StatusCode = (int)HttpStatusCode.Forbidden;
        return View("~/Views/Shared/AccessDenied.cshtml");
    }

    [Route("error")]
    public ActionResult ErrorPage()
    {
        Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        return View("Error");
    }
}
