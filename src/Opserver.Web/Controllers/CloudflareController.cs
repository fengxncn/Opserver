﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Opserver.Data.Cloudflare;
using Opserver.Helpers;
using Opserver.Views.Cloudflare;

namespace Opserver.Controllers;

[OnlyAllow(CloudflareRoles.Viewer)]
public class CloudflareController(CloudflareModule module, IOptions<OpserverSettings> settings) : StatusController<CloudflareModule>(module, settings)
{
    [DefaultRoute("cloudflare")]
    public ActionResult Dashboard() => RedirectToAction(nameof(DNS));

    [Route("cloudflare/dns")]
    public async Task<ActionResult> DNS()
    {
        await Module.API.PollAsync();
        var vd = new DNSModel
        {
            View = DashboardModel.Views.DNS,
            Zones = Module.API.Zones.SafeData(true),
            DNSRecords = Module.API.DNSRecords.Data,
            DataCenters = Module.AllDatacenters
        };
        return View(vd);
    }
}
