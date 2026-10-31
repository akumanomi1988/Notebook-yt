using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotebookYt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticNotebookLmWorkflowMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ExpertRun_Status",
                table: "ExpertWorkflowRun");

            migrationBuilder.AddColumn<string>(
                name: "WorkflowMode",
                table: "ExpertWorkflowRun",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Supervised");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExpertRun_Status",
                table: "ExpertWorkflowRun",
                sql: "\"Status\" IN ('Pending','WaitingForApproval','WaitingForExecution','Running','AwaitingReview','Approved','Rejected','Blocked','OutcomeUnknown','Failed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ExpertRun_Status",
                table: "ExpertWorkflowRun");

            migrationBuilder.DropColumn(
                name: "WorkflowMode",
                table: "ExpertWorkflowRun");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExpertRun_Status",
                table: "ExpertWorkflowRun",
                sql: "\"Status\" IN ('Pending','WaitingForApproval','Running','AwaitingReview','Approved','Rejected','Blocked','OutcomeUnknown','Failed')");
        }
    }
}
