using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeMentor.Infrastructure.Persistence.Configurations;

internal sealed class RepositoryFileConfiguration : IEntityTypeConfiguration<RepositoryFile>
{
    public void Configure(EntityTypeBuilder<RepositoryFile> builder)
    {
        builder.ToTable(
            "RepositoryFiles",
            table => table.HasCheckConstraint("CK_RepositoryFiles_SizeInBytes", "[SizeInBytes] >= 0"));
        builder.HasKey(file => file.Id);

        builder.Property(file => file.RelativePath)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(file => file.Language)
            .HasMaxLength(100);

        builder.Property(file => file.ContentHash)
            .HasMaxLength(128);

        builder.Property(file => file.SizeInBytes).IsRequired();
        builder.Property(file => file.CreatedAtUtc).IsRequired();
        builder.Property(file => file.UpdatedAtUtc).IsRequired();

        builder.HasIndex(file => new { file.ProjectId, file.RelativePath })
            .IsUnique();

        builder.HasMany(file => file.Chunks)
            .WithOne(chunk => chunk.RepositoryFile)
            .HasForeignKey(chunk => chunk.RepositoryFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
