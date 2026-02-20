using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MetaAnime.Infrastructure.Configurations;

public class ExternalAnimeIdentifierConfiguration : IEntityTypeConfiguration<ExternalAnimeIdentifier>
{
    public void Configure(EntityTypeBuilder<ExternalAnimeIdentifier> builder)
    {
        builder.ToTable("ExternalAnimeIdentifier");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.AnimeId).IsRequired();
        builder.HasOne(e => e.Anime).WithMany(a => a.ExternalAnimeIdentifiers);
        builder.Property(e => e.MediaPlatform).IsRequired();
    }
}
