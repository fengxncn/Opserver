﻿using Opserver.Data.Redis;

namespace Opserver.Views.Redis;

public enum RedisViews
{
    All = 0,
    Server = 1,
    Instance = 2
}

public class DashboardModel
{
    public List<RedisReplicationGroup> ReplicationGroups { get; set; }
    public List<RedisInstance> Instances { get; set; }
    public string CurrentRedisServer { get; set; }
    public RedisInstance CurrentInstance { get; set; }
    public bool Refresh { get; set; }
    public RedisViews View { get; set; }

    public bool? _allVersionsMatch;
    public bool AllVersionsMatch => _allVersionsMatch ??= Instances?.All(i => i.Version == Instances[0].Version) == true;

    public Version CommonVersion => AllVersionsMatch ? Instances[0].Version : null;

    public List<RedisInstance> Masters { get; private set; }
    public List<RedisInstance> Replicating { get; private set; }
    public List<RedisInstance> Missing { get; private set; }
    public List<RedisInstance> Heads { get; private set; }
    public List<RedisInstance> StandAloneMasters { get; private set; }

    public void Prep()
    {
        Instances = Instances.OrderBy(i => i.Port).ThenBy(i => i.Name).ThenBy(i => i.Host.HostName).ToList();
        Masters = Instances.Where(i => i.IsMaster).ToList();
        Replicating = Instances.Where(i => i.IsReplicating).ToList();
        Missing = Instances.Where(i => !Replicating.Contains(i) && (i.Info == null || i.Role == RedisInfo.RedisInstanceRole.Unknown || !i.Info.LastPollSuccessful)).ToList();
        // In the single server view, everything is top level
        Heads = View == RedisViews.Server ? Instances.ToList() : Masters.Where(m => m.ReplicaCount > 0).ToList();
        StandAloneMasters = View == RedisViews.Server ? [] : Masters.Where(m => m.ReplicaCount == 0 && !Missing.Contains(m)).ToList();
    }
}
