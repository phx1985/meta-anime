using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public sealed class VoiceActorRepository(MetaAnimeDbContext dbContext) : IVoiceActorRepository
{
    public async Task<VoiceActor?> GetByPersonIdAsync(int personId)
        => await dbContext.VoiceActors
            .AsNoTracking()
            .FirstOrDefaultAsync(va => va.PersonId == personId);

    public async Task UpsertAsync(VoiceActor voiceActor)
    {
        var existing = await dbContext.VoiceActors
            .FirstOrDefaultAsync(va => va.PersonId == voiceActor.PersonId);

        if (existing is null)
            await dbContext.VoiceActors.AddAsync(voiceActor);
        else
        {
            existing.Name = voiceActor.Name;
            existing.FamilyName = voiceActor.FamilyName;
            existing.GivenName = voiceActor.GivenName;
            existing.ImageUrl = voiceActor.ImageUrl;
            existing.Birthday = voiceActor.Birthday;
            existing.BloodType = voiceActor.BloodType;
            existing.HeightCm = voiceActor.HeightCm;
            existing.WeightKg = voiceActor.WeightKg;
            existing.CachedAt = voiceActor.CachedAt;
        }

        await dbContext.SaveChangesAsync();
    }
}
