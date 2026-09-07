using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JobFlow.Api.DTOs;

namespace JobFlow.Tests;

/// <summary>
/// Integration tests for the JobFlow API. They exercise the real controllers,
/// middleware, JWT auth, and an in-memory EF Core database.
///
/// Because <see cref="WebApplicationFactory{TEntryPoint}"/> caches a single host
/// per entry point, every test instance here shares one database and one rate
/// limiter. Each test therefore registers its own UNIQUE user so no two tests
/// collide on credentials or JWT state.
/// </summary>
public class ApiIntegrationTests : IAsyncLifetime
{
    private TestHost _host = null!;
    private HttpClient _client = null!;
    private HttpClient _anon = null!;

    public Task InitializeAsync()
    {
        _host = new TestHost();
        _client = _host.CreateClient();
        _anon = _host.CreateAnonClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _anon.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers a fresh, uniquely-named user and returns its JWT so each test
    /// is fully isolated from every other test.
    /// </summary>
    private async Task<string> RegisterUser()
    {
        var username = $"user-{Guid.NewGuid():N}";
        var res = await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(username, $"{username}@example.com", "password123"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth?.Token);
        return auth.Token;
    }

    private HttpClient AuthedClient(string token)
    {
        var c = _host.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Jobs_RequireAuth()
    {
        var res = await _anon.GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task CreateJob_Returns201_WithQueuedStatus()
    {
        var token = await RegisterUser();
        using var c = AuthedClient(token);

        var res = await c.PostAsJsonAsync("/api/jobs",
            new CreateJobRequest("my-job", "payload-data"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var job = await res.Content.ReadFromJsonAsync<JobDto>();
        Assert.Equal("my-job", job.Name);
        Assert.Equal("Queued", job.Status);
    }

    [Fact]
    public async Task CreateJob_WithoutName_Returns400()
    {
        var token = await RegisterUser();
        using var c = AuthedClient(token);

        var res = await c.PostAsJsonAsync("/api/jobs",
            new CreateJobRequest("", "payload"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task GetJob_ReturnsTheCreatedJob()
    {
        var token = await RegisterUser();
        using var c = AuthedClient(token);

        var created = await c.PostAsJsonAsync("/api/jobs",
            new CreateJobRequest("fetch-me", "x"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var job = await created.Content.ReadFromJsonAsync<JobDto>();

        var res = await c.GetAsync($"/api/jobs/{job.Id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var fetched = await res.Content.ReadFromJsonAsync<JobDto>();
        Assert.Equal(job.Id, fetched.Id);
        Assert.Equal("fetch-me", fetched.Name);
    }

    [Fact]
    public async Task GetJob_Missing_Returns404()
    {
        var token = await RegisterUser();
        using var c = AuthedClient(token);

        var res = await c.GetAsync($"/api/jobs/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task ListJobs_ReturnsAll()
    {
        var token = await RegisterUser();
        using var c = AuthedClient(token);

        await c.PostAsJsonAsync("/api/jobs", new CreateJobRequest("a", "1"));
        await c.PostAsJsonAsync("/api/jobs", new CreateJobRequest("b", "2"));

        var res = await c.GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var jobs = await res.Content.ReadFromJsonAsync<List<JobDto>>();
        Assert.True(jobs.Count >= 2);
    }

    [Fact]
    public async Task RetryQueuedJob_ResetsToQueued()
    {
        var token = await RegisterUser();
        using var c = AuthedClient(token);

        var created = await c.PostAsJsonAsync("/api/jobs",
            new CreateJobRequest("retry-me", "x"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var job = await created.Content.ReadFromJsonAsync<JobDto>();

        var res = await c.PostAsync($"/api/jobs/{job.Id}/retry", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var updated = await res.Content.ReadFromJsonAsync<JobDto>();
        Assert.Equal("Queued", updated.Status);
    }

    [Fact]
    public async Task DeleteJob_Returns204()
    {
        var token = await RegisterUser();
        using var c = AuthedClient(token);

        var created = await c.PostAsJsonAsync("/api/jobs",
            new CreateJobRequest("to-delete", "x"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var job = await created.Content.ReadFromJsonAsync<JobDto>();

        var res = await c.DeleteAsync($"/api/jobs/{job.Id}");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var after = await c.GetAsync($"/api/jobs/{job.Id}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Login_Returns200_WithToken()
    {
        var username = $"login-user-{Guid.NewGuid():N}";
        await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(username, $"{username}@example.com", "password123"));

        var res = await _anon.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(username, "password123"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth?.Token);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var username = $"login-user-{Guid.NewGuid():N}";
        await _anon.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(username, $"{username}@example.com", "password123"));

        var res = await _anon.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(username, "wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}