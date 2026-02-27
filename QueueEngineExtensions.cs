using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueueEngine.Config;
using QueueEngine.Core;
using QueueEngine.Data;
using QueueEngine.Workers;

namespace QueueEngine;

public static class QueueEngineExtensions
{
    public static IServiceCollection AddQueueEngine(
        this IServiceCollection services,
        Action<QueueEngineOptions> configure)
    {
        var options = new QueueEngineOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<IJobRepository, JobRepository>();
        services.AddSingleton<IQueueWorkerPool>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<QueueWorkerPool>>();
            return new QueueWorkerPool(
                sp.GetRequiredService<IJobRepository>(),
                new Dictionary<string, IJobHandler>(),
                options,
                logger);
        });
        services.AddSingleton<IQueueEngine>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<QueueEngine.Core.QueueEngine>>();
            return new QueueEngine.Core.QueueEngine(
                sp.GetRequiredService<IJobRepository>(),
                sp.GetRequiredService<IQueueWorkerPool>(),
                options,
                logger);
        });
        
        return services;
    }

    public static IServiceCollection AddJobHandler<T>(this IServiceCollection services) where T : class, IJobHandler
    {
        services.AddSingleton<IJobHandler, T>();
        return services;
    }

    public static IServiceCollection AddQueueEngineHostedService(this IServiceCollection services)
    {
        services.AddHostedService<QueueEngineHostedService>();
        return services;
    }

    private class QueueEngineHostedService : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly IQueueEngine _engine;
        private readonly IEnumerable<IJobHandler> _handlers;

        public QueueEngineHostedService(
            IQueueEngine engine,
            IEnumerable<IJobHandler> handlers)
        {
            _engine = engine;
            _handlers = handlers;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var handler in _handlers)
            {
                _engine.RegisterHandler(handler);
            }
            await _engine.StartAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _engine.StopAsync();
        }
    }
}
