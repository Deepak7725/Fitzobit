using Fitzobit.Application.Customers;

namespace Fitzobit.Web.Infrastructure;

/// <summary>
/// Runs the daily 12 pm customer rules (<see cref="CustomerAutomationService"/>). Checks at start-up and then every few minutes
/// whether a day is due, so a server that was asleep at 12 pm (free Render plan) catches up as soon as it wakes. Turn off with
/// Automation:Enabled=false.
/// </summary>
public sealed class CustomerAutomationWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<CustomerAutomationWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Automation:Enabled", true)) return;
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } // let start-up (migrations) finish first
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                foreach (var r in await scope.ServiceProvider.GetRequiredService<CustomerAutomationService>().RunDueAsync(stoppingToken))
                    logger.LogInformation("12 pm run for {Day:yyyy-MM-dd}: {Past} package(s) to past, {Inactive} inactive, {Active} active, {Paused} paused",
                        r.Day, r.PackagesMovedToPast, r.MadeInactive, r.MadeActive, r.MadePaused);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Daily 12 pm customer run failed; it is retried in a few minutes");
            }

            try { await Task.Delay(CheckEvery, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
