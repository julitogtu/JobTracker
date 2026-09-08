using JobTracker.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobTracker.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(message => message.Type).HasColumnName("type").HasMaxLength(500).IsRequired();
        builder.Property(message => message.Content).HasColumnName("content").HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();
        builder.Property(message => message.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(message => message.ProcessedOnUtc).HasColumnName("processed_on_utc");
        builder.Property(message => message.RetryCount).HasColumnName("retry_count").IsRequired();
        builder.Property(message => message.NextAttemptOnUtc).HasColumnName("next_attempt_on_utc").IsRequired();
        builder.Property(message => message.LastError).HasColumnName("last_error").HasMaxLength(4_000);
        builder.Property(message => message.LockId).HasColumnName("lock_id");
        builder.Property(message => message.LockedUntilUtc).HasColumnName("locked_until_utc");

        builder.HasIndex(message => new
        {
            message.ProcessedOnUtc,
            message.NextAttemptOnUtc,
            message.LockedUntilUtc,
            message.OccurredOnUtc
        })
            .HasDatabaseName("ix_outbox_pending");

        builder.HasIndex(message => message.CorrelationId)
            .HasDatabaseName("ix_outbox_correlation_id");
    }
}