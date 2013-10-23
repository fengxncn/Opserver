﻿using System;
using System.Collections.Generic;

namespace StackExchange.Opserver.Data.SQL
{
    public partial class SQLNode
    {
        private Cache<List<TCPListenerState>> _tcpListeners;
        public Cache<List<TCPListenerState>> TCPListeners
        {
            get { return _tcpListeners ?? (_tcpListeners = SqlCacheList<TCPListenerState>(Cluster.RefreshInterval)); }
        }

        /// <summary>
        /// http://msdn.microsoft.com/en-us/library/hh245287.aspx
        /// </summary>
        public class TCPListenerState : ISQLVersionedObject, IMonitorStatus
        {
            public Version MinVersion { get { return SQLServerVersions.SQL2012.RTM; } }

            public MonitorStatus MonitorStatus
            {
                get
                {
                    switch (State)
                    {
                        case TCPListenerStates.Online:
                            return MonitorStatus.Good;
                        case TCPListenerStates.PendingRestart:
                            return MonitorStatus.Maintenance;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
            public string MonitorStatusReason
            {
                get { return State == TCPListenerStates.Online ? null : State.GetDescription(); }
            }

            public int ListenerId { get; internal set; }
            public string IPAddress { get; internal set; }
            public bool IsIPV4 { get; internal set; }
            public int Port { get; internal set; }
            public TCPListenerTypes Type { get; internal set; }
            public TCPListenerStates State { get; internal set; }
            public DateTime StartTime { get; internal set; }

            internal const string FetchSQL = @"
select listener_id ListenerId,
       ip_address IPAddress,
       is_ipv4 IsIPV4,
       port Port,
       type Type,
       state State,
       start_time StartTime
from sys.dm_tcp_listener_states";

            public string GetFetchSQL(Version v)
            {
                return FetchSQL;
            }
        }
        
    }
}