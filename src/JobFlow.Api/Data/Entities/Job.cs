namespace JobFlow.Api.Data.Entities;

public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public string? Payload { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public int MaxRetries { get; set; } = 3;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? NextRunAt { get; set; }
}
