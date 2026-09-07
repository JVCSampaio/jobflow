namespace JobFlow.Api.Workers;

using JobFlow.Api.Data.Entities;

/// <summary>
/// Pure job-processing + retry/backoff state machine. No DB access — the worker
/// claims a job, hands it here, and persists whatever state we return. This keeps
/// the retry logic directly unit-testable.
/// </summary>
public class JobProcessor
{
    public static Task<string> ProcessAsync(Job job)
    {
        return Task.Run(() =>
        {
            // Simulate variable work latency.
            Thread.Sleep(Random.Shared.Next(20, 120));

            // Demonstrate a failing path: payloads containing "boom" throw.
            if (job.Payload is not null && job.Payload.Contains("boom", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"job payload requested failure: {job.Payload}");
            }

            return string.IsNullOrWhiteSpace(job.Payload)
                ? $"ok: empty payload for job {job.Id}"
                : $"processed: {job.Payload}";
        });
    }

    /// <summary>
    /// Runs the job and applies the retry/backoff state machine. Returns the job
    /// with Status, Result, Error, FinishedAt, and NextRunAt updated.
    /// </summary>
    public static async Task<Job> Execute(Job job)
    {
        try
        {
            job.Result = await ProcessAsync(job);
            job.Status = JobStatus.Succeeded;
            job.Error = null;
            job.FinishedAt = DateTime.UtcNow;
            job.NextRunAt = null;
        }
        catch (Exception ex)
        {
            job.Error = ex.Message;
            job.FinishedAt = DateTime.UtcNow;

            if (job.Attempts < job.MaxRetries)
            {
                job.Status = JobStatus.Failed;
                // Exponential backoff: 1s, 2s, 4s, ...
                var delay = TimeSpan.FromSeconds(Math.Pow(2, job.Attempts - 1));
                job.NextRunAt = DateTime.UtcNow + delay;
            }
            else
            {
                job.Status = JobStatus.Failed;
                job.NextRunAt = null; // permanently failed
            }
        }

        return job;
    }
}
