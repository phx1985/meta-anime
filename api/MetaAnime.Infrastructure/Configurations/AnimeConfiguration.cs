using MetaAnime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MetaAnime.Infrastructure.Configurations
{
    public class AnimeConfiguration : IEntityTypeConfiguration<Anime>
    {
        public void Configure(EntityTypeBuilder<Anime> builder)
        {
            builder.ToTable("Anime");
            builder.HasKey(a => a.Id);
            builder.Property(a => a.Id).ValueGeneratedOnAdd();
            builder.Property(a => a.SourceId).IsRequired();
            builder.Property(a => a.Title).IsRequired();
            builder.Property(a => a.Description);
            builder.Property(a => a.AnimeSource).IsRequired();
            builder.HasMany(a => a.ExternalAnimeIdentifiers).WithOne(e => e.Anime);
        }
    }
}