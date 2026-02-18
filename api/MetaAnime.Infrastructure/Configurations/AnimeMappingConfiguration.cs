using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MetaAnime.Infrastructure.Configurations
{
    public class AnimeMappingConfiguration : IEntityTypeConfiguration<AnimeMapping>
    {
        public void Configure(EntityTypeBuilder<AnimeMapping> builder)
        {
            builder.ToTable("AnimeMapping");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.AniDbId).IsRequired();
            builder.Property(e => e.AnimeId).IsRequired();
            builder.HasOne(e => e.Anime).WithMany().HasForeignKey(e => e.AnimeId);
        }
    }
}
