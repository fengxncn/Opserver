﻿using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Opserver.Data;

public partial class PollingService(IMemoryCache cache, ILogger<PollingService> logger) : IHostedService
{
    private DateTime _startTime;
    private readonly ILogger _logger = logger;
    private CancellationToken _cancellationToken;

    public IMemoryCache MemCache { get; } = cache;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        _logger.LogInformation("Polling service is starting.");
        _startTime = DateTime.UtcNow;
        _globalPollingThread ??= new Thread(MonitorPollingLoop)
        {
            Name = "GlobalPolling",
            Priority = ThreadPriority.Lowest,
            IsBackground = true
        };
        if (!_globalPollingThread.IsAlive)
        {
            _globalPollingThread.Start();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Taken care of by _cancellationToken
        _logger.LogInformation("Polling service is stopping.");
        return Task.CompletedTask;
    }
}
