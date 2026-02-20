using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services
{
    public interface IPlexClientService
    {
        Task<Result<IList<PlexClientDevice>>> GetAvailableClientsAsync();
        Task<Result> NavigateToSeriesAsync(string plexSeriesId, string clientIdentifier);
    }

    public class PlexClientDevice
    {
        public required string ClientIdentifier { get; set; }
        public required string Name { get; set; }
        public required string Product { get; set; }
        public required string Platform { get; set; }
        public required string DeviceClass { get; set; }
    }
}
