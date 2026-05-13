using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure;

public class MetaAnimeDbContext(DbContextOptions<MetaAnimeDbContext> options) : DbContext(options)
{
    public DbSet<Anime> Anime { get; set; }
    public DbSet<KometaAnimeMapping> KometaAnimeMapping { get; set; }
    public DbSet<CacheEntry> CacheEntries { get; set; }
    public DbSet<AnimeMapping> AnimeMappings { get; set; }
    public DbSet<PropertyBag> PropertyBag { get; set; }
    public DbSet<AnimeCast> AnimeCast { get; set; }
    public DbSet<VoiceActor> VoiceActors { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AnimeConfiguration());
        modelBuilder.ApplyConfiguration(new KometaAnimeMappingConfiguration());
        modelBuilder.ApplyConfiguration(new ExternalAnimeIdentifierConfiguration());
        modelBuilder.ApplyConfiguration(new CacheEntryConfiguration());
        modelBuilder.ApplyConfiguration(new AnimeMappingConfiguration());
        modelBuilder.ApplyConfiguration(new PropertyBagConfiguration());
        modelBuilder.ApplyConfiguration(new AnimeCastConfiguration());
        modelBuilder.ApplyConfiguration(new VoiceActorConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
