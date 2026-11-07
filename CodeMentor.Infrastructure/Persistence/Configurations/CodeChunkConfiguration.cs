using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeMentor.Infrastructure.Persistence.Configurations;

internal sealed class CodeChunkConfiguration : IEntityTypeConfiguration<CodeChunk>
{
    public void Configure(EntityTypeBuilder<CodeChunk> builder)
    {
        builder.ToTable(
            "CodeChunks",
            table =>
            {
                table.HasCheckConstraint("CK_CodeChunks_ChunkIndex", "[ChunkIndex] >= 0");
                table.HasCheckConstraint("CK_CodeChunks_StartLine", "[StartLine] >= 1");
                table.HasCheckConstraint("CK_CodeChunks_EndLine", "[EndLine] >= [StartLine]");
            });
        builder.HasKey(chunk => chunk.Id);

        builder.Property(chunk => chunk.Content)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(chunk => chunk.ChunkIndex).IsRequired();
        builder.Property(chunk => chunk.StartLine).IsRequired();
        builder.Property(chunk => chunk.EndLine).IsRequired();
        builder.Property(chunk => chunk.CreatedAtUtc).IsRequired();

        builder.HasIndex(chunk => new { chunk.RepositoryFileId, chunk.ChunkIndex })
            .IsUnique();
    }
}
