using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeMentor.Infrastructure.Persistence.Configurations;

internal sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable(
            "ConversationMessages",
            table =>
            {
                table.HasCheckConstraint("CK_ConversationMessages_Sequence", "[Sequence] >= 0");
                table.HasCheckConstraint(
                    "CK_ConversationMessages_Role",
                    "[Role] IN ('System', 'User', 'Assistant')");
            });
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Sequence).IsRequired();

        builder.Property(message => message.Role)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(message => message.Content)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(message => message.CreatedAtUtc).IsRequired();

        builder.HasIndex(message => new { message.ConversationId, message.Sequence })
            .IsUnique();
    }
}
