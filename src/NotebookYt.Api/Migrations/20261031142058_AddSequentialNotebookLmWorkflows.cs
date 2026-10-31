using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotebookYt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSequentialNotebookLmWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ExpertRun_Status",
                table: "ExpertWorkflowRun");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowId",
                table: "ExpertWorkflowRun",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowRequestHash",
                table: "ExpertWorkflowRun",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertWorkflowRun_WorkflowId_Sequence",
                table: "ExpertWorkflowRun",
                columns: new[] { "WorkflowId", "Sequence" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExpertRun_Status",
                table: "ExpertWorkflowRun",
                sql: "\"Status\" IN ('Pending','WaitingForApproval','Running','AwaitingReview','Approved','Rejected','Blocked','OutcomeUnknown','Failed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExpertWorkflowRun_WorkflowId_Sequence",
                table: "ExpertWorkflowRun");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ExpertRun_Status",
                table: "ExpertWorkflowRun");

            migrationBuilder.DropColumn(
                name: "WorkflowId",
                table: "ExpertWorkflowRun");

            migrationBuilder.DropColumn(
                name: "WorkflowRequestHash",
                table: "ExpertWorkflowRun");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExpertRun_Status",
                table: "ExpertWorkflowRun",
                sql: "\"Status\" IN ('Pending','Running','AwaitingReview','Approved','Rejected','OutcomeUnknown','Failed')");
        }
    }
}
