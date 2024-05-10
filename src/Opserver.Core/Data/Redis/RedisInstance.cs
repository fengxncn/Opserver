﻿using System.Runtime.CompilerServices;
using StackExchange.Redis;

namespace Opserver.Data.Redis;

public partial class RedisInstance : PollNode<RedisModule>, IEquatable<RedisInstance>, ISearchableNode
{
    // TODO: Per-Instance searchability, sub-nodes
    string ISearchableNode.DisplayName => HostAndPort + " - " + Name;
    string ISearchableNode.Name => HostAndPort;
    string ISearchableNode.CategoryName => "Redis";

    public RedisConnectionInfo ConnectionInfo { get; internal set; }
    public string Name => ConnectionInfo.Name;
    public RedisHost Host => ConnectionInfo.Server;
    public string ReplicationGroup => ConnectionInfo.Server.ReplicationGroupName;
    public string ShortHost { get; internal set; }
    public bool ReplicatesCrossRegion { get; }

    public Version Version => Info.Data?.Server.Version;

    public string Password => ConnectionInfo.Password;
    public int Port => ConnectionInfo.Port;
    public bool UseSsl => ConnectionInfo.Settings.UseSSL;

    private string _hostAndPort;
    public string HostAndPort => _hostAndPort ??= Host + ":" + Port.ToString();

    // Redis is spanish for WE LOVE DANGER, I think.
    protected override TimeSpan BackoffDuration => TimeSpan.FromSeconds(5);

    public override string NodeType => "Redis";
    public override int MinSecondsBetweenPolls => 5;

    private ConnectionMultiplexer _connection;
    public ConnectionMultiplexer Connection
    {
        get
        {
            if (_connection?.IsConnected != true)
            {
                _connection ??= GetConnection(allowAdmin: true);
                if (!_connection.IsConnected)
                {
                    _connection.Configure();
                }
            }
            return _connection;
        }
    }

    public override IEnumerable<Cache> DataPollers
    {
        get
        {
            yield return Config;
            yield return Info;
            yield return Clients;
            yield return SlowLog;
            yield return Tiebreaker;
        }
    }

    protected override IEnumerable<MonitorStatus> GetMonitorStatus()
    {
        // WE DON'T KNOW ANYTHING, k?
        if (Info.PollsTotal == 0)
        {
            yield return MonitorStatus.Unknown;
            yield break;
        }
        if (Role == RedisInfo.RedisInstanceRole.Unknown)
        {
            yield return Info.LastPollSuccessful ? MonitorStatus.Critical : MonitorStatus.Warning;
        }
        if (!Info.LastPollSuccessful) yield return MonitorStatus.Warning;
        if (Replication == null)
        {
            yield return MonitorStatus.Unknown;
            yield break;
        }
        if (IsReplica && Replication.MasterLinkStatus != "up") yield return MonitorStatus.Warning;
        if (IsMaster && Replication.ReplicaConnections.Any(s => s.Status != "online")) yield return MonitorStatus.Warning;
    }

    protected override string GetMonitorStatusReason()
    {
        if (Role == RedisInfo.RedisInstanceRole.Unknown) return "Unknown role";
        if (Replication == null) return "Replication Unknown";
        if (IsReplica && Replication.MasterLinkStatus != "up") return "Master link down";
        if (IsMaster && Replication.ReplicaConnections.Any(s => s.Status != "online")) return "Replica offline";
        return null;
    }

    public RedisInstance(RedisModule module, RedisConnectionInfo connectionInfo) : base(module, connectionInfo.Host + ":" + connectionInfo.Port.ToString())
    {
        ConnectionInfo = connectionInfo;
        ShortHost = connectionInfo.Host.Split(StringSplits.Period)[0];
        ReplicatesCrossRegion = module.Settings.Replication?.CrossRegionNameRegex?.IsMatch(ConnectionInfo.Name) ?? true;
    }

    // We're not doing a lot of redis access, so tone down the thread count to 1 socket queue handler
    public static readonly SocketManager SharedSocketManager = new("Opserver Shared");

    private ConnectionMultiplexer GetConnection(bool allowAdmin = false, int syncTimeout = 60000)
    {
        var config = new ConfigurationOptions
        {
            SyncTimeout = syncTimeout,
            ConnectTimeout = 60000,
            AllowAdmin = allowAdmin,
            Password = Password,
            Ssl=UseSsl,
            EndPoints =
            {
                { ConnectionInfo.Host, ConnectionInfo.Port }
            },
            ClientName = "Opserver",
            SocketManager = SharedSocketManager
        };
        return ConnectionMultiplexer.Connect(config);
    }

    private Cache<T> GetRedisCache<T>(
        TimeSpan cacheDuration,
        Func<Task<T>> get,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0
        ) where T : class
    {
        return new Cache<T>(this, "Redis Fetch: " + Name + ":" + memberName,
            cacheDuration,
            get,
            addExceptionData: e => e.AddLoggedData("Server", Name)
                                    .AddLoggedData("Host", ConnectionInfo.Host)
                                    .AddLoggedData("Port", Port.ToString()),
            timeoutMs: 10000,
            memberName: memberName,
            sourceFilePath: sourceFilePath,
            sourceLineNumber: sourceLineNumber
        );
    }

    public bool Equals(RedisInstance other)
    {
        if (other == null) return false;
        return Host == other.Host && Port == other.Port;
    }

    public override string ToString() => HostAndPort;

    public RedisMemoryAnalysis GetDatabaseMemoryAnalysis(int database, bool runIfMaster = false)
    {
        if (IsMaster && !runIfMaster)
        {
            // no replicas, and a master - boom
            if (ReplicaCount == 0)
            {
                return new RedisMemoryAnalysis(new RedisAnalyzer(this), ConnectionInfo, database)
                {
                    ErrorMessage = "Cannot run memory analysis on a master - it hurts."
                };
            }

            // Go to the first replica, automagically
            return new RedisAnalyzer(ReplicaInstances[0]).AnalyzeDatabaseMemory(database);
        }

        return new RedisAnalyzer(this).AnalyzeDatabaseMemory(database);
    }

    public void ClearDatabaseMemoryAnalysisCache(int database)
    {
        RedisAnalyzer.ClearDatabaseMemoryAnalysisCache(this, database);
    }

    //static RedisInstance()
    //{
    // Cache all the things! - need ClientFlags first though
    //RuntimeTypeModel.Default
    //                .Add(typeof (ClientInfo), false)
    //                .Add("Address",
    //                     "AgeSeconds",
    //                     "IdleSeconds",
    //                     "Database",
    //                     "SubscriptionCount",
    //                     "PatternSubscriptionCount",
    //                     "TransactionCommandLength",
    //                     "FlagsRaw",
    //                     "ClientFlags",
    //                     "LastCommand",
    //                     "Name");
    //}
}
