using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using JobFlow.Api.Data;
using JobFlow.Api.Services;

namespace JobFlow.Tests;

public class UserServiceTests : IAsyncLifetime
{
    private JobFlowDbContext _db = null!;
    private const string Key = "test-signing-key-32-chars-minimum-00";

    public Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("JWT_SIGNING_KEY", Key);
        var options = new DbContextOptionsBuilder<JobFlowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new JobFlowDbContext(options);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private UserService NewService() =>
        new UserService(_db, LoggerFactory.Create(b => { }).CreateLogger<UserService>());

    [Fact]
    public async Task Register_HashesPassword_WithBcrypt()
    {
        var user = await NewService().RegisterAsync("alice", "alice@example.com", "secret123");

        Assert.NotEqual("secret123", user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("secret123", user.PasswordHash));
    }

    [Fact]
    public async Task Register_RejectsDuplicateUsername()
    {
        var service = NewService();
        await service.RegisterAsync("bob", "bob@example.com", "password123");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RegisterAsync("bob", "bob2@example.com", "password123"));
    }

    [Fact]
    public async Task Authenticate_ReturnsUser_ForCorrectPassword()
    {
        var service = NewService();
        await service.RegisterAsync("carol", "carol@example.com", "password123");

        var user = await service.AuthenticateAsync("carol", "password123");

        Assert.NotNull(user);
        Assert.Equal("carol", user.Username);
    }

    [Fact]
    public async Task Authenticate_ReturnsNull_ForWrongPassword()
    {
        var service = NewService();
        await service.RegisterAsync("dave", "dave@example.com", "password123");

        Assert.Null(await service.AuthenticateAsync("dave", "wrong"));
    }

    [Fact]
    public async Task IssueToken_ReturnsJwt_ContainingUsername()
    {
        var service = NewService();
        var user = await service.RegisterAsync("erin", "erin@example.com", "password123");

        var token = service.IssueToken(user);

        // Decode the JWT payload (base64url) and verify the username claim.
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
        var json = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(payload));
        Assert.Contains("erin", json);
    }
}
