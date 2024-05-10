using System.Collections.Concurrent;
using StackExchange.Profiling;

namespace Opserver.Data;

public partial class PollingService
{
    internal ConcurrentBag<IIssuesProvider> IssueProviders { get; } = [];

    public List<Issue> GetIssues()
    {
        using (MiniProfiler.Current.Step(nameof(GetIssues)))
        {
            return MemCache.GetSet<List<Issue>>("IssuesList", (_, __) =>
            {
                var result = new List<Issue>();
                Parallel.ForEach(IssueProviders, p =>
                {
                    List<Issue> pIssues;
                    using (MiniProfiler.Current.Step("Issues: " + p.Name))
                    {
                        pIssues = p.GetIssues().ToList();
                    }
                    lock (result)
                    {
                        result.AddRange(pIssues);
                    }
                });

                return result
                    .OrderByDescending(i => i.IsCluster)
                    .ThenByDescending(i => i.MonitorStatus)
                    .ThenByDescending(i => i.Date)
                    .ThenBy(i => i.Title)
                    .ToList();
            }, 15.Seconds(), 4.Hours());
        }
    }
}
