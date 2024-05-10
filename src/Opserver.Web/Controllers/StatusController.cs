﻿using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Opserver.Helpers;

namespace Opserver.Controllers;

public class StatusController<T> : StatusController where T : StatusModule
{
    public override NavTab NavTab => NavTab.Get(this);
    public override ISecurableModule SettingsModule => Module.SecuritySettings;
    protected virtual T Module { get; }

    public StatusController(T module, IOptions<OpserverSettings> settings) : base(settings)
    {
        Module = module;
        if (NavTab != null)
        {
            Current.NavTab = NavTab;
        }
    }
}

public partial class StatusController(IOptions<OpserverSettings> settings) : Controller
{
    public virtual ISecurableModule SettingsModule => null;
    public virtual NavTab NavTab => null;
    protected OpserverSettings Settings { get; } = settings.Value;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var iSettings = SettingsModule as ModuleSettings;
        if (iSettings?.Enabled == false)
        {
            // TODO: "Module isn't enabled" page?
            context.Result = Redirect("~/");
        }

        base.OnActionExecuting(context);
    }

    public void SetTitle(string title)
    {
        title = title.HtmlEncode();
        ViewData[ViewDataKeys.PageTitle] = title.IsNullOrEmpty() ? Settings.Global.SiteName : string.Concat(title, " - ", Settings.Global.SiteName);
    }

    /// <summary>
    /// returns ContentResult with the parameter 'content' as its payload and "text/plain" as media type.
    /// </summary>
    /// <param name="content">The text content to render</param>
    protected ContentResult TextPlain(string content) => new() { Content = content, ContentType = "text/plain" };

    protected ContentResult ContentNotFound(string message = null)
    {
        Response.StatusCode = (int)HttpStatusCode.NotFound;
        return Content(message ?? "404");
    }

    protected JsonResult Json(object data, System.Text.Json.JsonSerializerOptions serializerOptions = null) =>
        serializerOptions != null
        ? base.Json(data, serializerOptions)
        : base.Json(data);

    protected ActionResult JsonNotFound()
    {
        Response.StatusCode = (int)HttpStatusCode.NotFound;
        return Json(null);
    }

    protected ActionResult JsonError(string message, HttpStatusCode? status = null)
    {
        Response.StatusCode = (int)(status ?? HttpStatusCode.InternalServerError);
        return Json(new { ErrorMessage = message });
    }

    protected ActionResult JsonError<T>(T toSerialize, HttpStatusCode? status = null)
    {
        Response.StatusCode = (int)(status ?? HttpStatusCode.InternalServerError);
        return Json(toSerialize);
    }
}
