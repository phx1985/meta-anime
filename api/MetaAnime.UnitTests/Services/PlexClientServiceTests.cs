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

    [Fact]
    public async Task GetAvailableClientsAsync_WhenPlayersPresent_ReturnsOnlyOnlinePlayerDevices()
    {
        // Arrange
        var plexDevices = new List<PlexDevice>
        {
            MakePlexDevice("client-1", "Living Room TV", "player", true),
            MakePlexDevice("server-1", "My Plex Server", "server", true),
            MakePlexDevice("client-offline", "Offline TV", "player", false)
        };

        var mockPlexSdk = new Mock<IPlex>();
        mockPlexSdk
            .Setup(x => x.GetServerResourcesAsync(It.IsAny<GetServerResourcesRequest>(), "https://plex.tv/api/v2"))
            .ReturnsAsync(new GetServerResourcesResponse
            {
                ContentType = "application/json",
                StatusCode = 200,
                RawResponse = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK),
                PlexDevices = plexDevices
            });

        _mockPlexApi.Setup(x => x.Plex).Returns(mockPlexSdk.Object);

        // Act
        var result = await _sut.GetAvailableClientsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().ClientIdentifier.Should().Be("client-1");
        result.Value.First().Name.Should().Be("Living Room TV");
    }

    [Fact]
    public async Task GetAvailableClientsAsync_WhenNoPlayersOnline_ReturnsEmptyList()
    {
        // Arrange
        var plexDevices = new List<PlexDevice>
        {
            MakePlexDevice("server-1", "My Plex Server", "server", true)
        };

        var mockPlexSdk = new Mock<IPlex>();
        mockPlexSdk
            .Setup(x => x.GetServerResourcesAsync(It.IsAny<GetServerResourcesRequest>(), "https://plex.tv/api/v2"))
            .ReturnsAsync(new GetServerResourcesResponse
            {
                ContentType = "application/json",
                StatusCode = 200,
                RawResponse = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK),
                PlexDevices = plexDevices
            });

        _mockPlexApi.Setup(x => x.Plex).Returns(mockPlexSdk.Object);

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
