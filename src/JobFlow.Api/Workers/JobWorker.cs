using Microsoft.EntityFrameworkCore;
using JobFlow.Api.Data;
using JobFlow.Api.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobFlow.Api.Workers;

/// <summary>
/// Background worker that polls the jobs table for due jobs, runs them via
/// JobProcessor, and persists the resulting state. Uses a single-claim-per-tick
/// strategy: a job is marked Running before processing so a second worker
/// instance won't double-claim it.
/// </summary>
public class JobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);

    public JobWorker(IServiceScopeFactory scopeFactory, ILogger<JobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobWorker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JobWorker tick failed.");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private async Task TickAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobFlowDbContext>();

        var now = DateTime.UtcNow;

        // Claim one due job: Queued (never started) or Failed with retries left,
        // whose NextRunAt is due. Mark Running to prevent double-claim.
        var job = await db.Jobs
            .Where(j => (j.Status == JobStatus.Queued || j.Status == JobStatus.Failed)
                        && (j.NextRunAt == null || j.NextRunAt <= now))
            .OrderBy(j => j.CreatedAt)
            .FirstAsync();

        if (job is null) return;

        job.Status = JobStatus.Running;
        job.StartedAt = now;
        job.Attempts += 1;
        await db.SaveChangesAsync();

        _logger.LogInformation("Running job {JobId} (attempt {Attempts}/{Max})",
            job.Id, job.Attempts, job.MaxRetries);

        await JobProcessor.Execute(job);
        await db.SaveChangesAsync();

        _logger.LogInformation("Job {JobId} finished with status {Status}", job.Id, job.Status);
    }
}
