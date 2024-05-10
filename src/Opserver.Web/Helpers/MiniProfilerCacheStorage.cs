using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Opserver.Data;
using StackExchange.Profiling;
using StackExchange.Profiling.Storage;

namespace Opserver.Helpers;

public class MiniProfilerCacheStorage(IMemoryCache cache, PollingService poller, TimeSpan cacheDuration)
    : MemoryCacheStorage(cache, cacheDuration), IAsyncStorage
{
    private readonly PollingService _poller = poller;

    MiniProfiler IAsyncStorage.Load(Guid id) => Load(id) ?? _poller.GetCache(id)?.Profiler;
    Task<MiniProfiler> IAsyncStorage.LoadAsync(Guid id) => LoadAsync(id) ?? Task.FromResult(_poller.GetCache(id)?.Profiler);
}

internal class MiniProfilerCacheStorageDefaults(IMemoryCache cache, PollingService poller) : IConfigureOptions<MiniProfilerOptions>
{
    private readonly IMemoryCache _cache = cache;
    private readonly PollingService _poller = poller;

    public void Configure(MiniProfilerOptions options)
        => options.Storage ??= new MiniProfilerCacheStorage(_cache, _poller, TimeSpan.FromMinutes(10));
}
