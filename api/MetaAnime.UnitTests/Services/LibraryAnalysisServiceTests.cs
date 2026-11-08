using System.Net;
using MetaAnime.Application.Services;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace MetaAnime.UnitTests.Services;

/// <summary>
/// A cancelled enrichment is a disconnected client (reload, navigation, HMR, proxy timeout),
/// not an application fault: it must return a plain failure and log nothing at Error level.
/// </summary>
public sealed class LibraryAnalysisServiceTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static LibraryAnalysisService Create(
        out CapturingLogger<LibraryAnalysisService> logger,
        out Mock<IPropertyBagRepository> propertyBag)
    {
        logger = new CapturingLogger<LibraryAnalysisService>();

        propertyBag = new Mock<IPropertyBagRepository>();
        propertyBag.Setup(r => r.GetByKeyAsync(SettingsKeys.SonarrHost))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.SonarrHost, Value = "http://sonarr.test:8989" });
        propertyBag.Setup(r => r.GetByKeyAsync(SettingsKeys.SonarrApiKey))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.SonarrApiKey, Value = "key" });

        var sonarr = new Mock<ISonarrAnimeSourceService<SonarrSeries>>();
        var mappings = new Mock<IAnimeMappingRepository>();
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://sonarr.test:8989") });

        return new LibraryAnalysisService(sonarr.Object, propertyBag.Object, mappings.Object, httpFactory.Object, logger);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }

    [Fact]
    public async Task EnrichSeriesAsync_WhenCancelled_ReturnsFailureWithoutErrorLogs()
    {
        var service = Create(out var logger, out _);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await service.EnrichSeriesAsync([1, 2, 3], cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal("The request was cancelled.", result.Error.Message);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task EnrichSeriesAsync_WhenNotConfigured_ReturnsEmptyWithoutTouchingTheNetwork()
    {
        var logger = new CapturingLogger<LibraryAnalysisService>();
        var propertyBag = new Mock<IPropertyBagRepository>();
        propertyBag.Setup(r => r.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((PropertyBag?)null);

        var sonarr = new Mock<ISonarrAnimeSourceService<SonarrSeries>>();
        var mappings = new Mock<IAnimeMappingRepository>();
        var httpFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);

        var service = new LibraryAnalysisService(sonarr.Object, propertyBag.Object, mappings.Object, httpFactory.Object, logger);

        var result = await service.EnrichSeriesAsync([1, 2, 3], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        httpFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }
}
