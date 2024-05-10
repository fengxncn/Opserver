﻿using StackExchange.Profiling;
using StackExchange.Redis;

namespace Opserver.Data.Redis;

public static class ServerUtils
{
    public static IServer GetSingleServer(this ConnectionMultiplexer connection)
    {
        var endpoints = connection.GetEndPoints(configuredOnly: true);
        if (endpoints.Length == 1) return connection.GetServer(endpoints[0]);

        var sb = StringBuilderCache.Get().Append("expected one endpoint, got ").Append(endpoints.Length).Append(": ");
        foreach (var ep in endpoints)
            sb.Append(ep).Append(", ");
        sb.Length -= 2;
        throw new InvalidOperationException(sb.ToStringRecycle());
    }
}

public partial class RedisInstance
{
    //Specially caching this since it's accessed so often
    public RedisInfo.ReplicationInfo Replication { get; private set; }

    private Cache<RedisInfo> _info;
    public Cache<RedisInfo> Info =>
        _info ??= GetRedisCache(10.Seconds(), async () =>
        {
            var server = Connection.GetSingleServer();
            string infoStr;
            using (MiniProfiler.Current.CustomTiming("redis", "INFO"))
            {
                infoStr = await server.InfoRawAsync();
            }
            ConnectionInfo.Features = server.Features;
            var ri = RedisInfo.FromInfoString(infoStr);
            if (ri != null) Replication = ri.Replication;
            return ri;
        });

    public RedisInfo.RedisInstanceRole Role
    {
        get
        {
            var lastRole = Replication?.RedisInstanceRole;
            // If we think we're a master and the last poll failed - look to other nodes for info
            if (!Info.LastPollSuccessful && lastRole == RedisInfo.RedisInstanceRole.Master
                && Module.Instances.Any(r => r.ReplicaInstances.Any(s => s == this)))
            {
                return RedisInfo.RedisInstanceRole.Replica;
            }
            return lastRole ?? RedisInfo.RedisInstanceRole.Unknown;
        }
    }

    public string RoleDescription =>
        Role switch
        {
            RedisInfo.RedisInstanceRole.Master => "Master",
            RedisInfo.RedisInstanceRole.Replica => "Replica",
            _ => "Unknown",
        };

    public bool IsMaster => Role == RedisInfo.RedisInstanceRole.Master;
    public bool IsReplica => Role == RedisInfo.RedisInstanceRole.Replica;
    public bool IsReplicating => IsReplica && (Replication.MasterLinkStatus != "up" || Info.Data?.Replication?.MastSyncLeftBytes > 0);

    public RedisInstance TopMaster
    {
        get
        {
            var top = this;
            while (top.Master != null)
            {
                top = top.Master;
            }
            return top;
        }
    }

    public RedisInstance Master =>
        Replication?.MasterHost.HasValue() == true
        ? Module.GetInstance(Replication.MasterHost, Replication.MasterPort)
        : Module.Instances.Find(i => i.ReplicaInstances.Contains(this));

    public int ReplicaCount => Replication?.ConnectedReplicas ?? 0;

    public int TotalReplicaCount
    {
        get { return ReplicaCount + (ReplicaCount > 0 && ReplicaInstances != null ? ReplicaInstances.Sum(s => s?.TotalReplicaCount ?? 0) : 0); }
    }

    public List<RedisInfo.RedisReplicaInfo> ReplicaConnections => Replication?.ReplicaConnections;

    public List<RedisInstance> ReplicaInstances
    {
        get
        {
            return Info.LastPollSuccessful ? (Replication?.ReplicaConnections.Select(s => Module.GetInstance(s)).Where(s => s != null).ToList() ?? []) : [];
            // If we can't poll this server, ONLY trust the other nodes we can poll
            //return AllInstances.Where(i => i.Master == this).ToList();
        }
    }

    public List<RedisInstance> GetAllReplicasInChain()
    {
        var replicas = ReplicaInstances;
        return replicas.Union(replicas.SelectMany(i => i?.GetAllReplicasInChain() ?? Enumerable.Empty<RedisInstance>())).Distinct().ToList();
    }

    public class RedisInfoSection
    {
        /// <summary>
        /// Indicates if this section is for the entire INFO
        /// Pretty much means this is from a pre-2.6 release of redis
        /// </summary>
        public bool IsGlobal { get; internal set; }

        public string Title { get; internal set; }

        public List<RedisInfoLine> Lines { get; internal set; }
    }

    public class RedisInfoLine
    {
        public bool Important { get; internal set; }
        public string Key { get; internal set; }
        public string ParsedValue { get; internal set; }
        public string OriginalValue { get; internal set; }
    }
}
