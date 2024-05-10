﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Opserver.Data.PagerDuty;
using Opserver.Helpers;
using Opserver.Views.PagerDuty;

namespace Opserver.Controllers;

[OnlyAllow(PagerDutyRoles.Viewer)]
public partial class PagerDutyController(PagerDutyModule module, IOptions<OpserverSettings> settings) : StatusController<PagerDutyModule>(module, settings)
{
    public PagerDutyPerson CurrentPagerDutyPerson
    {
        get
        {
            var currentAccount = Current.User.AccountName;
            var allUsers = Module.API.AllUsers.SafeData(true);
            var pdMap = Module.API.Settings.UserNameMap.Find(un => un.OpServerName == currentAccount);
            return pdMap != null
                ? allUsers.Find(u => u.EmailUserName == pdMap.EmailUser)
                : allUsers.Find(u => string.Equals(u.EmailUserName, currentAccount, StringComparison.OrdinalIgnoreCase));
        }
    }

    [DefaultRoute("pagerduty")]
    public async Task<ActionResult> Dashboard()
    {
        var api = Module.API;
        await api.PollAsync();

        var vd = new PagerDutyModel
        {
            Schedule = api.GetOnCall(),
            CachedDays = api.Settings.DaysToCache,
            AllIncidents = api.Incidents.SafeData(true),
            CurrentPagerDutyPerson = CurrentPagerDutyPerson
        };
        return View("PagerDuty", vd);
    }

    [Route("pagerduty/incident/detail/{id}")]
    public async Task<ActionResult> IncidentDetail(int id)
    {
        var incident = Module.API.Incidents.Data.First(i => i.Number == id);
        var vd = new PagerDutyIncidentModel
        {
            Incident = incident,
            Logs = await incident.GetLogsAsync()
        };

        return PartialView("PagerDuty.Incident", vd);
    }

    [Route("pagerduty/escalation/full")]
    public ActionResult FullEscalation() => PartialView("PagerDuty.EscFull", Module.API.OnCallInfo.Data);
}
