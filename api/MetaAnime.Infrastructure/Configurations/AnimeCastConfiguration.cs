using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MetaAnime.Infrastructure.Configurations;

public sealed class AnimeCastConfiguration : IEntityTypeConfiguration<AnimeCast>
{
    public void Configure(EntityTypeBuilder<AnimeCast> builder)
    {
        builder.ToTable("AnimeCast");
        builder.HasKey(ac => ac.Id);
        builder.Property(ac => ac.Id).ValueGeneratedOnAdd();
        builder.Property(ac => ac.AnimeMalId).IsRequired();
        builder.Property(ac => ac.CharacterId).IsRequired();
        builder.Property(ac => ac.CharacterName).IsRequired();
        builder.Property(ac => ac.CharacterRole).IsRequired();
        builder.Property(ac => ac.CharacterImageUrl);
        builder.Property(ac => ac.VaPersonId).IsRequired();
        builder.Property(ac => ac.VaName).IsRequired();
        builder.Property(ac => ac.VaImageUrl);
        builder.Property(ac => ac.Language).IsRequired();
        builder.Property(ac => ac.SyncedAt).IsRequired();

        builder.HasIndex(ac => ac.AnimeMalId).HasDatabaseName("IX_AnimeCast_AnimeMalId");
        builder.HasIndex(ac => ac.VaPersonId).HasDatabaseName("IX_AnimeCast_VaPersonId");
        builder.HasIndex(ac => new { ac.AnimeMalId, ac.CharacterId, ac.Language })
               .IsUnique()
               .HasDatabaseName("UX_AnimeCast_Anime_Character_Language");
    }
}
