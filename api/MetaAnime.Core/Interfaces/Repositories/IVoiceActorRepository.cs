using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories;

public interface IVoiceActorRepository
{
    Task<VoiceActor?> GetByPersonIdAsync(int personId);
    Task UpsertAsync(VoiceActor voiceActor);
    /// <summary>Deletes every profile row (one-time ID-space migration).</summary>
    Task DeleteAllAsync();
}
