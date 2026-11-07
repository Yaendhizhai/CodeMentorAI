using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeMentor.Infrastructure.Persistence.Configurations;

internal sealed class RepositoryProjectConfiguration : IEntityTypeConfiguration<RepositoryProject>
{
    public void Configure(EntityTypeBuilder<RepositoryProject> builder)
    {
        builder.ToTable("RepositoryProjects");
        builder.HasKey(project => project.Id);

        builder.Property(project => project.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(project => project.RepositoryUrl)
            .HasMaxLength(2048);

        builder.Property(project => project.Description)
            .HasMaxLength(2000);

        builder.Property(project => project.CreatedAtUtc).IsRequired();
        builder.Property(project => project.UpdatedAtUtc).IsRequired();

        builder.HasIndex(project => project.Name);

        builder.HasMany(project => project.Files)
            .WithOne(file => file.Project)
            .HasForeignKey(file => file.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(project => project.AnalysisSessions)
            .WithOne(session => session.Project)
            .HasForeignKey(session => session.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(project => project.Conversations)
            .WithOne(conversation => conversation.Project)
            .HasForeignKey(conversation => conversation.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
