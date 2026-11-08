using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MetaAnime.Infrastructure.Configurations;

public sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("ActivityLogs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Type).IsRequired();
        builder.Property(a => a.Title).IsRequired().HasMaxLength(400);
        builder.Property(a => a.Subtitle).HasMaxLength(600);
        builder.Property(a => a.PosterUrl).HasMaxLength(1000);
        builder.Property(a => a.SonarrSeriesId);
        builder.Property(a => a.Severity).IsRequired().HasMaxLength(16).HasDefaultValue("info");
        builder.Property(a => a.MetadataJson).HasColumnType("text");
        builder.Property(a => a.IsReverted).IsRequired().HasDefaultValue(false);
        builder.Property(a => a.ExternalEventKey).HasMaxLength(120);
        builder.Property(a => a.Timestamp).IsRequired();

        // The feed is always "newest first, optionally filtered by type".
        builder.HasIndex(a => new { a.Type, a.Timestamp });
        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => a.MalId);
        builder.HasIndex(a => a.ExternalEventKey);
    }
}
