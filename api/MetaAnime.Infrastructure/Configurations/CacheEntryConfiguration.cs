using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MetaAnime.Infrastructure.Configurations
{
    public class CacheEntryConfiguration : IEntityTypeConfiguration<CacheEntry>
    {
        public void Configure(EntityTypeBuilder<CacheEntry> builder)
        {
            builder.ToTable("CacheEntry");
            builder.HasKey(cache => cache.Id);
            builder.Property(cache => cache.Id).ValueGeneratedOnAdd();
            builder.Property(cache => cache.RawData).HasColumnType("jsonb").IsRequired();
            builder.Property(cache => cache.SourceId).IsRequired();
            builder.Property(cache => cache.AnimeSource).IsRequired();
        }
    }
}
