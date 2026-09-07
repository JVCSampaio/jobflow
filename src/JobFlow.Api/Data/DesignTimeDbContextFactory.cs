using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobFlow.Api.Data;

/// <summary>
/// Design-time factory used by `dotnet ef` to create a DbContext for migrations
/// and queries without booting the full application (which requires JWT_SIGNING_KEY).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<JobFlowDbContext>
{
    public JobFlowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<JobFlowDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=jobflow;Username=jobflow;Password=jobflow")
            .Options;
        return new JobFlowDbContext(options);
    }
}
