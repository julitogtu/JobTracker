using JobTracker.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobTracker.Persistence.Configurations;

internal sealed class JobPhotoConfiguration : IEntityTypeConfiguration<JobPhoto>
{
    public void Configure(EntityTypeBuilder<JobPhoto> builder)
    {
        builder.ToTable("job_photos");
        builder.HasKey(photo => photo.Id);

        builder.Property(photo => photo.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(photo => photo.JobId).HasColumnName("job_id").IsRequired();
        builder.Property(photo => photo.Url).HasColumnName("url").HasMaxLength(2_048).IsRequired();
        builder.Property(photo => photo.CapturedAtUtc).HasColumnName("captured_at_utc").IsRequired();
        builder.Property(photo => photo.Caption).HasColumnName("caption").HasMaxLength(500);

        builder.HasIndex(photo => new { photo.JobId, photo.CapturedAtUtc })
            .HasDatabaseName("ix_job_photos_job_captured");
    }
}
