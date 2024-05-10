using System.Text.RegularExpressions;

namespace Opserver.Data.Exceptions;

/// <summary>
/// Represents an application and its store location
/// </summary>
public partial class Application
{
    public string Name { get; internal set; }
    public string StoreName { get; internal set; }
    public ExceptionStore Store { get; internal set; }

    public int ExceptionCount { get; internal set; }
    public int RecentExceptionCount { get; internal set; }
    public DateTime? MostRecent { get; internal set; }

    private static readonly Regex _shortLogStripRegex = GetShortLogStripRegex();
    private string _shortName;

    public string ShortName => _shortName ??= _shortLogStripRegex.Replace(Name, "");

    public JSONApplication ToJSON()
    {
        return new JSONApplication
                   {
                       Name = Name,
                       ExceptionCount = ExceptionCount,
                       MostRecent = MostRecent?.ToRelativeTime()
                   };
    }

    public void ClearCounts()
    {
        ExceptionCount = 0;
        RecentExceptionCount = 0;
        MostRecent = null;
    }

    [GeneratedRegex(@"[^A-Za-z_0-9_\-_\.\/]", RegexOptions.Compiled)]
    private static partial Regex GetShortLogStripRegex();
}

public class ApplicationGroup
{
    public string Name { get; internal set; }
    public List<Application> Applications { get; internal set; } = [];
    public int Total => Applications.Sum(a => a.ExceptionCount);

    public Application this[string name] =>
        Applications.Find(a => a.Name == name) ?? Applications.Find(a => a.ShortName == name);
}

public class JSONApplication
{
    public string Name { get; internal set; }
    public int ExceptionCount { get; internal set; }
    public string MostRecent { get; internal set; }
}
