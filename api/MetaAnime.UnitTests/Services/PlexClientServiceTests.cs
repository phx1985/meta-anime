using FluentAssertions;
using LukeHagar.PlexAPI.SDK;
using LukeHagar.PlexAPI.SDK.Models.Components;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.Plex.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace MetaAnime.UnitTests.Services;

public class PlexClientServiceTests
{
    private readonly Mock<IPlexAPI> _mockPlexApi;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly IOptions<PlexSettings> _plexOptions;
    private readonly PlexClientService _sut;

    public PlexClientServiceTests()
    {
        _mockPlexApi = new Mock<IPlexAPI>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _plexOptions = Options.Create(new PlexSettings
        {
            Token = "test-token",
            Host = "http://localhost:32400",
            AnimeSections = "1"
        });
        _sut = new PlexClientService(_mockPlexApi.Object, _plexOptions, _mockHttpClientFactory.Object);
    }

    private static PlexDevice MakePlexDevice(string clientId, string name, string provides, bool presence) => new()
    {
        ClientIdentifier = clientId,
        Name = name,
        Product = "Plex",
        Platform = "tvOS",
        Provides = provides,
        Presence = presence,
        ProductVersion = "1.0",
        PlatformVersion = "15.0",
        Device = "Apple TV",
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
        OwnerId = 1,
        SourceTitle = "Home",
        PublicAddress = "1.2.3.4",
        AccessToken = "abc",
        Owned = true,
        Home = true,
        Synced = false,
        Relay = false,
        HttpsRequired = false,
        PublicAddressMatches = true,
        DnsRebindingProtection = false,
        NatLoopbackSupported = true,
        Connections = []
    };

    private void SetupPlexSdk(List<PlexDevice> devices)
    {
        var mockPlexSdk = new Mock<IPlex>();
        mockPlexSdk
            .Setup(x => x.GetServerResourcesAsync(It.IsAny<GetServerResourcesRequest>(), "https://plex.tv/api/v2"))
            .ReturnsAsync(new GetServerResourcesResponse
            {
                ContentType = "application/json",
                StatusCode = 200,
                RawResponse = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK),
                PlexDevices = devices
            });
        _mockPlexApi.Setup(x => x.Plex).Returns(mockPlexSdk.Object);
    }

    [Fact]
    public async Task GetAvailableClientsAsync_ExcludesServerDevices()
    {
        // Arrange — mix of player, client, offline, and server devices
        SetupPlexSdk([
            MakePlexDevice("player-1", "Living Room TV", "player", true),
            MakePlexDevice("client-1", "Chromecast", "client", false),          // offline Chromecast
            MakePlexDevice("combo-1", "Plex for Windows", "client,player,pubsub-player,controller", true),
            MakePlexDevice("server-1", "My Plex Server", "server", true)         // server — must be excluded
        ]);

        // Act
        var result = await _sut.GetAvailableClientsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Should().NotContain(d => d.ClientIdentifier == "server-1");
    }

    [Fact]
    public async Task GetAvailableClientsAsync_IncludesOfflineClientDevices()
    {
        // Arrange — offline Chromecast (Presence=false) must appear in results
        SetupPlexSdk([
            MakePlexDevice("chromecast-1", "Living Room Chromecast", "player", false),
            MakePlexDevice("server-1", "My Plex Server", "server", true)
        ]);

        // Act
        var result = await _sut.GetAvailableClientsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().ClientIdentifier.Should().Be("chromecast-1");
        result.Value.First().Presence.Should().BeFalse();
    }

    [Fact]
    public async Task GetAvailableClientsAsync_IncludesDevicesWithClientProvides()
    {
        // Arrange — some devices register with "client" rather than "player" in Provides
        SetupPlexSdk([
            MakePlexDevice("gtv-1", "Google TV Streamer", "client", false)
        ]);

        // Act
        var result = await _sut.GetAvailableClientsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().ClientIdentifier.Should().Be("gtv-1");
        result.Value.First().Provides.Should().Be("client");
    }

    [Fact]
    public async Task GetAvailableClientsAsync_WhenOnlyServersPresent_ReturnsEmptyList()
    {
        // Arrange
        SetupPlexSdk([
            MakePlexDevice("server-1", "My Plex Server", "server", true)
        ]);

        // Act
        var result = await _sut.GetAvailableClientsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableClientsAsync_WhenApiThrows_ReturnsFailure()
    {
        // Arrange
        var mockPlexSdk = new Mock<IPlex>();
        mockPlexSdk
            .Setup(x => x.GetServerResourcesAsync(It.IsAny<GetServerResourcesRequest>(), "https://plex.tv/api/v2"))
            .ThrowsAsync(new InvalidOperationException("Plex API unreachable"));
        _mockPlexApi.Setup(x => x.Plex).Returns(mockPlexSdk.Object);

        // Act
        var result = await _sut.GetAvailableClientsAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Plex.GetDevices.Failed");
    }
}
