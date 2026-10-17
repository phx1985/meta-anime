using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MetaAnime.Infrastructure.Configurations;

public sealed class VoiceActorConfiguration : IEntityTypeConfiguration<VoiceActor>
{
    public void Configure(EntityTypeBuilder<VoiceActor> builder)
    {
        builder.ToTable("VoiceActors");
        builder.HasKey(va => va.PersonId);
        builder.Property(va => va.PersonId).ValueGeneratedNever();
        builder.Property(va => va.Name).IsRequired();
        builder.Property(va => va.FamilyName);
        builder.Property(va => va.GivenName);
        builder.Property(va => va.ImageUrl);
        builder.Property(va => va.Birthday);
        builder.Property(va => va.BloodType);
        builder.Property(va => va.HeightCm).HasPrecision(6, 2);
        builder.Property(va => va.WeightKg).HasPrecision(6, 2);
        builder.Property(va => va.CachedAt).IsRequired();
    }
}
