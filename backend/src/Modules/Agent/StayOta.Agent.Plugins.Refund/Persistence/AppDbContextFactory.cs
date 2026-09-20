using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
using StayOta.Agent.Abstractions.Options;

namespace StayOta.Agent.Plugins.Refund.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations</c>.
/// Uses default schema <c>agent</c> matching demo Hosting defaults.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=5432;Database=stayota_agent;Username=stayota;Password=stayota")
            .Options;

        var storage = Options.Create(new AgentStorageOptions { Schema = "agent" });
        return new AppDbContext(options, storage);
    }
}
