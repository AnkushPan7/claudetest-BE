using ClaudeCertPractice.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaudeCertPractice.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260717120000_EnsureResultQuestionOptionExplanations")]
    public partial class EnsureResultQuestionOptionExplanations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Covers production DBs where AddResultQuestionOptionExplanations ran with an empty Up().
            migrationBuilder.Sql(
                """
                ALTER TABLE "ResultQuestions"
                ADD COLUMN IF NOT EXISTS "OptionExplanations" jsonb NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: dropping would lose data if older migration also owns the column.
        }
    }
}
