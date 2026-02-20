using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MetaAnime.Infrastructure.Configurations;

public class PropertyBagConfiguration : IEntityTypeConfiguration<PropertyBag>
{
    public void Configure(EntityTypeBuilder<PropertyBag> builder)
    {
        builder.ToTable("PropertyBag");
        builder.HasKey(p => p.Key);
        builder.Property(p => p.Key).IsRequired();
        builder.Property(p => p.Value).IsRequired();
        builder.Property(p => p.CreationDate).IsRequired();
        builder.Property(p => p.UpdateDate).IsRequired();
    }
}
