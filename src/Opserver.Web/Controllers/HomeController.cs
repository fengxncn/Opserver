using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Opserver.Data;
using Opserver.Data.Dashboard;
using Opserver.Data.Elastic;
using Opserver.Data.Exceptions;
using Opserver.Data.HAProxy;
using Opserver.Data.Redis;
using Opserver.Data.SQL;
using Opserver.Helpers;
using Opserver.Models;
using Opserver.Views.Home;
using Opserver.Views.Shared;

namespace Opserver.Controllers;

[OnlyAllow(Roles.Authenticated)]
public class HomeController(
    IOptions<OpserverSettings> _settings,
    PollingService poller,
    IEnumerable<StatusModule> modules,
    DashboardModule dashboard,
    SQLModule sql,
    RedisModule redis,
    ElasticModule elastic,
    ExceptionsModule exceptions,
    HAProxyModule haproxy) : StatusController(_settings)
{
    private PollingService Poller { get; } = poller;
    private IEnumerable<StatusModule> Modules { get; } = modules;
    private DashboardModule Dashboard { get; } = dashboard;
    private SQLModule Sql { get; } = sql;
    private RedisModule Redis { get; } = redis;
    private ElasticModule Elastic { get; } = elastic;
    private ExceptionsModule Exceptions { get; } = exceptions;
    private HAProxyModule HAProxy { get; } = haproxy;

    [DefaultRoute("")]
    public ActionResult Home()
    {
        // TODO: Order
        //foreach (var m in Modules)
        //{
        //    if (m.Enabled && m.SecuritySettings)
        //        return RedirectToAction()...
        //}

        static bool AllowMeMaybe(StatusModule m) => m.Enabled && Current.User.HasAccess(m);

        if (AllowMeMaybe(Dashboard))
            return RedirectToAction(nameof(DashboardController.Dashboard), "Dashboard");
        if (AllowMeMaybe(Sql))
            return RedirectToAction(nameof(SQLController.Dashboard), "SQL");
        if (AllowMeMaybe(Redis))
            return RedirectToAction(nameof(RedisController.Dashboard), "Redis");
        if (AllowMeMaybe(Elastic))
            return RedirectToAction(nameof(ElasticController.Dashboard), "Elastic");
        if (AllowMeMaybe(Exceptions))
            return RedirectToAction(nameof(ExceptionsController.Exceptions), "Exceptions");
        if (AllowMeMaybe(HAProxy))
            return RedirectToAction(nameof(HAProxyController.Dashboard), "HAProxy");

        return View("NoConfiguration");
    }

    [Route("ping"), HttpGet, HttpHead, AllowAnonymous, AlsoAllow(Roles.InternalRequest)]
    public ActionResult Ping()
    {
        return Ok();
    }

    [Route("top-refresh")]
    public ActionResult TopRefresh(string tab)
    {
        Current.NavTab = NavTab.GetByName(tab);

        var vd = new TopRefreshModel
        {
            Tab = tab
        };
        return PartialView(vd);
    }

    [Route("issues")]
    public ActionResult Issues() => PartialView();

    [Route("about"), AlsoAllow(Roles.InternalRequest)]
    public ActionResult About() => View();

    [Route("about/caches"), AlsoAllow(Roles.InternalRequest)]
    public ActionResult AboutCaches(string filter, bool refresh = true)
    {
        var vd = new AboutModel
        {
            AutoRefresh = refresh,
            Filter = filter
        };
        return View("About.Caches", vd);
    }

    [Route("set-theme"), HttpPost]
    public ActionResult SetTheme(string theme)
    {
        Theme.Set(theme, Response);
        return RedirectToAction(nameof(About));
    }

    [Route("debug"), AllowAnonymous]
    public ActionResult Debug()
    {
        var sb = StringBuilderCache.Get()
            .AppendLine("Request Info")
            .Append("  IP: ").AppendLine(Current.RequestIP)
            .Append("  User: ").AppendLine(Current.User.AccountName)
            .Append("  Roles: ").AppendLine(Current.User.Roles.ToString())
            .AppendLine()
            .AppendLine("Headers");
        foreach (string k in Request.Headers.Keys)
        {
            sb.AppendFormat("  {0}: {1}\n", k, Request.Headers[k]);
        }

        var ps = Poller.GetPollingStatus();
        sb.AppendLine()
          .AppendLine("Polling Info")
          .AppendLine(ps.GetPropertyNamesAndValues(prefix: "  "));
        return TextPlain(sb.ToStringRecycle());
    }

    [Route("error-test")]
    public ActionResult ErrorTestPage()
    {
        Current.LogException(new Exception("Test Exception via GlobalApplication.LogException()"));
        throw new NotImplementedException("I AM IMPLEMENTED, I WAS BORN TO THROW ERRORS!");
    }
}
