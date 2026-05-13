using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories;

public interface IVoiceActorRepository
{
    Task<VoiceActor?> GetByPersonIdAsync(int personId);
    Task UpsertAsync(VoiceActor voiceActor);
}
