using CodeMentor.Core.Entities;
using CodeMentor.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeMentor.Infrastructure.Persistence.Configurations;

internal sealed class AnalysisSessionConfiguration : IEntityTypeConfiguration<AnalysisSession>
{
    public void Configure(EntityTypeBuilder<AnalysisSession> builder)
    {
        builder.ToTable(
            "AnalysisSessions",
            table => table.HasCheckConstraint(
                "CK_AnalysisSessions_Status",
                "[Status] IN ('Queued', 'Running', 'Completed', 'Failed', 'Cancelled')"));
        builder.HasKey(session => session.Id);

        builder.Property(session => session.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(AnalysisSessionStatus.Queued)
            .IsRequired();

        builder.Property(session => session.CreatedAtUtc).IsRequired();
        builder.HasIndex(session => new { session.ProjectId, session.CreatedAtUtc });
    }
}
