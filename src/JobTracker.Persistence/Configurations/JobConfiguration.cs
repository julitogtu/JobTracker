using JobTracker.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace JobTracker.Persistence.Configurations;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");
        builder.HasKey(job => job.Id);

        builder.Property(job => job.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(job => job.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(job => job.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(job => job.AssigneeId).HasColumnName("assignee_id");
        builder.Property(job => job.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(job => job.Description).HasColumnName("description").HasMaxLength(4_000).IsRequired();
        builder.Property(job => job.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(job => job.ScheduledDateUtc).HasColumnName("scheduled_date_utc");
        builder.Property(job => job.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(job => job.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(job => job.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(job => job.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(1_000);
        builder.Property(job => job.SignatureUrl).HasColumnName("signature_url").HasMaxLength(2_048);
        builder.Property(job => job.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(job => job.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        builder.OwnsOne(job => job.Address, address =>
        {
            address.Property(value => value.Street).HasColumnName("address_street").HasMaxLength(200).IsRequired();
            address.Property(value => value.City).HasColumnName("address_city").HasMaxLength(100).IsRequired();
            address.Property(value => value.State).HasColumnName("address_state").HasMaxLength(100).IsRequired();
            address.Property(value => value.ZipCode).HasColumnName("address_zip_code").HasMaxLength(20).IsRequired();
            address.Property(value => value.Latitude).HasColumnName("address_latitude").HasPrecision(9, 6).IsRequired();
            address.Property(value => value.Longitude).HasColumnName("address_longitude").HasPrecision(9, 6).IsRequired();
        });
        builder.Navigation(job => job.Address).IsRequired();

        builder.HasMany(job => job.Photos)
            .WithOne()
            .HasForeignKey(photo => photo.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(job => job.Photos)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(job => job.DomainEvents);

        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                "to_tsvector('english', coalesce(title, '') || ' ' || coalesce(description, ''))",
                stored: true);

        builder.HasIndex("SearchVector")
            .HasDatabaseName("ix_jobs_search_vector")
            .HasMethod("GIN");

        builder.HasIndex(job => new { job.OrganizationId, job.Status, job.ScheduledDateUtc, job.Id })
            .HasDatabaseName("ix_jobs_org_status_schedule_id");
        builder.HasIndex(job => new { job.OrganizationId, job.AssigneeId, job.ScheduledDateUtc, job.Id })
            .HasDatabaseName("ix_jobs_org_assignee_schedule_id");
        builder.HasIndex(job => new { job.OrganizationId, job.UpdatedAtUtc })
            .HasDatabaseName("ix_jobs_org_updated");
    }
}
