using FluentAssertions;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.Plex.Services;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Text;

namespace MetaAnime.UnitTests.Services;

public class PlexClientServiceTests
{
    private readonly IOptions<PlexSettings> _plexOptions;

    public PlexClientServiceTests()
    {
        _plexOptions = Options.Create(new PlexSettings
        {
            Token = "test-token",
            Host = "http://localhost:32400",
            AnimeSections = "1"
        });
    }

    private PlexClientService CreateSut(string xmlBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new StubHttpHandler(xmlBody, statusCode);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(PlexClientService.HttpClientName)).Returns(client);
        return new PlexClientService(_plexOptions, factory.Object);
    }

    private const string TwoClientsXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <MediaContainer size="3">
          <Device clientIdentifier="chromecast-1" name="Living Room Chromecast"
                  product="Plex for Chromecast" platform="Cast" provides="player" lastSeenAt="1708394400" />
          <Device clientIdentifier="windows-1" name="My PC"
                  product="Plex for Windows" platform="Windows"
                  provides="client,player,pubsub-player,controller" lastSeenAt="1708394400" />
          <Device clientIdentifier="server-1" name="My Plex Server"
                  product="Plex Media Server" platform="Linux" provides="server" lastSeenAt="1708394400" />
        </MediaContainer>
        """;

    [Fact]
    public async Task GetAvailableClientsAsync_ExcludesServerDevices()
    {
        // Act
        var result = await CreateSut(TwoClientsXml).GetAvailableClientsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().NotContain(d => d.ClientIdentifier == "server-1");
    }

    [Fact]
    public async Task GetAvailableClientsAsync_IncludesOfflineDevices()
    {
        // Arrange — device has an old lastSeenAt (was offline for days)
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <MediaContainer size="1">
              <Device clientIdentifier="gtv-1" name="Google TV Streamer"
                      product="Plex for Android TV" platform="Android" provides="player" lastSeenAt="1700000000" />
            </MediaContainer>
            """;

        // Act
        var result = await CreateSut(xml).GetAvailableClientsAsync();

        // Assert — offline device (last seen days ago) must still appear
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().ClientIdentifier.Should().Be("gtv-1");
    }

    [Fact]
    public async Task GetAvailableClientsAsync_MapsAllFieldsCorrectly()
    {
        // Act
        var result = await CreateSut(TwoClientsXml).GetAvailableClientsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var chromecast = result.Value.Single(d => d.ClientIdentifier == "chromecast-1");
        chromecast.Name.Should().Be("Living Room Chromecast");
        chromecast.Product.Should().Be("Plex for Chromecast");
        chromecast.Platform.Should().Be("Cast");
        chromecast.Provides.Should().Be("player");
        chromecast.LastSeen.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAvailableClientsAsync_WhenOnlyServers_ReturnsEmptyList()
    {
        // Arrange
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <MediaContainer size="1">
              <Device clientIdentifier="server-1" name="My Plex Server"
                      product="Plex Media Server" platform="Linux" provides="server" lastSeenAt="1708394400" />
            </MediaContainer>
            """;

        // Act
        var result = await CreateSut(xml).GetAvailableClientsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableClientsAsync_WhenPlexTvReturnsError_ReturnsFailure()
    {
        // Act
        var result = await CreateSut("", HttpStatusCode.Unauthorized).GetAvailableClientsAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Plex.GetDevices.Failed");
    }

    /// <summary>Minimal stub HTTP handler for unit testing.</summary>
    /// <param name="body">The response body to return.</param>
    /// <param name="statusCode">The HTTP status code to return (default: 200 OK).</param>
    private sealed class StubHttpHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml")
            };
            return Task.FromResult(response);
        }
    }
}
