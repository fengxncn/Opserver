﻿namespace Opserver.Data.SQL;

public partial class SQLCluster : IEquatable<SQLCluster>, IMonitoredService
{
    private SQLModule Module { get; }
    public string Name => Settings.Name;
    public string Description => Settings.Description;
    public TimeSpan RefreshInterval { get; }
    private SQLSettings.Cluster Settings { get; }

    public List<SQLNode> Nodes { get; }

    public List<SQLNode.AGInfo> AvailabilityGroups
        => Nodes.SelectMany(n => n.AvailabilityGroups.Data?.Where(ag => ag.IsPrimaryReplica) ?? []).ToList();

    public IEnumerable<SQLNode.AGInfo> GetAvailabilityGroups(string node, string agName)
    {
        bool agMatch(SQLNode.AGInfo ag) => agName.IsNullOrEmpty() || ag.Name == agName;

        return (node.HasValue()
            ? Nodes.Where(n => string.Equals(n.Name, node))
            : Nodes)
            .SelectMany(n => n.AvailabilityGroups.Data?.Where(agMatch) ?? []);
    }

    public MonitorStatus MonitorStatus => Nodes.GetWorstStatus();
    public string MonitorStatusReason => MonitorStatus == MonitorStatus.Good ? null : Nodes.GetReasonSummary();

    public SQLNode.AGClusterState ClusterStatus =>
        Nodes.Find(n => n.AGClusterInfo.Data?.ClusterName.HasValue() ?? false)?.AGClusterInfo.Data;

    public QuorumTypes QuorumType => ClusterStatus?.QuorumType ?? QuorumTypes.Unknown;
    public QuorumStates QuorumState => ClusterStatus?.QuorumState ?? QuorumStates.Unknown;

    public SQLCluster(SQLModule module, SQLSettings.Cluster cluster)
    {
        Module = module;
        Settings = cluster;
        Nodes = cluster.Nodes
                       .Select(n => new SQLNode(module, this, n))
                       .Where(n => n.TryAddToGlobalPollers())
                       .ToList();
        RefreshInterval = (cluster.RefreshIntervalSeconds ?? Module.Settings.RefreshIntervalSeconds).Seconds();
    }

    public bool Equals(SQLCluster other) =>
        other != null && string.Equals(Name, other.Name);

    public SQLNode GetNode(string name) =>
        Nodes.Find(n => string.Equals(n.Name, name, StringComparison.InvariantCultureIgnoreCase));
}
