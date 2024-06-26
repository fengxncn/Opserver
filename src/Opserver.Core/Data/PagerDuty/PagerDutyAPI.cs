﻿using System.Net;
using System.Runtime.CompilerServices;
using Jil;
using Microsoft.Net.Http.Headers;
using StackExchange.Profiling;
using StackExchange.Utils;

namespace Opserver.Data.PagerDuty;

public partial class PagerDutyAPI(PagerDutyModule module) : PollNode<PagerDutyModule>(module, nameof(PagerDutyAPI))
{
    internal static readonly Options JilOptions = new(
        dateFormat: DateTimeFormat.ISO8601,
        unspecifiedDateTimeKindBehavior: UnspecifiedDateTimeKindBehavior.IsUTC,
        excludeNulls: true
        );

    public PagerDutySettings Settings => Module.Settings;
    public override string NodeType => nameof(PagerDutyAPI);
    public override int MinSecondsBetweenPolls => 3600;

    protected override IEnumerable<MonitorStatus> GetMonitorStatus()
    {
        if (OnCallInfo.ContainsData)
        {
            foreach (var a in OnCallInfo.Data)
                yield return a.MonitorStatus;
        }
        if (Incidents.ContainsData)
        {
            foreach (var i in Incidents.Data)
                yield return i.MonitorStatus;
        }
        yield return MonitorStatus.Good;
    }

    protected override string GetMonitorStatusReason() => "";
    public string APIKey => Settings.APIKey;

    public override IEnumerable<Cache> DataPollers
    {
        get
        {
            yield return AllUsers;
            yield return OnCallInfo;
            yield return Incidents;
            yield return AllSchedules;
            yield return AllUsers;
        }
    }

    private Cache<T> GetPagerDutyCache<T>(
        TimeSpan cacheDuration,
        Func<Task<T>> get,
        bool logExceptions = true,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0
        ) where T : class
    {
        return new Cache<T>(this, "PagerDuty - API: " + memberName,
            cacheDuration,
            get,
            logExceptions: logExceptions,
            memberName: memberName,
            sourceFilePath: sourceFilePath,
            sourceLineNumber: sourceLineNumber
        );
    }

    /// <summary>
    /// Gets content from the PagerDuty API.
    /// </summary>
    /// <typeparam name="T">The Type to return (deserialized from the returned JSON).</typeparam>
    /// <param name="path">The path to return, including any query string.</param>
    /// <param name="getFromJson">The deserialize function.</param>
    /// <param name="httpMethod">The HTTP method to use for this API call.</param>
    /// <param name="data">Data to serialize for the request.</param>
    /// <param name="extraHeaders">Headers to add to the API request.</param>
    /// <returns>The deserialized content from the PagerDuty API.</returns>
    public async Task<T> GetFromPagerDutyAsync<T>(string path, Func<string, T> getFromJson, HttpMethod httpMethod = null, object data = null, Dictionary<string,string> extraHeaders = null)
    {
        httpMethod ??= HttpMethod.Get;
        const string baseUrl = "https://api.pagerduty.com/";
        var fullUri = baseUrl + path;

        using (MiniProfiler.Current.CustomTiming("http", fullUri, httpMethod.ToString()))
        {
            var request = Http.Request(fullUri)
                              .AddHeader(HeaderNames.Accept, "application/vnd.pagerduty+json;version=2")
                              .AddHeader(HeaderNames.Authorization, "Token token=" + APIKey);

            if (extraHeaders != null)
            {
                foreach (var h in extraHeaders.Keys)
                {
                    request.AddHeader(h, extraHeaders[h]);
                }
            }

            if ((httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put) && data != null)
            {
                request.AddHeader(HeaderNames.ContentType, "application/json");
                request.SendJson(data, JilOptions);
            }

            try
            {
                var response = httpMethod switch
                {
                    _ when httpMethod == HttpMethod.Put => await request.ExpectString().PutAsync(),
                    _ when httpMethod == HttpMethod.Post => await request.ExpectString().PostAsync(),
                    _ => await request.ExpectString().GetAsync(),
                };

                // TODO: We could make this streaming JSON parsing probably
                return getFromJson(response.Data);
            }
            catch (WebException e)
            {
                // StackExchange.Utils lib is handling the logging load here
                e.Log();
                return getFromJson(null);
            }
        }
    }

    private Cache<List<PagerDutyPerson>> _allusers;
    public Cache<List<PagerDutyPerson>> AllUsers =>
        _allusers ??= GetPagerDutyCache(60.Minutes(),
                () => GetFromPagerDutyAsync("users?include[]=contact_methods", r => JSON.Deserialize<PagerDutyUserResponse>(r, JilOptions).Users)
        );
}
