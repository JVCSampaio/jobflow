using JobFlow.Api.Data.Entities;
using JobFlow.Api.Workers;

namespace JobFlow.Tests;

public class JobProcessorTests
{
    private static Job NewJob(string? payload = "hello", int maxRetries = 3)
    {
        return new Job
        {
            Name = "test-job",
            Payload = payload,
            MaxRetries = maxRetries,
            Status = JobStatus.Queued,
            Attempts = 0
        };
    }

    [Fact]
    public async Task Success_ReturnsSucceeded_WithResult()
    {
        var job = NewJob();

        await JobProcessor.Execute(job);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.NotNull(job.Result);
        Assert.Null(job.Error);
        Assert.NotNull(job.FinishedAt);
        Assert.Null(job.NextRunAt);
    }

    [Fact]
    public async Task Failure_WithRetriesLeft_MarksFailed_SchedulesBackoff()
    {
        var job = NewJob("boom"); // "boom" triggers a failure

        await JobProcessor.Execute(job);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.NotNull(job.Error);
        Assert.NotNull(job.NextRunAt);
        // Backoff for attempt 1 is 2^(1-1) = 1 second.
        var delay = job.NextRunAt!.Value - job.FinishedAt!.Value;
        Assert.InRange(delay.TotalSeconds, 0.5, 2.0);
    }

    [Fact]
    public async Task Failure_ExhaustsRetries_MarksFailed_NoNextRun()
    {
        var job = NewJob("boom", maxRetries: 1);
        job.Attempts = 1; // already used the only retry

        await JobProcessor.Execute(job);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Null(job.NextRunAt); // permanently failed
    }

    [Fact]
    public async Task EmptyPayload_StillSucceeds()
    {
        var job = NewJob(null);

        await JobProcessor.Execute(job);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Contains("empty payload", job.Result);
    }
}
