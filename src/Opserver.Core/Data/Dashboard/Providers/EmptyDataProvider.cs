using System.Net;

namespace Opserver.Data.Dashboard.Providers;

public class EmptyDataProvider(DashboardModule module, string uniqueKey) : DashboardDataProvider(module, uniqueKey)
{
    public override bool HasData => false;

    public override int MinSecondsBetweenPolls => 10;
    public override string NodeType => "None";
    public override IEnumerable<Cache> DataPollers => [];
    protected override IEnumerable<MonitorStatus> GetMonitorStatus() => [];
    protected override string GetMonitorStatusReason() => null;

    private static readonly List<Node> EmptyAllNodes = [];

    public override IEnumerable<string> GetExceptions() => [];

    public override List<Node> AllNodes => EmptyAllNodes;
    public override IEnumerable<Node> GetNodesByIP(IPAddress ip) => EmptyAllNodes;

    public override Task<List<GraphPoint>> GetCPUUtilizationAsync(Node node, DateTime? start, DateTime? end, int? pointCount = null) => Task.FromResult(new List<GraphPoint>());
    public override Task<List<GraphPoint>> GetMemoryUtilizationAsync(Node node, DateTime? start, DateTime? end, int? pointCount = null) => Task.FromResult(new List<GraphPoint>());
    public override Task<List<DoubleGraphPoint>> GetNetworkUtilizationAsync(Node node, DateTime? start, DateTime? end, int? pointCount = null) => Task.FromResult(new List<DoubleGraphPoint>());
    public override Task<List<DoubleGraphPoint>> GetVolumePerformanceUtilizationAsync(Node node, DateTime? start, DateTime? end, int? pointCount = null) => Task.FromResult(new List<DoubleGraphPoint>());
    public override Task<List<DoubleGraphPoint>> GetUtilizationAsync(Interface iface, DateTime? start, DateTime? end, int? pointCount = null) => Task.FromResult(new List<DoubleGraphPoint>());
    public override Task<List<DoubleGraphPoint>> GetPerformanceUtilizationAsync(Volume volume, DateTime? start, DateTime? end, int? pointCount = null) => Task.FromResult(new List<DoubleGraphPoint>());
    public override Task<List<GraphPoint>> GetUtilizationAsync(Volume volume, DateTime? start, DateTime? end, int? pointCount = null) => Task.FromResult(new List<GraphPoint>());
}
