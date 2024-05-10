﻿using Opserver.Data.Elastic;

namespace Opserver;

public class ElasticSettings : ModuleSettings
{
    public override bool Enabled => Clusters?.Count > 0;
    public override string AdminRole => ElasticRoles.Admin;
    public override string ViewRole => ElasticRoles.Viewer;

    /// <summary>
    /// elastic search clusters to monitor
    /// </summary>
    public List<Cluster> Clusters { get; set; } = [];

    public class Cluster : ISettingsCollectionItem
    {
        /// <summary>
        /// Nodes in this cluster
        /// </summary>
        public List<string> Nodes { get; set; } = [];

        /// <summary>
        /// The machine name for this SQL cluster
        /// </summary>
        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The authorization header, if any, to send on requests.
        /// </summary>
        public string AuthorizationHeader { get; set; }

        /// <summary>
        /// How many seconds before polling this cluster for status again
        /// </summary>
        public int RefreshIntervalSeconds { get; set; } = 120;

        /// <summary>
        /// How many seconds before polling this cluster for status again, if the cluster status is not green
        /// </summary>
        public int DownRefreshIntervalSeconds { get; set; } = 10;
    }
}
