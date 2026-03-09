using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MetaAnime.Infrastructure.Configurations;

public sealed class ManualMediaMappingConfiguration : IEntityTypeConfiguration<ManualMediaMapping>
{
    public void Configure(EntityTypeBuilder<ManualMediaMapping> builder)
    {
        builder.ToTable("ManualMediaMapping");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.PlexSourceId).IsRequired();
        builder.Property(e => e.PlexTitle).IsRequired();
        builder.Property(e => e.MalId).IsRequired();
        builder.Property(e => e.MalTitle).IsRequired();
        builder.Property(e => e.AniDbId).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.HasIndex(e => e.PlexSourceId).HasDatabaseName("IX_ManualMediaMapping_PlexSourceId");
        builder.HasIndex(new[] { "PlexSourceId", "MalId" })
               .IsUnique()
               .HasDatabaseName("IX_ManualMediaMapping_PlexSourceId_MalId");
    }
}
