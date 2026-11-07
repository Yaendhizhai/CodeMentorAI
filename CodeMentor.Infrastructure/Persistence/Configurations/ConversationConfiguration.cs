using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeMentor.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        builder.HasKey(conversation => conversation.Id);

        builder.Property(conversation => conversation.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(conversation => conversation.CreatedAtUtc).IsRequired();
        builder.Property(conversation => conversation.UpdatedAtUtc).IsRequired();

        builder.HasIndex(conversation => new { conversation.ProjectId, conversation.CreatedAtUtc });

        builder.HasMany(conversation => conversation.Messages)
            .WithOne(message => message.Conversation)
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
