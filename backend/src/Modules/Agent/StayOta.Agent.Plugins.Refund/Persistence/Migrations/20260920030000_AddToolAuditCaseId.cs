using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StayOta.Agent.Plugins.Refund.Persistence;

#nullable disable

namespace StayOta.Agent.Plugins.Refund.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260920030000_AddToolAuditCaseId")]
public class AddToolAuditCaseId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CaseId",
            schema: "agent_refund",
            table: "tool_audits",
            type: "text",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_tool_audits_CaseId",
            schema: "agent_refund",
            table: "tool_audits",
            column: "CaseId");

        migrationBuilder.CreateIndex(
            name: "IX_tool_audits_TraceId",
            schema: "agent_refund",
            table: "tool_audits",
            column: "TraceId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_tool_audits_CaseId",
            schema: "agent_refund",
            table: "tool_audits");

        migrationBuilder.DropIndex(
            name: "IX_tool_audits_TraceId",
            schema: "agent_refund",
            table: "tool_audits");

        migrationBuilder.DropColumn(
            name: "CaseId",
            schema: "agent_refund",
            table: "tool_audits");
    }
}
