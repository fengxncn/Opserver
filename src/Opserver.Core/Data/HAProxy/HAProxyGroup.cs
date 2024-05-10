﻿namespace Opserver.Data.HAProxy;

public partial class HAProxyGroup : IMonitoredService, ISearchableNode
{
    private HAProxyModule Module { get; }

    public string DisplayName => Name;
    public string CategoryName => "HAProxy";

    public HAProxySettings.Group Settings { get; }

    public string Name => Settings.Name;
    public string Description => Settings.Description;
    public List<HAProxyInstance> Instances { get; }

    public MonitorStatus MonitorStatus => Instances.GetWorstStatus();

    public string MonitorStatusReason => Instances.GetReasonSummary();

    public HAProxyGroup(HAProxyModule module, HAProxySettings.Group group)
    {
        Module = module;
        Settings = group;
        Instances = group.Instances.Select(i => new HAProxyInstance(module, i, group) { Group = this }).ToList();
        Instances.ForEach(i => i.TryAddToGlobalPollers());
    }

    /// <summary>
    /// Creates a single instance group for consistent management at a higher level.
    /// </summary>
    /// <param name="module">The overall <see cref="HAProxyModule"/> this group belongs to.</param>
    /// <param name="instance">The <see cref="HAProxyInstance"/> to create a single-item group for.</param>
    public HAProxyGroup(HAProxyModule module, HAProxySettings.Instance instance)
    {
        Module = module;
        Settings = new HAProxySettings.Group
            {
                Name = instance.Name,
                Description = instance.Description
            };
        Instances =
        [
            new HAProxyInstance(module, instance)
            {
                Group = this
            }
        ];
        Instances.ForEach(i => i.TryAddToGlobalPollers());
    }

    public override string ToString()
    {
        return string.Concat(Name, " - ", Instances != null ? Instances.Count.ToString() + " instances" : "");
    }

    /// <summary>
    /// Gets the list of proxies for this group
    /// </summary>
    public List<Proxy> GetProxies() => HAProxyModule.GetProxies(Instances);
}
