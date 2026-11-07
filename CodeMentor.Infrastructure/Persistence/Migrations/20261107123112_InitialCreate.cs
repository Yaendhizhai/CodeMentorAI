using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeMentor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RepositoryProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RepositoryUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryProjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Queued"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisSessions", x => x.Id);
                    table.CheckConstraint("CK_AnalysisSessions_Status", "[Status] IN ('Queued', 'Running', 'Completed', 'Failed', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_AnalysisSessions_RepositoryProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "RepositoryProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conversations_RepositoryProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "RepositoryProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SizeInBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryFiles", x => x.Id);
                    table.CheckConstraint("CK_RepositoryFiles_SizeInBytes", "[SizeInBytes] >= 0");
                    table.ForeignKey(
                        name: "FK_RepositoryFiles_RepositoryProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "RepositoryProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                    table.CheckConstraint("CK_ConversationMessages_Role", "[Role] IN ('System', 'User', 'Assistant')");
                    table.CheckConstraint("CK_ConversationMessages_Sequence", "[Sequence] >= 0");
                    table.ForeignKey(
                        name: "FK_ConversationMessages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CodeChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepositoryFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChunkIndex = table.Column<int>(type: "int", nullable: false),
                    StartLine = table.Column<int>(type: "int", nullable: false),
                    EndLine = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeChunks", x => x.Id);
                    table.CheckConstraint("CK_CodeChunks_ChunkIndex", "[ChunkIndex] >= 0");
                    table.CheckConstraint("CK_CodeChunks_EndLine", "[EndLine] >= [StartLine]");
                    table.CheckConstraint("CK_CodeChunks_StartLine", "[StartLine] >= 1");
                    table.ForeignKey(
                        name: "FK_CodeChunks_RepositoryFiles_RepositoryFileId",
                        column: x => x.RepositoryFileId,
                        principalTable: "RepositoryFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisSessions_ProjectId_CreatedAtUtc",
                table: "AnalysisSessions",
                columns: new[] { "ProjectId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeChunks_RepositoryFileId_ChunkIndex",
                table: "CodeChunks",
                columns: new[] { "RepositoryFileId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationId_Sequence",
                table: "ConversationMessages",
                columns: new[] { "ConversationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ProjectId_CreatedAtUtc",
                table: "Conversations",
                columns: new[] { "ProjectId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryFiles_ProjectId_RelativePath",
                table: "RepositoryFiles",
                columns: new[] { "ProjectId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryProjects_Name",
                table: "RepositoryProjects",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalysisSessions");

            migrationBuilder.DropTable(
                name: "CodeChunks");

            migrationBuilder.DropTable(
                name: "ConversationMessages");

            migrationBuilder.DropTable(
                name: "RepositoryFiles");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "RepositoryProjects");
        }
    }
}
