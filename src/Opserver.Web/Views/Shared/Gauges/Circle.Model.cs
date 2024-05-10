using Opserver.Data;

namespace Opserver.Views.Shared.Guages;

public class CircleModel(string label, IEnumerable<IMonitorStatus> items)
{
    public string Label { get; set; } = label;
    public IEnumerable<IMonitorStatus> Items { get; set; } = items;
}
