﻿namespace Opserver.Data.SQL;

public partial class SQLNode
{
    private Cache<AGClusterState> _agClusterInfo;

    public Cache<AGClusterState> AGClusterInfo =>
        _agClusterInfo ??= GetSqlCache(nameof(AGClusterInfo), async conn =>
        {
            var sql = QueryLookup.GetOrAdd(Tuple.Create(nameof(AGClusterInfo), Engine), k =>
                    GetFetchSQL<AGClusterState>(k.Item2) + "\n" +
                    GetFetchSQL<AGClusterMemberInfo>(k.Item2) + "\n" +
                    GetFetchSQL<AGClusterNetworkInfo>(k.Item2)
            );

            AGClusterState state;
            using (var multi = await conn.QueryMultipleAsync(sql))
            {
                state = await multi.ReadFirstOrDefaultAsync<AGClusterState>();
                if (state != null)
                {
                    state.Members = await multi.ReadAsync<AGClusterMemberInfo>().AsList();
                    state.Networks = await multi.ReadAsync<AGClusterNetworkInfo>().AsList();
                }
            }
            if (state != null)
            {
                foreach (var m in state.Members)
                {
                    m.IsLocal = string.Equals(m.MemberName, ServerProperties.Data?.ServerName, StringComparison.InvariantCultureIgnoreCase);
                }
            }
            return state;
        });

    public class AGClusterState : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2012.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public string ClusterName { get; internal set; }
        public QuorumTypes QuorumType { get; internal set; }
        public QuorumStates QuorumState { get; internal set; }
        public int? Votes { get; internal set; }

        public List<AGClusterMemberInfo> Members { get; internal set; }
        public List<AGClusterNetworkInfo> Networks { get; internal set; }

        public string GetFetchSQL(in SQLServerEngine e) => @"
Select cluster_name ClusterName,
       quorum_type QuorumType,
       quorum_state QuorumState
  From sys.dm_hadr_cluster;";
    }

    public class AGClusterMemberInfo : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2012.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public string MemberName { get; internal set; }
        public ClusterMemberTypes Type { get; internal set; }
        public ClusterMemberStates State { get; internal set; }
        public int? Votes { get; internal set; }
        public bool IsLocal { get; internal set; }

        public string GetFetchSQL(in SQLServerEngine e) => @"
Select member_name MemberName,
       member_type Type,
       member_state State,
       number_of_quorum_votes Votes
  From sys.dm_hadr_cluster_members;";
    }

    public class AGClusterNetworkInfo : ISQLVersioned
    {
        Version IMinVersioned.MinVersion => SQLServerVersions.SQL2012.RTM;
        SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

        public string MemberName { get; internal set; }
        public string NetworkSubnetIP { get; internal set; }
        public string NetworkSubnetIPMask { get; internal set; }
        public int NetworkSubnetPrefixLength { get; internal set; }
        public bool IsPublic { get; internal set; }
        public bool IsIPV4 { get; internal set; }
        public bool IsLocal { get; internal set; }

        private IPNet _networkIPNet;
        public IPNet NetworkIPNet =>
            _networkIPNet ??= IPNet.Parse(NetworkSubnetIP, (byte)NetworkSubnetPrefixLength);

        public string GetFetchSQL(in SQLServerEngine e) => @"
Select member_name MemberName,
       network_subnet_ip NetworkSubnetIP,
       network_subnet_ipv4_mask NetworkSubnetIPMask,
       network_subnet_prefix_length NetworkSubnetPrefixLength,
       is_public IsPublic,
       is_ipv4 IsIPV4,
       Cast(Case When member_name = SERVERPROPERTY('MachineName') Then 1 Else 0 End as Bit) IsLocal
  From sys.dm_hadr_cluster_networks;";
    }
}
