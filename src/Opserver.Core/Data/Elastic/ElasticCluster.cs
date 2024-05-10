﻿using System.Runtime.CompilerServices;
using StackExchange.Profiling;

namespace Opserver.Data.Elastic;

public partial class ElasticCluster : PollNode<ElasticModule>, ISearchableNode, IMonitoredService
{
    string ISearchableNode.Name => SettingsName;
    string ISearchableNode.DisplayName => SettingsName;
    private TimeSpan? _refreshInteval;
    public TimeSpan RefreshInterval => _refreshInteval ??= Settings.RefreshIntervalSeconds.Seconds();
    string ISearchableNode.CategoryName => "elastic";
    public ElasticSettings.Cluster Settings { get; }
    private string SettingsName => Settings.Name;

    public ElasticCluster(ElasticModule module, ElasticSettings.Cluster cluster) : base(module, cluster.Name)
    {
        Settings = cluster;
        KnownNodes = cluster.Nodes.Select(n => new ElasticNode(n, Settings)).ToList();
    }

    public override string NodeType => "elastic";
    public override int MinSecondsBetweenPolls => 5;

    public override IEnumerable<Cache> DataPollers
    {
        get
        {
            yield return Nodes;
            yield return IndexStats;
            yield return State;
            yield return HealthStatus;
            yield return Aliases;
        }
    }

    protected override IEnumerable<MonitorStatus> GetMonitorStatus()
    {
        if (HealthStatus.Data?.Indexes != null)
            yield return HealthStatus.Data.Indexes.Values.GetWorstStatus();
        if (KnownNodes.All(n => n.LastException != null))
            yield return MonitorStatus.Critical;
        if (KnownNodes.Any(n => n.LastException != null))
            yield return MonitorStatus.Warning;

        yield return DataPollers.GetWorstStatus();
    }

    protected override string GetMonitorStatusReason()
    {
        var reason = HealthStatus.Data?.Indexes?.Values.GetReasonSummary();
        if (reason.HasValue()) return reason;

        var sb = StringBuilderCache.Get();
        foreach (var node in KnownNodes)
        {
            if (node.LastException != null)
            {
                sb.Append(node.Name.IsNullOrEmptyReturn(node.Host))
                    .Append(": ")
                    .Append(node.LastException.Message)
                    .Append("; ");
            }
        }

        if (sb.Length > 2)
            sb.Length -= 2;

        return sb.ToStringRecycle();
    }

    // TODO: Poll down nodes faster?
    //var hs = result as ClusterHealthStatusInfo;
    //if (hs != null)
    //{
    //    _settingsRefreshIntervalSeconds = hs.MonitorStatus == MonitorStatus.Good
    //                                        ? (int?)null
    //                                        : ConfigSettings.DownRefreshIntervalSeconds;
    //}

    private Cache<T> GetElasticCache<T>(
        Func<Task<T>> get,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0
        ) where T : class, new()
    {
        return new Cache<T>(this, "Elastic Fetch: " + SettingsName + ":" + typeof(T).Name,
            RefreshInterval,
            get,
            addExceptionData: e => e.AddLoggedData("Cluster", SettingsName).AddLoggedData("Type", typeof(T).Name),
            memberName: memberName,
            sourceFilePath: sourceFilePath,
            sourceLineNumber: sourceLineNumber
        );
    }

    public async Task<T> GetAsync<T>(string path) where T : class
    {
        using (MiniProfiler.Current.CustomTiming("elastic", path))
        {
            foreach (var n in KnownNodes)
            {
                var result = await n.GetAsync<T>(path);
                if (result != null)
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static MonitorStatus ColorToStatus(string color) =>
        color switch
        {
            "green" => MonitorStatus.Good,
            "yellow" => MonitorStatus.Warning,
            "red" => MonitorStatus.Critical,
            _ => MonitorStatus.Unknown,
        };

    public override string ToString() => SettingsName;
}
