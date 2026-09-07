using System.Collections.Concurrent;

namespace JobFlow.Api.Middleware;

/// <summary>
/// Simple in-memory per-IP fixed-window rate limiter (60 req/min by default).
/// Registered as a singleton; invoked from the pipeline with the next delegate.
/// </summary>
public class RateLimitMiddleware
{
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, (long count, DateTime reset)> _counters
        = new(StringComparer.OrdinalIgnoreCase);

    public RateLimitMiddleware(int limit, TimeSpan window)
    {
        _limit = limit;
        _window = window;
    }

    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        var now = DateTime.UtcNow;

        long allowed;
        lock (_counters)
        {
            if (_counters.TryGetValue(key, out var entry))
            {
                if (now - entry.reset < _window)
                {
                    allowed = entry.count + 1;
                    _counters[key] = (allowed, entry.reset);
                }
                else
                {
                    allowed = 1;
                    _counters[key] = (1, now);
                }
            }
            else
            {
                allowed = 1;
                _counters[key] = (1, now);
            }
        }

        if (allowed > _limit)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.Append("Retry-After", $"{(int)_window.TotalSeconds}");
            return context.Response.WriteAsJsonAsync(new { error = "rate limit exceeded" });
        }

        return next(context);
    }
}
