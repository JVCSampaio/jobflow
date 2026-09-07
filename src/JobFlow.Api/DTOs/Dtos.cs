namespace JobFlow.Api.DTOs;

public record CreateJobRequest(string Name, string? Payload, int? MaxRetries = null);

public record JobDto(
    Guid Id,
    string Name,
    string Status,
    string? Payload,
    string? Result,
    string? Error,
    int Attempts,
    int MaxRetries,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    DateTime? NextRunAt);

public record RegisterRequest(string Username, string Email, string Password);

public record LoginRequest(string Username, string Password);

public record AuthResponse(string Token, string Username, string Email);
