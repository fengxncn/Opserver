﻿using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Jil;

namespace Opserver.Data.Dashboard.Providers;

public partial class BosunDataProvider
{
    public class TSDBQuery
    {
        [DataMember(Name = "start")]
        public string Start { get; set; }
        [DataMember(Name = "end")]
        public string End { get; set; }
        [DataMember(Name = "queries")]
        public List<object> Queries { get; set; }

        public TSDBQuery(DateTime? startTime, DateTime? endTime = null)
        {
            Start = ConvertTime(startTime, DateTime.UtcNow.AddYears(-1));
            if (endTime.HasValue) End = ConvertTime(endTime, DateTime.UtcNow);
            Queries = [];
        }

        public static string ConvertTime(DateTime? date, DateTime valueIfNull)
        {
            return (date ?? valueIfNull).ToString("yyyy/MM/dd-HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public void AddQuery(string metric, string host = "*", bool counter = true, IDictionary<string, string> tags = null)
        {
            var query = new
            {
                metric,
                aggregator = "sum",
                tags = new Dictionary<string, string>
                {
                    [nameof(host)] = host
                },
                rate = counter,
                rateOptions = new
                {
                    resetValue = 1,
                    counter = true
                }
            };
            if (tags != null)
            {
                foreach (var p in tags) query.tags[p.Key] = p.Value;
            }
            Queries.Add(query);
        }
    }

    public async Task<BosunMetricResponse> RunTSDBQueryAsync(TSDBQuery query, int? pointCount = null)
    {
        var json = JSON.SerializeDynamic(query, Options.ExcludeNullsUtc);
        var url = GetUrl($"api/graph?json={json}{(pointCount.HasValue ? "&autods=" + pointCount.ToString() : "")}");
        var apiResult = await GetFromBosunAsync<BosunMetricResponse>(url);
        return apiResult.Result;
    }

    public Task<BosunMetricResponse> GetMetric(string metricName, DateTime start, DateTime? end = null, string host = "*", IDictionary<string, string> tags = null)
    {
        metricName = BosunMetric.GetDenormalized(metricName, host, NodeMetricCache.Data);
        var query = new TSDBQuery(start, end);
        query.AddQuery(metricName, host, BosunMetric.IsCounter(metricName, host), tags);
        return RunTSDBQueryAsync(query, 500);
    }

    private Cache<IntervalCache> _dayCache;
    public Cache<IntervalCache> DayCache
    {
        get
        {
            return _dayCache ??= ProviderCache(async () =>
            {
                var result = new IntervalCache(TimeSpan.FromDays(1));
                async Task addMetric(string metricName, string[] tags)
                {
                    var tagDict = tags?.ToDictionary(t => t, _ => "*");
                    var apiResult = await GetMetric(metricName, result.StartTime, tags: tagDict);
                    if (apiResult == null) return;
                    if (tags?.Length > 0)
                    {
                        result.MultiSeries[metricName] = apiResult.Series
                            .GroupBy(s => s.Host)
                            .ToDictionary(s => s.Key.NormalizeForCache(), s => s.ToList());
                    }
                    else
                    {
                        result.Series[metricName] = apiResult.Series.ToDictionary(s => s.Host.NormalizeForCache());
                    }
                }

                var c = addMetric(BosunMetric.Globals.CPU, null);
                var m = addMetric(BosunMetric.Globals.MemoryUsed, null);
                var n = addMetric(BosunMetric.Globals.NetBytes, [BosunMetric.Tags.Direction]);
                await Task.WhenAll(c, m, n); // parallel baby!

                return result;
            }, 60.Seconds(), 60.Minutes());
        }
    }

    public class IntervalCache(TimeSpan timespan)
    {
        public TimeSpan TimeSpan { get; set; } = timespan;
        public DateTime StartTime { get; set; } = DateTime.UtcNow - timespan;

        public Dictionary<string, PointSeries> CPU => Series[BosunMetric.Globals.CPU];
        public Dictionary<string, PointSeries> Memory => Series[BosunMetric.Globals.MemoryUsed];
        public Dictionary<string, List<PointSeries>> Network => MultiSeries[BosunMetric.Globals.NetBytes];

        internal ConcurrentDictionary<string, Dictionary<string, PointSeries>> Series { get; set; } = new ConcurrentDictionary<string, Dictionary<string, PointSeries>>();
        internal ConcurrentDictionary<string, Dictionary<string, List<PointSeries>>> MultiSeries { get; set; } = new ConcurrentDictionary<string, Dictionary<string, List<PointSeries>>>();
    }
}

public class BosunMetric
{
    public BosunMetricType? Type { get; set; }
    public string Unit { get; set; }
    public List<BosunMetricDescription> Description { get; set; }

    public static class Globals
    {
        public const string CPU = "os.cpu";
        public const string MemoryUsed = "os.mem.used";
        public const string NetBytes = "os.net.bytes";
        public const string NetBondBytes = "os.net.bond.bytes";
        public const string NetVirtualBytes = "os.net.virtual.bytes";
        public const string NetTunnelBytes = "os.net.tunnel.bytes";
        public const string NetOtherBytes = "os.net.other.bytes";
        public const string DiskUsed = "os.disk.fs.space_used";
    }

    private static class Suffixes
    {
        public const string CPU = "." + Globals.CPU;
    }

    public static class Tags
    {
        public const string Direction = "direction";
        public const string Disk = "disk";
        public const string Host = "host";
        public const string IFace = "iface";
    }

    public static class TagValues
    {
        public const string In = "in";
        public const string Out = "out";
    }

    public static class TagCombos
    {
        public static readonly Dictionary<string, string>
            AllNetDirections = new() { [Tags.Direction] = "*" },
            AllDisks = new() { [Tags.Disk] = "*" };

        public static Dictionary<string, string> AllDirectionsForInterface(string ifaceId)
            => new()
            {
                [Tags.Direction] = "*",
                [Tags.IFace] = ifaceId
            };
    }

    public static bool IsCounter(string metric, string host)
    {
        if (metric.IsNullOrEmpty()) return false;
        if (metric.StartsWith("__"))
        {
            metric = metric.Replace($"__{host}.", "");
        }
        return metric switch
        {
            Globals.CPU or
            Globals.NetBytes or
            Globals.NetBondBytes or
            Globals.NetOtherBytes or
            Globals.NetTunnelBytes or
            Globals.NetVirtualBytes => true,
            _ => false,
        };
    }

    public static string InterfaceMetricName(Interface i) =>
        i.TypeDescription switch
        {
            "bond" => Globals.NetBondBytes,
            "other" => Globals.NetOtherBytes,
            "tunnel" => Globals.NetTunnelBytes,
            "virtual" => Globals.NetVirtualBytes,
            _ => Globals.NetBytes,
        };

    public static string GetDenormalized(string metric, string host, Dictionary<string, List<string>> metricCache)
    {
        if (host == null || host.Contains('*') || host.Contains('|'))
        {
            return metric;
        }

        switch (metric)
        {
            case Globals.CPU:
            case Globals.MemoryUsed:
            case Globals.NetBondBytes:
            case Globals.NetOtherBytes:
            case Globals.NetTunnelBytes:
            case Globals.NetVirtualBytes:
            case Globals.NetBytes:
                var result = $"__{host}.{metric}";
                List<string> hostMetrics;
                // Only return this denormalized metric optimization if it's actually configured in the Bosun relay
                if (metricCache != null && metricCache.TryGetValue(host, out hostMetrics) && hostMetrics.Contains(result))
                    return result;
                break;
        }
        return metric;
    }
}

public enum BosunMetricType
{
    gauge = 0,
    counter = 1,
    rate = 2
}

public class BosunMetricDescription
{
    public string Text { get; set; }
    public Dictionary<string, string> Tags { get; set; }
}

public class BosunMetricResponse
{
    public List<string> Queries { get; set; }
    public List<PointSeries> Series { get; set; }
}

/// <summary>
/// The Data field consists of pairs, Data[n][0] is the epoch, Data[n][1] is the value.
/// </summary>
public partial class PointSeries
{
    private static readonly Regex HostRegex = GetHostRegex();
    private string _host;
    public string Host
    {
        get
        {
            if (_host == null && !Tags.TryGetValue("host", out _host))
            {
                var match = HostRegex.Match(Name);
                _host = match.Success ? match.Groups[1].Value : "Unknown";
            }
            return _host;
        }
        set { _host = value; }
    }

    public string Name { get; set; }
    public string Metric { get; set; }
    public string Unit { get; set; }
    public Dictionary<string, string> Tags { get; set; }
    public List<float[]> Data { get; set; }

    private List<GraphPoint> _pointData;
    public List<GraphPoint> PointData => _pointData ??= Data.Select(p => new GraphPoint
    {
        DateEpoch = (long)p[0],
        Value = p[1]
    }).ToList();

    public PointSeries() { }
    public PointSeries(string host)
    {
        _host = host;
        Data = [];
    }

    [GeneratedRegex(@"\{host=(.*)[,|\}]", RegexOptions.Compiled)]
    private static partial Regex GetHostRegex();
}
