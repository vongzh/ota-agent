using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StayOta.Agent.Abstractions.Domain.Entities;
using StayOta.Agent.Abstractions.Options;

namespace StayOta.Agent.Plugins.Refund.Persistence;

public sealed class AppDbContext : DbContext
{
    private readonly string _schema;

    public AppDbContext(DbContextOptions<AppDbContext> options, IOptions<AgentStorageOptions> storage)
        : base(options)
    {
        _schema = storage.Value.Schema?.Trim() ?? "";
    }

    public string Schema => _schema;

    public DbSet<HotelOrder> Orders => Set<HotelOrder>();
    public DbSet<PolicySnapshot> Policies => Set<PolicySnapshot>();
    public DbSet<RefundCase> Cases => Set<RefundCase>();
    public DbSet<CaseEvent> CaseEvents => Set<CaseEvent>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<ToolAuditLog> ToolAudits => Set<ToolAuditLog>();
    public DbSet<ScenarioFixture> Scenarios => Set<ScenarioFixture>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (!string.IsNullOrWhiteSpace(_schema))
            modelBuilder.HasDefaultSchema(_schema);

        modelBuilder.Entity<HotelOrder>(e =>
        {
            e.ToTable("orders");
            e.HasKey(x => x.OrderId);
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
        });
        modelBuilder.Entity<PolicySnapshot>(e =>
        {
            e.ToTable("policies");
            e.HasKey(x => x.PolicyId);
            e.Property(x => x.FixedFee).HasPrecision(18, 2);
        });
        modelBuilder.Entity<RefundCase>(e =>
        {
            e.ToTable("cases");
            e.HasKey(x => x.CaseId);
            e.Property(x => x.QuoteRefundAmount).HasPrecision(18, 2);
            e.Property(x => x.QuoteFeeAmount).HasPrecision(18, 2);
        });
        modelBuilder.Entity<CaseEvent>(e =>
        {
            e.ToTable("case_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<WorkflowRun>(e =>
        {
            e.ToTable("workflow_runs");
            e.HasKey(x => x.RunId);
        });
        modelBuilder.Entity<ToolAuditLog>(e =>
        {
            e.ToTable("tool_audits");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => x.TraceId);
            e.HasIndex(x => x.CaseId);
        });
        modelBuilder.Entity<ScenarioFixture>(e =>
        {
            e.ToTable("scenarios");
            e.HasKey(x => x.ScenarioId);
        });
    }
}
