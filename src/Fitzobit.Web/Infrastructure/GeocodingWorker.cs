using Fitzobit.Application.Geocoding;
using Microsoft.Extensions.Options;

namespace Fitzobit.Web.Infrastructure;

/// <summary>
/// Looks up customer locations in the background, so saving a customer never waits for (or fails because of) the
/// geocoding provider. Wakes when customers are saved (<see cref="IGeocodingQueue"/>) and otherwise every minute to pick up
/// scheduled retries. Turn off with Geocoding:BackgroundEnabled=false.
/// </summary>
public sealed class GeocodingWorker(IServiceScopeFactory scopes, IGeocodingQueue queue, IOptions<GeocodingOptions> options,
    IConfiguration configuration, ILogger<GeocodingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleWait = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Geocoding:BackgroundEnabled", true)) return;
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // let start-up (migrations) finish first

        while (!stoppingToken.IsCancellationRequested)
        {
            var batchSize = Math.Max(1, options.Value.BatchSize);
            var fullBatch = false;
            try
            {
                using var scope = scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<CustomerGeocodingService>().ProcessDueAsync(batchSize, stoppingToken);
                fullBatch = result.Processed >= batchSize && result.Retrying == 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Background geocoding batch failed");
            }

            if (fullBatch) continue; // more customers are waiting
            try { await queue.WaitAsync(IdleWait, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
