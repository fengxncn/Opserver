using System.Collections.Concurrent;

namespace Opserver.Data;

public partial class PollingService
{
    internal ConcurrentBag<INodeRoleProvider> NodeRoleProviders { get; } = [];

    public IEnumerable<NodeRole> GetNodeRoles(string node)
    {
        foreach (var p in NodeRoleProviders)
        {
            foreach (var r in p.GetRoles(node))
            {
                yield return r;
            }
        }
    }

    public async Task<bool> EnableAllNodeRolesAsync(string node)
    {
        var tasks = new List<Task<bool>>();
        foreach (var p in NodeRoleProviders)
        {
            tasks.Add(p.EnableAsync(node));
        }
        await Task.WhenAll(tasks);
        return tasks.TrueForAll(b => b.Result);
    }

    public async Task<bool> DisableAllNodeRolesAsync(string node)
    {
        var tasks = new List<Task<bool>>();
        foreach (var p in NodeRoleProviders)
        {
            tasks.Add(p.DisableAsync(node));
        }
        await Task.WhenAll(tasks);
        return tasks.TrueForAll(b => b.Result);
    }
}
