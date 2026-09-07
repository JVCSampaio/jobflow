using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobFlow.Api.Data;
using JobFlow.Api.Data.Entities;
using JobFlow.Api.DTOs;
using JobFlow.Api.Services;

namespace JobFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly JobFlowDbContext _db;

    public JobsController(JobFlowDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(JobDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateJobRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "name is required" });

        var job = new Job
        {
            Name = req.Name,
            Payload = req.Payload,
            MaxRetries = req.MaxRetries ?? 3,
            Status = JobStatus.Queued,
            NextRunAt = DateTime.UtcNow
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        return Created($"/api/jobs/{job.Id}", ToDto(job));
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(JobDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        var job = await _db.Jobs.FindAsync(id)
            ?? throw new KeyNotFoundException($"Job {id} not found.");
        return Ok(ToDto(job));
    }

    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(JobDto[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int take = 50)
    {
        var jobs = await _db.Jobs
            .OrderByDescending(j => j.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync();
        return Ok(jobs.Select(ToDto));
    }

    [HttpPost("{id:guid}/retry")]
    [Authorize]
    [ProducesResponseType(typeof(JobDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Retry(Guid id)
    {
        var job = await _db.Jobs.FindAsync(id)
            ?? throw new KeyNotFoundException($"Job {id} not found.");

        if (job.Status == JobStatus.Running)
            return BadRequest(new { error = "job is already running" });

        job.Status = JobStatus.Queued;
        job.Attempts = 0;
        job.Error = null;
        job.Result = null;
        job.StartedAt = null;
        job.FinishedAt = null;
        job.NextRunAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(ToDto(job));
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var job = await _db.Jobs.FindAsync(id)
            ?? throw new KeyNotFoundException($"Job {id} not found.");
        _db.Jobs.Remove(job);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static JobDto ToDto(Job j) => new(
        j.Id, j.Name, j.Status.ToString(), j.Payload, j.Result, j.Error,
        j.Attempts, j.MaxRetries, j.CreatedAt, j.StartedAt, j.FinishedAt, j.NextRunAt);
}
