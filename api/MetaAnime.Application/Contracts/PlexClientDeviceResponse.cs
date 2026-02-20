namespace MetaAnime.Application.Contracts
{
    public sealed record PlexClientDeviceResponse(
        string ClientIdentifier,
        string Name,
        string Product,
        string Platform,
        string DeviceClass
    );
}
