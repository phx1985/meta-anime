using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MetaAnime.Infrastructure.Configurations;

public sealed class SonarrHistoryEventConfiguration : IEntityTypeConfiguration<SonarrHistoryEvent>
{
    public void Configure(EntityTypeBuilder<SonarrHistoryEvent> builder)
    {
        builder.ToTable("SonarrHistoryEvents");

        // Sonarr's own history id is the key — it is supplied by the caller, never generated.
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.SizeBytes).IsRequired();
        builder.Property(e => e.SeriesId).IsRequired();
        builder.Property(e => e.EpisodeId).IsRequired();
        builder.Property(e => e.SeriesTitle).IsRequired().HasColumnType("text");
        builder.Property(e => e.SeasonNumber).IsRequired();
        builder.Property(e => e.EpisodeNumber).IsRequired();

        // Every stats read filters on OccurredAt (window start / retention prune).
        builder.HasIndex(e => e.OccurredAt);
    }
}
