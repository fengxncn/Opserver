﻿using System.Text.RegularExpressions;
using Opserver.Data.Redis;

namespace Opserver;

public class RedisSettings : ModuleSettings
{
    public override bool Enabled => Servers.Count > 0;
    public override string AdminRole => RedisRoles.Admin;
    public override string ViewRole => RedisRoles.Viewer;

    public List<Server> Servers { get; set; } = [];

    public Server AllServers { get; set; } = new Server();

    public Server Defaults { get; set; } = new Server();

    public ReplicationSettings Replication { get; set; }

    public class ReplicationSettings
    {
        private Regex _regionHostRegex, _crossRegionNameRegex;

        public string RegionHostPattern { get; set; }
        public string CrossRegionNamePattern { get; set; }

        /// <summary>
        /// The pattern to match against a host to get the region portion (e.g. a datacenter).
        /// </summary>
        public Regex RegionHostRegex => _regionHostRegex ??= GetRegex(RegionHostPattern);

        /// <summary>
        /// The pattern to match against a host to get the region portion (e.g. a datacenter).
        /// </summary>
        public Regex CrossRegionNameRegex => _crossRegionNameRegex ??= GetRegex(CrossRegionNamePattern);

        private static Regex GetRegex(string pattern) =>
            pattern.IsNullOrEmpty() ? null : new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    }

    public class Server : ISettingsCollectionItem
    {
        public List<Instance> Instances { get; set; } = [];

        /// <summary>
        /// The machine name for this Redis server - used to match against the dashboard
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Unused currently
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The group this server belongs to (for server-level controls)
        /// </summary>
        public string ReplicationGroup { get; set; }

        /// <summary>
        /// How many seconds before polling this cluster for status again
        /// </summary>
        public int RefreshIntervalSeconds { get; set; } = 30;
    }

    public class Instance : ISettingsCollectionItem
    {
        /// <summary>
        /// The machine name for this node
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Connection for for this node
        /// </summary>
        public int Port { get; set; } = 6379;

        /// <summary>
        /// The password for this node
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Specify if ssl should be used. Defaults to false.
        /// </summary>
        public bool UseSSL { get; set; } = false;

        /// <summary>
        /// Regular expressions collection to crawl keys against, to break out Redis DB usage
        /// </summary>
        public Dictionary<string, string> AnalysisRegexes { get; set; } = [];
    }
}
