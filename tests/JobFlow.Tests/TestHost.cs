using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using JobFlow.Api.Data;

namespace JobFlow.Tests;

/// <summary>
/// Stands up the real JobFlow API (controllers, middleware, JWT) against an
/// in-memory EF Core database via <see cref="WebApplicationFactory{TEntryPoint}"/>.
///
/// <see cref="WebApplicationFactory{TEntryPoint}"/> caches ONE host per entry
/// point, so every factory instance shares the same host (and its in-memory
/// DB + rate limiter). We therefore create a single shared factory and never
/// dispose it mid-run — doing so would tear down the host that other tests'
/// <c>CreateClient()</c> still references (the 401s seen when tests ran
/// together). Each test uses a UNIQUE user so register/login never collide.
/// </summary>
public sealed class TestHost
{
    private static readonly object _gate = new();
    private static WebApplicationFactory<JobFlowApi>? _sharedFactory;

    private readonly WebApplicationFactory<JobFlowApi> _factory;
    private readonly HttpClient _client;
    private readonly HttpClient _anon;

    public TestHost()
    {
        lock (_gate)
        {
            if (_sharedFactory is null)
            {
                Environment.SetEnvironmentVariable("USE_INMEMORY_DB", "true");
                Environment.SetEnvironmentVariable("RUN_MIGRATIONS_ON_STARTUP", "false");
                Environment.SetEnvironmentVariable("RUN_WORKER", "false");
                Environment.SetEnvironmentVariable("JWT_SIGNING_KEY", "test-signing-key-32-bytes-minimum!!");

                _sharedFactory = new WebApplicationFactory<JobFlowApi>();
                using var scope = _sharedFactory.Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<JobFlowDbContext>().Database.EnsureCreated();
            }
            _factory = _sharedFactory;
        }

        _client = _factory.CreateClient();
        _anon = _factory.CreateClient();
    }

    public HttpClient CreateClient() => _client;
    public HttpClient CreateAnonClient() => _anon;
    public IServiceProvider Services => _factory.Services;
}