using FluentAssertions;
using LukeHagar.PlexAPI.SDK;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.Plex.Services;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Text;

namespace MetaAnime.UnitTests.Services;

public class PlexClientServiceTests
{
    private readonly Mock<IPlexAPI> _mockPlexApi;
    private readonly IOptions<PlexSettings> _plexOptions;
    private readonly PlexClientService _sut;

    public PlexClientServiceTests()
    {
        _mockPlexApi = new Mock<IPlexAPI>();
        _plexOptions = Options.Create(new PlexSettings
        {
            Token = "test-token",
            Host = "http://localhost:32400",
            AnimeSections = "1"
        });
        _sut = new PlexClientService(_mockPlexApi.Object, _plexOptions, CreateHttpClientFactory());
    }

    private static IHttpClientFactory CreateHttpClientFactory(string json = "")
    {
        var handler = new StubHttpHandler(json);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:32400") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(PlexClientService.HttpClientName)).Returns(client);
        return factory.Object;
    }

    [Fact]
    public async Task GetAvailableClientsAsync_WithMultipleClients_ReturnsAllClients()
    {
        // Arrange
        var json = """
        {
          "MediaContainer": {
            "size": 2,
            "Server": [
              {
                "machineIdentifier": "chromecast-1",
                "name": "Living Room Chromecast",
                "product": "Plex for Chromecast",
                "platform": "Cast",
                "deviceClass": "stb"
              },
              {
                "machineIdentifier": "windows-1",
                "name": "My PC",
                "product": "Plex for Windows",
                "platform": "Windows",
                "deviceClass": "desktop"
              }
            ]
          }
        }
        """;
        var sut = new PlexClientService(_mockPlexApi.Object, _plexOptions, CreateHttpClientFactory(json));

        // Act
        var result = await sut.GetAvailableClientsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(d => d.ClientIdentifier == "chromecast-1" && d.DeviceClass == "stb");
        result.Value.Should().Contain(d => d.ClientIdentifier == "windows-1" && d.DeviceClass == "desktop");
    }

    [Fact]
    public async Task GetAvailableClientsAsync_WhenNoClients_ReturnsEmptyList()
    {
        // Arrange
        var json = """{"MediaContainer":{"size":0}}""";
        var sut = new PlexClientService(_mockPlexApi.Object, _plexOptions, CreateHttpClientFactory(json));

        // Act
        var result = await sut.GetAvailableClientsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableClientsAsync_WhenPmsReturnsError_ReturnsFailure()
    {
        // Arrange
        var handler = new StubHttpHandler("", HttpStatusCode.Unauthorized);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(PlexClientService.HttpClientName)).Returns(client);

        var sut = new PlexClientService(_mockPlexApi.Object, _plexOptions, factory.Object);

        // Act
        var result = await sut.GetAvailableClientsAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Plex.GetDevices.Failed");
    }

    [Fact]
    public async Task GetAvailableClientsAsync_MapsAllFields_Correctly()
    {
        // Arrange
        var json = """
        {
          "MediaContainer": {
            "Server": [
              {
                "machineIdentifier": "gtv-1",
                "name": "Google TV Streamer",
                "product": "Plex for Android TV",
                "platform": "Android",
                "deviceClass": "stb"
              }
            ]
          }
        }
        """;
        var sut = new PlexClientService(_mockPlexApi.Object, _plexOptions, CreateHttpClientFactory(json));

        // Act
        var result = await sut.GetAvailableClientsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var device = result.Value.Single();
        device.ClientIdentifier.Should().Be("gtv-1");
        device.Name.Should().Be("Google TV Streamer");
        device.Product.Should().Be("Plex for Android TV");
        device.Platform.Should().Be("Android");
        device.DeviceClass.Should().Be("stb");
    }

    /// <summary>Minimal stub HTTP handler for unit testing.</summary>
    /// <param name="json">The JSON body to return in the response.</param>
    /// <param name="statusCode">The HTTP status code to return (default: 200 OK).</param>
    private sealed class StubHttpHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
