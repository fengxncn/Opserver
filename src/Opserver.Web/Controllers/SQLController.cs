﻿using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Opserver.Data.SQL;
using Opserver.Helpers;
using Opserver.Views.SQL;

namespace Opserver.Controllers;

[OnlyAllow(SQLRoles.Viewer)]
public partial class SQLController(SQLModule sqlModule, IOptions<OpserverSettings> settings) : StatusController<SQLModule>(sqlModule, settings)
{
    [DefaultRoute("sql")]
    public ActionResult Dashboard() => RedirectToAction(nameof(Servers));

    [Route("sql/servers")]
    public ActionResult Servers(string cluster, string node, string ag, bool detailOnly = false)
    {
        var vd = new ServersModel
            {
                AzureServers = Module.AzureServers,
                StandaloneInstances = Module.StandaloneInstances,
                Clusters = Module.Clusters,
                Refresh = node.HasValue() ? 10 : 5
            };


        if (cluster.HasValue())
            vd.CurrentCluster = vd.Clusters.Find(c => string.Equals(c.Name, cluster, StringComparison.OrdinalIgnoreCase));
        if (vd.CurrentCluster != null)
            vd.AvailabilityGroups = vd.CurrentCluster.GetAvailabilityGroups(node, ag).ToList();

        if (detailOnly && vd.CurrentCluster != null)
            return PartialView("Servers.ClusterDetail", vd);

        return View("Servers", vd);
    }

    [Route("sql/jobs")]
    public ActionResult AllJobs(JobSort? sort = null, SortDir? dir = null)
    {
        var vd = new ServersModel
        {
            View = SQLViews.Jobs,
            AzureServers = Module.AzureServers,
            StandaloneInstances = Module.StandaloneInstances,
            Clusters = Module.Clusters,
            Refresh = 30,
            JobSort = sort,
            SortDirection = dir
        };

        return View("AllJobs", vd);
    }

    [Route("sql/instance")]
    public ActionResult Instance(string node)
    {
        var i = Module.GetInstance(node);
        var vd = new InstanceModel
        {
            View = SQLViews.Instance,
            Refresh = node.HasValue() ? 10 : 5,
            CurrentInstance = i
        };
        return View("Instance", vd);
    }

    [Route("sql/instance/summary/{type}")]
    public ActionResult InstanceSummary(string node, string type)
    {
        var i = Module.GetInstance(node);
        if (i == null) return NoInstanceRedirect(node);

        return type switch
        {
            "configuration" => PartialView("Instance.Configuration", i),
            "connections" => PartialView("Instance.Connections", i),
            "errors" => PartialView("Instance.Errors", i),
            "memory" => PartialView("Instance.Memory", i),
            "jobs" => PartialView("Instance.Jobs", i),
            "db-files" => PartialView("Instance.DBFiles", i),
            _ => ContentNotFound("Unknown summary view requested"),
        };
    }

    [ResponseCache(Duration = 5 * 1, VaryByQueryKeys = ["node", "sort", "options"], Location = ResponseCacheLocation.Client)]
    [Route("sql/top")]
    public ActionResult Top(string node, SQLInstance.TopSearchOptions options)
    {
        var vd = GetOperationsModel(node, options);
        var i = vd.CurrentInstance;

        if (i != null)
        {
            var cache = i.GetTopOperations(options);
            vd.TopOperations = cache.Data;
            vd.ErrorMessage = cache.ErrorMessage;
        }

        return View("Operations.Top", vd);
    }

    [Route("sql/top/filters")]
    public ActionResult TopFilters(string node, SQLInstance.TopSearchOptions options) =>
        View("Operations.Top.Filters", GetOperationsModel(node, options));

    private OperationsTopModel GetOperationsModel(string node, SQLInstance.TopSearchOptions options)
    {
        var i = Module.GetInstance(node);
        options.SetDefaults();

        return new OperationsTopModel
        {
            View = SQLViews.Top,
            CurrentInstance = i,
            TopSearchOptions = options
        };
    }

    [Route("sql/top/detail")]
    public ActionResult TopDetail(string node, string handle, int? offset = null)
    {
        var planHandle = WebEncoders.Base64UrlDecode(handle);

        var i = Module.GetInstance(node);

        var vd = new OperationsTopDetailModel
        {
            Instance = i,
            Op = i.GetTopOperation(planHandle, offset).Data
        };
        return PartialView("Operations.Top.Detail", vd);
    }

    [Route("sql/top/plan")]
    public ActionResult TopPlan(string node, string handle)
    {
        var planHandle = WebEncoders.Base64UrlDecode(handle);
        var i = Module.GetInstance(node);
        var op = i.GetTopOperation(planHandle).Data;
        if (op == null) return ContentNotFound("Plan was not found.");

        var ms = new MemoryStream(Encoding.UTF8.GetBytes(op.QueryPlan));

        return File(ms, "text/xml", $"QueryPlan-{Math.Abs(handle.GetHashCode())}.sqlplan");
    }

    [Route("sql/active")]
    public ActionResult Active(string node, SQLInstance.ActiveSearchOptions options) =>
        View("Operations.Active", GetOperationsActiveModel(node, options));

    [Route("sql/active/filters")]
    public ActionResult ActiveFilters(string node, SQLInstance.ActiveSearchOptions options) =>
        View("Operations.Active.Filters", GetOperationsActiveModel(node, options));

    private OperationsActiveModel GetOperationsActiveModel(string node, SQLInstance.ActiveSearchOptions options)
    {
        var i = Module.GetInstance(node);
        return new OperationsActiveModel
        {
            View = SQLViews.Active,
            CurrentInstance = i,
            ActiveSearchOptions = options
        };
    }

    [Route("sql/connections")]
    public async Task<ActionResult> Connections(string node)
    {
        var i = Module.GetInstance(node);
        var vd = new DashboardModel
        {
            View = SQLViews.Connections,
            CurrentInstance = i,
            Cache = i?.Connections,
            Connections = i == null ? null : await i.Connections.GetData()
        };
        return View(vd);
    }

    [Route("sql/databases")]
    public ActionResult Databases(string node)
    {
        var i = Module.GetInstance(node);
        var vd = new DashboardModel
        {
            View = SQLViews.Databases,
            CurrentInstance = i,
            Refresh = 2*60
        };
        return View(vd);
    }

    [Route("sql/db/{database}/{view}")]
    [Route("sql/db/{database}/{view}/{objectName}")]
    public ActionResult DatabaseDetail(string node, string database, string view, string objectName)
    {
        var i = Module.GetInstance(node);
        var vd = new DatabasesModel
        {
            Instance = i,
            Database = database,
            ObjectName = objectName
        };
        switch (view)
        {
            case "backups":
                vd.View = DatabasesModel.Views.Backups;
                return View("Databases.Modal.Backups", vd);
            case "restores":
                vd.View = DatabasesModel.Views.Restores;
                return View("Databases.Modal.Restores", vd);
            case "storage":
                vd.View = DatabasesModel.Views.Storage;
                return View("Databases.Modal.Storage", vd);
            case "tables":
                vd.View = DatabasesModel.Views.Tables;
                return View("Databases.Modal.Tables", vd);
            case "views":
                vd.View = DatabasesModel.Views.Views;
                return View("Databases.Modal.Views", vd);
            case "unusedindexes":
                vd.View = DatabasesModel.Views.UnusedIndexes;
                return View("Databases.Modal.UnusedIndexes", vd);
            case "missingindexes":
                vd.View = DatabasesModel.Views.MissingIndexes;
                return View("Databases.Modal.MissingIndexes", vd);
            case "storedprocedures":
                vd.View = DatabasesModel.Views.StoredProcedures;
                return View("Databases.Modal.StoredProcedures", vd);
        }
        return View("Databases.Modal.Tables", vd);
    }

    [Route("sql/databases/tables")]
    public ActionResult DatabaseTables(string node, string database)
    {
        var i = Module.GetInstance(node);
        var vd = new DatabasesModel
        {
            Instance = i,
            Database = database
        };
        return View("Databases.Modal.Tables", vd);
    }

    private ActionResult NoInstanceRedirect(string node) =>
        Request.IsAjax()
        ? ContentNotFound("Instance " + node + " was not found.")
        : View("Instance.Selector", new DashboardModel());
}
