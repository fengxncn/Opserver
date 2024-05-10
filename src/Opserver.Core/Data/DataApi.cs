namespace Opserver.Data;

public static class DataApi
{
    public static IEnumerable<NodeData> GetType(PollingService poller, string type, bool includeData = false) =>
        poller.GetNodes(type).Select(n => new NodeData(n, includeData));

    public static NodeData GetNode(PollNode node, bool includeData = false) => new(node, includeData);

    public static CacheData GetCache(Cache cache, bool includeData = false) => new(cache, includeData);

    public class NodeData(PollNode node, bool includeData = false)
    {
        public string Name { get; } = node.UniqueKey;
        public string Type { get; } = node.NodeType;
        public DateTime? LastPolled { get; } = node.LastPoll;
        public double LastPollDurationMS { get; } = node.LastPollDuration.TotalMilliseconds;
        public IEnumerable<object> Caches { get; } = node.DataPollers.Select(c => new CacheData(c, includeData));
    }

    public class CacheData(Cache cache, bool includeData = false)
    {
        public string Name { get; } = cache.ParentMemberName;
        public DateTime? LastPolled { get; } = cache.LastPoll;
        public DateTime? LastSuccess { get; } = cache.LastSuccess;
        public double? LastPollDurationMs { get; } = cache.LastPollDuration?.TotalMilliseconds;
        public string LastPollError { get; } = cache.ErrorMessage.HasValue() ? cache.ErrorMessage : null;
        public bool HasData { get; } = cache.ContainsData;
        public object Data { get; } = includeData ? cache.InnerCache : null;
    }
}
