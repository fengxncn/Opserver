﻿using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using StackExchange.Profiling;
using StackExchange.Redis;

namespace Opserver.Data.Redis;

public partial class RedisAnalyzer
{
    private RedisInstance Instance { get; }
    internal readonly List<KeyMatcher> KeyMatchers;
    public RedisAnalyzer(RedisInstance instance)
    {
        Instance = instance;
        KeyMatchers = instance.ConnectionInfo.Settings.AnalysisRegexes
                              .Where(r => r.Value.HasValue())
                              .Select(r => new KeyMatcher { Name = r.Key, Regex = new Regex(r.Value, RegexOptions.Compiled) })
                              .ToList();
        KeyMatchers.Add(new KeyMatcher { Name = "Other (unrecognized)", Regex = GetKeyMatcherRegex() });
    }

    private static string GetMemoryAnalysisKey(RedisConnectionInfo connectionInfo, int database)
    {
        return $"redis-memory-analysis-{connectionInfo.Host}:{connectionInfo.Port}:{database}";
    }

    public RedisMemoryAnalysis AnalyzeDatabaseMemory(int database)
    {
        var connectionInfo = Instance.ConnectionInfo;
        using (MiniProfiler.Current.Step("Redis Memory Analysis for " + connectionInfo + " - DB:" + database.ToString()))
        {
            return Instance.MemCache.GetSet<RedisMemoryAnalysis>(GetMemoryAnalysisKey(connectionInfo, database), (_, __) => GetDatabaseMemoryAnalysis(connectionInfo, database), 24.Hours(), 24.Hours());
        }
    }

    public static void ClearDatabaseMemoryAnalysisCache(RedisInstance instance, int database)
    {
        instance.MemCache.Remove(GetMemoryAnalysisKey(instance.ConnectionInfo, database));
    }

    private RedisMemoryAnalysis GetDatabaseMemoryAnalysis(RedisConnectionInfo connectionInfo, int database)
    {
        var config = new ConfigurationOptions
        {
            SyncTimeout = 10 * 60 * 1000,
            AllowAdmin = true,
            ClientName = "Status-MemoryAnalyzer",
            Password = connectionInfo.Password,
            Ssl = connectionInfo.Settings.UseSSL,
            EndPoints =
            {
                { connectionInfo.Host, connectionInfo.Port }
            }
        };
        using var muxer = ConnectionMultiplexer.Connect(config);
        var ma = new RedisMemoryAnalysis(this, connectionInfo, database);
        if (ma.ErrorMessage.HasValue())
        {
            return ma;
        }
        // Prep the match dictionary
        foreach (var km in KeyMatchers)
        {
            ma.KeyStats[km] = new KeyStats();
        }

        ma.Analyze(muxer);

        return ma;
    }

    [GeneratedRegex(".", RegexOptions.Compiled)]
    private static partial Regex GetKeyMatcherRegex();
}

public partial class RedisMemoryAnalysis : IMonitorStatus
{
    public RedisConnectionInfo ConnectionInfo { get; internal set; }
    public bool IsGlobal => Database == -1;
    public int Database { get; internal set; }
    public DateTime CreationDate { get; internal set; }

    public TimeSpan KeyTime { get; internal set; }
    public TimeSpan AnalysisTime { get; internal set; }
    public TimeSpan TotalTime => KeyTime + AnalysisTime;

    public List<KeyMatcher> KeyMatchers { get; internal set; }
    public ConcurrentDictionary<KeyMatcher,KeyStats> KeyStats { get; internal set; }

    public IEnumerable<TopKey> TopKeys
    {
        get
        {
            return KeyStats.SelectMany(ks => ks.Value.TopKeys.Select(tk => new TopKey {Name = tk.Value, ValueBytes = tk.Key, Matcher = ks.Key }))
                           .OrderByDescending(tk => tk.TotalBytes)
                           .Take(50);
        }
    }

    public MonitorStatus MonitorStatus
    {
        get
        {
            if (ErrorMessage.HasValue())
                return MonitorStatus.Critical;
            if (ErrorCount > 0 && Count == 0)
                return MonitorStatus.Critical;
            if (ErrorCount > 0 && Count > 0)
                return MonitorStatus.Warning;
            return MonitorStatus.Good;
        }
    }

    public string MonitorStatusReason
    {
        get
        {
            if (ErrorMessage.HasValue())
                return ErrorMessage;
            if (ErrorCount > 0 && Count == 0)
                return "Error processing all " + ErrorCount.ToComma() + " keys";
            if (ErrorCount > 0 && Count > 0)
                return "Error processing " + ErrorCount.ToComma() + " of " + (ErrorCount + Count).ToComma() + " keys";
            return null;
        }
    }

    private long _count;
    private long _keyByteSize;
    private long _valueByteSize;
    private long _errorCount;

    public long Count => _count;
    public long KeyByteSize => _keyByteSize;
    public long ValueByteSize => _valueByteSize;
    public long TotalByteSize => _keyByteSize + _valueByteSize;
    public long ErrorCount => _errorCount;

    public string ErrorMessage { get; internal set; }

    public void Analyze(ConnectionMultiplexer muxer)
    {
        // Get the keys
        var sw = Stopwatch.StartNew();

        var db = muxer.GetDatabase(Database);
        var server = muxer.GetSingleServer();
        var keys = server.Keys(Database, pageSize: 1000);
        KeyTime = sw.Elapsed;

        // Analyze each key
        sw.Restart();
        using (MiniProfiler.Current.Step("Key analysis"))
        {
            Task last = null;
            foreach (var tmpKey in keys)
            {
                var key = tmpKey;
                last = db.DebugObjectAsync(key).ContinueWith(x =>
                    {
                        try
                        {
                            TallyDebugLine(key, x.Result);
                        }
                        catch (Exception)
                        {
                            TallyError();
                        }
                    });
            }
            if (last != null) server.Wait(last);
        }
        AnalysisTime = sw.Elapsed;
        sw.Stop();
    }

    public RedisMemoryAnalysis(RedisAnalyzer analyzer, RedisConnectionInfo connectionInfo, int database)
    {
        CreationDate = DateTime.UtcNow;
        KeyStats = new ConcurrentDictionary<KeyMatcher, KeyStats>();

        ConnectionInfo = connectionInfo;
        Database = database;
        KeyMatchers = analyzer.KeyMatchers;
        if (KeyMatchers.Count == 0)
        {
            ErrorMessage = "Could not find regexes defined for " + connectionInfo;
            return;
        }
        foreach (var km in KeyMatchers)
        {
            KeyStats[km] = new KeyStats();
        }
    }

    private static readonly Regex _debugObjectSize = GetDebugObjectSizeRegex();

    internal void TallyDebugLine(string key, string debugLine)
    {
        if (debugLine == null) return;

        var match = _debugObjectSize.Match(debugLine);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out long size)) return;

        var matcher = GetKeyMatcher(key);
        var keySize = Encoding.UTF8.GetByteCount(key);
        // Global counts
        Interlocked.Increment(ref _count);
        Interlocked.Add(ref _keyByteSize, keySize);
        Interlocked.Add(ref _valueByteSize, size);

        if (matcher == null) return;

        // Per-key counts
        KeyStats[matcher].Tally(key, keySize, size);
    }

    private KeyMatcher GetKeyMatcher(string key)
    {
        foreach (var t in KeyMatchers)
            if (t.Regex.IsMatch(key)) return t;
        return null;
    }

    internal void TallyError()
    {
        Interlocked.Increment(ref _errorCount);
    }

    [GeneratedRegex(@"\bserializedlength:([0-9]+)\b", RegexOptions.Compiled)]
    private static partial Regex GetDebugObjectSizeRegex();
}

public class TopKey
{
    public string Name { get; internal set; }
    public KeyMatcher Matcher { get; internal set; }
    public int KeyBytes => Encoding.UTF8.GetByteCount(Name);
    public long ValueBytes { get; internal set; }
    public long TotalBytes => KeyBytes + ValueBytes;
}

public class KeyMatcher
{
    public string Name { get; internal set; }
    public Regex Regex { get; internal set; }
}

public class KeyStats
{
    private readonly object _lock = new();

    internal long _count;
    internal long _keyByteSize;
    internal long _valueByteSize;

    public long Count => _count;
    public long KeyByteSize => _keyByteSize;
    public long ValueByteSize => _valueByteSize;
    public long TotalByteSize => _keyByteSize + _valueByteSize;

    public readonly SortedList<long, string> TopKeys = new(50, new DescLongCompare());

    public void Tally(string key, long keySize, long valueSize)
    {
        Interlocked.Increment(ref _count);
        Interlocked.Add(ref _keyByteSize, keySize);
        Interlocked.Add(ref _valueByteSize, valueSize);

        lock (_lock) TopKeys.Add(valueSize, key);

        // Capacity cap to prevent a memory leak, but only every so often because Array.Copy isn't cheap.
        if (TopKeys.Count > 10000)
        {
            lock (_lock) TopKeys.Capacity = 50;
        }
    }

    private class DescLongCompare : IComparer<long>
    {
        public int Compare(long x, long y)
        {
            return y.CompareTo(x);
        }
    }
}
