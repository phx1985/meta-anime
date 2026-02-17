using MetaAnime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MetaAnime.Infrastructure.Configurations
{
    public class KometaAnimeMappingConfiguration : IEntityTypeConfiguration<KometaAnimeMapping>
    {
        public void Configure(EntityTypeBuilder<KometaAnimeMapping> builder)
        {
            builder.ToTable("KometaAnimeMapping");
            builder.HasKey(k => k.Id);
            builder.Property(k => k.Id).ValueGeneratedOnAdd();
            builder.Property(k => k.AniDbId).IsRequired();
            builder.Property(k => k.PlatformName).IsRequired();
            builder.Property(k => k.PlatformId).IsRequired();
            builder.Property(k => k.Season);
            builder.Property(k => k.EpOffset);
        }
    }
}