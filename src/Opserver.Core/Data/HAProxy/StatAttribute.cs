namespace Opserver.Data.HAProxy;

/// <summary>
/// Represents a statistic from the proxy stat dump, since these are always added at the end in newer versions, they're parsed based on position.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class StatAttribute(string name, int position) : Attribute
{
    public int Position { get; set; } = position;
    public string Name { get; set; } = name;
}
