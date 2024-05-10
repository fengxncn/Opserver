﻿using System.Runtime.Serialization;
using Jil;

namespace Opserver.Data.PagerDuty;

public partial class PagerDutyAPI
{
    public async Task<Incident> UpdateIncidentStatusAsync(string incidentId, PagerDutyPerson person, IncidentStatus newStatus)
    {
        ArgumentNullException.ThrowIfNull(person);
        var data = new
        {
            incident = new
            {
                type = "incident_reference",
                status = newStatus.ToString()
            }
        };

        var headers = new Dictionary<string, string>
        {
            ["From"] = person.Email
        };
        try
        {
            var result = await GetFromPagerDutyAsync($"incidents/{incidentId}",
            response => JSON.Deserialize<PagerDutyIncidentUpdateResp>(response, JilOptions),
            httpMethod: HttpMethod.Put,
            data: data,
            extraHeaders: headers);
            await Incidents.PollAsync(true);

            return result?.Response ?? new Incident();
        }
        catch (DeserializationException de)
        {
            de.AddLoggedData("Message", de.Message)
              .AddLoggedData("Snippet After", de.SnippetAfterError)
              .Log();
            return null;
        }
    }

    public class PagerDutyIncidentUpdateResp
    {
        [DataMember(Name = "incident")]
        public Incident Response { get; set; }
    }
}
