using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using JobFlow.Api.Data;
using JobFlow.Api.Middleware;
using JobFlow.Api.Services;
using JobFlow.Api.Workers;

/// <summary>
/// The application entry point. Non-static and exposed so tests can use
/// <c>WebApplicationFactory&lt;JobFlowApi&gt;</c> to stand up the real app with
/// an in-memory database. When <c>RUN_MIGRATIONS_ON_STARTUP=false</c> the
/// background worker and startup migrations are skipped, so tests run without a
/// real Postgres or network access.
/// </summary>
public class JobFlowApi
{
    public static async Task Main(string[] args)
    {
        var app = new JobFlowApi().CreateBuilder(args).Build();
        JobFlowApi.ConfigurePipeline(app);

        // --- Migrations on startup (skip in tests) ---
        if (Environment.GetEnvironmentVariable("RUN_MIGRATIONS_ON_STARTUP") != "false")
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JobFlowDbContext>();
            await db.Database.MigrateAsync();
        }

        await app.RunAsync();
    }

    /// <summary>
    /// Builds the host configuration (DI + middleware registration). Shared by
    /// <see cref="Main"/> and <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>.
    /// </summary>
    public WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // --- Logging ---
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.IncludeScopes = false;
        });

        // --- JWT auth ---
        var signingKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
            ?? "test-signing-key-32-bytes-minimum!!";
        var keyBytes = Encoding.UTF8.GetBytes(signingKey);

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "jobflow",
                    ValidateAudience = true,
                    ValidAudience = "jobflow",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        builder.Services.AddAuthorization();

        // --- DbContext (InMemory in tests, Npgsql in production) ---
        if (Environment.GetEnvironmentVariable("USE_INMEMORY_DB") == "true")
        {
            // One store per host: every scoped DbContext in this app instance
            // shares the same database, so data persists across requests.
            var dbName = $"jobflow-test-{Guid.NewGuid()}";
            builder.Services.AddDbContext<JobFlowDbContext>(o =>
                o.UseInMemoryDatabase(dbName));
        }
        else
        {
            var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? "Host=localhost;Port=5432;Database=jobflow;Username=jobflow;Password=jobflow";
            builder.Services.AddDbContext<JobFlowDbContext>(o => o.UseNpgsql(connectionString));
        }

        // --- MVC controllers ---
        builder.Services.AddControllers();

        // --- OpenAPI document consumed by Swagger UI ---
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info ??= new Microsoft.OpenApi.OpenApiInfo();
                document.Info.Title = "JobFlow";
                document.Info.Version = "v1";
                document.Info.Description =
                    "API de jobs com autenticação JWT, rate limiting e processamento em segundo plano.";
                return Task.CompletedTask;
            });
        });

        // --- Services ---
        builder.Services.AddScoped<UserService>();

        // --- Rate limiter (singleton, in-memory per-IP window) ---
        builder.Services.AddSingleton<RateLimitMiddleware>(
            sp => new RateLimitMiddleware(limit: 60, window: TimeSpan.FromMinutes(1)));

        // --- Background worker (skipped in tests) ---
        if (Environment.GetEnvironmentVariable("RUN_MIGRATIONS_ON_STARTUP") != "false")
        {
            builder.Services.AddHostedService<JobWorker>();
        }

        return builder;
    }

    /// <summary>
    /// Configures the request pipeline. Shared by <see cref="Main"/> and the test
    /// host so both produce an identical, fully-configured app.
    /// </summary>
    public static void ConfigurePipeline(WebApplication app)
    {
        // Swagger UI is registered first so its static assets are not rate limited.
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "JobFlow v1");
            options.DocumentTitle = "JobFlow API";
            options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
        });

        app.UseMiddleware<ExceptionMiddleware>();

        // Rate limiter is a singleton (no RequestDelegate ctor), so invoke it
        // manually via a delegate that pulls the instance from DI.
        app.Use(async (ctx, next) =>
        {
            var limiter = ctx.RequestServices.GetRequiredService<RateLimitMiddleware>();
            await limiter.InvokeAsync(ctx, next);
        });

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapOpenApi();
    }
}
