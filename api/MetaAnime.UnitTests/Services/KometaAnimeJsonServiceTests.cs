using FluentAssertions;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Settings;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.Kometa.Models;
using MetaAnime.Infrastructure.Kometa.Services;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Net.Http;

namespace MetaAnime.UnitTests.Services;

public class KometaAnimeJsonServiceTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = [];

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static (KometaAnimeJsonService Service, ScriptedHandler Handler) CreateService(HttpResponseMessage response)
    {
        var handler = new ScriptedHandler(_ => response);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));

        var propertyBag = new Mock<IPropertyBagRepository>();
        propertyBag
            .Setup(p => p.GetByKeyAsync(SettingsKeys.KometaAnimeIdsJsonUrl))
            .ReturnsAsync((PropertyBag?)null);

        var service = new KometaAnimeJsonService(
            factory.Object,
            Options.Create(new KometaSettings()),
            propertyBag.Object);
        return (service, handler);
    }

    [Fact]
    public async Task GetIfModifiedAsync_SendsConditionalHeaders_WhenValidatorsStored()
    {
        var (service, handler) = CreateService(
            new HttpResponseMessage(HttpStatusCode.NotModified));

        var result = await service.GetIfModifiedAsync("etag-1", "Mon, 01 Jan 2024 00:00:00 GMT", forceRefresh: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.NotModified.Should().BeTrue();
        result.Value.Items.Should().BeNull();

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Headers.TryGetValues("If-None-Match", out var etags).Should().BeTrue();
        etags.Should().ContainSingle(e => e == "etag-1");
        request.Headers.TryGetValues("If-Modified-Since", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetIfModifiedAsync_WithForceRefresh_SendsNoConditionalHeaders()
    {
        var (service, handler) = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"1":{"mal_id":100}}"""),
        });

        var result = await service.GetIfModifiedAsync("etag-1", null, forceRefresh: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.NotModified.Should().BeFalse();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items![0].AniDbId.Should().Be("1");
        result.Value.Items[0].MalIds.Should().Equal("100");

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Headers.Contains("If-None-Match").Should().BeFalse();
    }

    [Fact]
    public async Task GetIfModifiedAsync_ReturnsFreshValidators_OnSuccessfulFetch()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"1":{"mal_id":100}}"""),
        };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag-2\"");
        var (service, _) = CreateService(response);

        var result = await service.GetIfModifiedAsync(null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.ETag.Should().Be("\"etag-2\"");
    }
}
