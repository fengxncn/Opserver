namespace Opserver.Data.Redis;

[AttributeUsage(AttributeTargets.Property)]
public sealed class RedisInfoPropertyAttribute(string propertyName) : Attribute
{
    public string PropertyName { get; } = propertyName;
}
