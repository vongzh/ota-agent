using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StayOta.Agent.Plugins.Refund.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "agent");

            migrationBuilder.CreateTable(
                name: "case_events",
                schema: "agent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CaseId = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cases",
                schema: "agent",
                columns: table => new
                {
                    CaseId = table.Column<string>(type: "text", nullable: false),
                    OrderId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ScenarioId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RiskLevel = table.Column<int>(type: "integer", nullable: false),
                    Intent = table.Column<string>(type: "text", nullable: true),
                    RecommendedAction = table.Column<string>(type: "text", nullable: false),
                    QuoteRefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    QuoteFeeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ConversationState = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cases", x => x.CaseId);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "agent",
                columns: table => new
                {
                    OrderId = table.Column<string>(type: "text", nullable: false),
                    ScenarioId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    HotelName = table.Column<string>(type: "text", nullable: false),
                    PolicyId = table.Column<string>(type: "text", nullable: false),
                    PaymentId = table.Column<string>(type: "text", nullable: false),
                    RefundId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CheckIn = table.Column<DateOnly>(type: "date", nullable: false),
                    CheckOut = table.Column<DateOnly>(type: "date", nullable: false),
                    RoomType = table.Column<string>(type: "text", nullable: false),
                    RoomCount = table.Column<int>(type: "integer", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    UserOnSite = table.Column<bool>(type: "boolean", nullable: false),
                    ChannelType = table.Column<string>(type: "text", nullable: false),
                    ExtraJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "policies",
                schema: "agent",
                columns: table => new
                {
                    PolicyId = table.Column<string>(type: "text", nullable: false),
                    ScenarioId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    RuleCode = table.Column<string>(type: "text", nullable: false),
                    FreeCancel = table.Column<bool>(type: "boolean", nullable: false),
                    FixedFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Authority = table.Column<string>(type: "text", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policies", x => x.PolicyId);
                });

            migrationBuilder.CreateTable(
                name: "scenarios",
                schema: "agent",
                columns: table => new
                {
                    ScenarioId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    EntryMessage = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    OrderId = table.Column<string>(type: "text", nullable: false),
                    CaseId = table.Column<string>(type: "text", nullable: false),
                    RiskLevel = table.Column<int>(type: "integer", nullable: false),
                    ExpectedRoute = table.Column<string>(type: "text", nullable: false),
                    ExpectedCaseStatus = table.Column<string>(type: "text", nullable: false),
                    RequiredToolsJson = table.Column<string>(type: "text", nullable: false),
                    ExpectedStatesJson = table.Column<string>(type: "text", nullable: false),
                    SuccessAssertionsJson = table.Column<string>(type: "text", nullable: false),
                    Group = table.Column<string>(type: "text", nullable: false),
                    Goal = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenarios", x => x.ScenarioId);
                });

            migrationBuilder.CreateTable(
                name: "tool_audits",
                schema: "agent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TraceId = table.Column<string>(type: "text", nullable: false),
                    ToolName = table.Column<string>(type: "text", nullable: false),
                    Access = table.Column<int>(type: "integer", nullable: false),
                    Allowed = table.Column<bool>(type: "boolean", nullable: false),
                    RequestJson = table.Column<string>(type: "text", nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    DenyReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_audits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_runs",
                schema: "agent",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "text", nullable: false),
                    CaseId = table.Column<string>(type: "text", nullable: false),
                    ScenarioId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TraceJson = table.Column<string>(type: "text", nullable: false),
                    ToolSequenceJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_runs", x => x.RunId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_events",
                schema: "agent");

            migrationBuilder.DropTable(
                name: "cases",
                schema: "agent");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "agent");

            migrationBuilder.DropTable(
                name: "policies",
                schema: "agent");

            migrationBuilder.DropTable(
                name: "scenarios",
                schema: "agent");

            migrationBuilder.DropTable(
                name: "tool_audits",
                schema: "agent");

            migrationBuilder.DropTable(
                name: "workflow_runs",
                schema: "agent");
        }
    }
}
