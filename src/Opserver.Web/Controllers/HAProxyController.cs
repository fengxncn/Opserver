﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Opserver.Data.HAProxy;
using Opserver.Helpers;
using Opserver.Views.HAProxy;

namespace Opserver.Controllers;

[OnlyAllow(HAProxyRoles.Viewer)]
public partial class HAProxyController(HAProxyModule module, IOptions<OpserverSettings> settings) : StatusController<HAProxyModule>(module, settings)
{
    [DefaultRoute("haproxy")]
    public ActionResult Dashboard(string group, string node, string watch = null, bool norefresh = false)
    {
        var haGroup = Module.GetGroup(group ?? node);
        var proxies = haGroup != null ? haGroup.GetProxies() : Module.GetAllProxies();
        proxies.RemoveAll(p => !p.HasServers);

        var vd = new HAProxyModel
        {
            SelectedGroup = haGroup,
            Groups = haGroup != null ? [haGroup] : Module.Groups,
            Proxies = proxies,
            View = HAProxyModel.Views.Dashboard,
            Refresh = !norefresh,
            WatchProxy = watch
        };
        return View("HAProxy.Dashboard", vd);
    }
}
