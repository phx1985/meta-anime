using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using MetaAnime.Application.Services.AniList;
using MetaAnime.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MetaAnime.UnitTests.Services;

public class AniListHttpPipelineTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<HttpContent?> Contents { get; } = [];
        public List<string?> Bodies { get; } = [];

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responses.Enqueue(responder);

        public void Enqueue(HttpStatusCode status, string? json = null, TimeSpan? retryAfter = null) =>
            Enqueue(_ =>
            {
                var response = new HttpResponseMessage(status);
                if (json is not null)
                    response.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (retryAfter is not null)
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
                return response;
            });

        public void EnqueueFault() => Enqueue(_ => throw new HttpRequestException("network fault"));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Contents.Add(request.Content);
            Bodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            if (_responses.Count == 0)
                throw new InvalidOperationException("No scripted response left.");
            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static (AniListHttpPipeline Pipeline, ScriptedHandler Handler) CreatePipeline()
    {
        var handler = new ScriptedHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://graphql.anilist.co") };
        var gate = new AniListPacingGate(minInterval: TimeSpan.FromMilliseconds(1), maxQueueWait: TimeSpan.FromSeconds(5));
        var pipeline = new AniListHttpPipeline(
            new StubHttpClientFactory(client),
            gate,
            Options.Create(new AniListSettings()),
            NullLogger<AniListHttpPipeline>.Instance);
        return (pipeline, handler);
    }

    private static IReadOnlyDictionary<string, object?> Vars() =>
        new Dictionary<string, object?> { ["malId"] = 123, ["page"] = 1 };

    [Fact]
    public async Task RetryOn429_UsesFreshRequestPerAttempt_WithRequiredHeadersAndSameBody()
    {
        var (pipeline, handler) = CreatePipeline();
        handler.Enqueue(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.Zero);
        handler.Enqueue(HttpStatusCode.OK, """{"data":{"Media":null}}""");

        var result = await pipeline.PostQueryAsync<AniListMediaCastData>(AniListQueries.AnimeCast, Vars());
        result.Status.Should().Be(AniListApiStatus.Success);
        result.Data!.Media.Should().BeNull(); // unknown idMal shape survives deserialization
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Should().NotBeSameAs(handler.Requests[1]); // fresh HttpRequestMessage per attempt
        handler.Contents[0].Should().NotBeSameAs(handler.Contents[1]); // fresh StringContent per attempt
        handler.Bodies[0].Should().Be(handler.Bodies[1]); // identical JSON body on every attempt

        foreach (var (request, content) in handler.Requests.Zip(handler.Contents))
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.Headers.GetValues("User-Agent").Single().Should().Be("SeiyuuNexusClient/3.0");
            request.Headers.Accept.Should().ContainSingle(a => a.MediaType == "application/json");
            request.RequestUri!.Host.Should().Be("graphql.anilist.co");
            content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        }

        handler.Bodies[0].Should().Contain("\"query\"").And.Contain("\"malId\":123").And.Contain("\"page\":1");
    }

    [Fact]
    public async Task Unavailable503_IsRetriedTwice_ThenReturnsUnavailable()
    {
        var (pipeline, handler) = CreatePipeline();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, retryAfter: TimeSpan.Zero);
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, retryAfter: TimeSpan.Zero);
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, retryAfter: TimeSpan.Zero);

        var result = await pipeline.PostQueryAsync<AniListMediaCastData>(AniListQueries.AnimeCast, Vars());
        result.Status.Should().Be(AniListApiStatus.Unavailable);
        handler.Requests.Should().HaveCount(3); // 3 attempts, no more
    }

    [Fact]
    public async Task Http404_IsDefinitive_NoRetry()
    {
        var (pipeline, handler) = CreatePipeline();
        handler.Enqueue(HttpStatusCode.NotFound);

        var result = await pipeline.PostQueryAsync<AniListStaffData>(AniListQueries.StaffProfile, new Dictionary<string, object?> { ["id"] = 9 });
        result.Status.Should().Be(AniListApiStatus.NotFound);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GraphQl404Error_WithNullData_IsNotFound()
    {
        var (pipeline, handler) = CreatePipeline();
        handler.Enqueue(HttpStatusCode.OK, """{"errors":[{"message":"Not Found.","status":404}],"data":null}""");

        var result = await pipeline.PostQueryAsync<AniListMediaCastData>(AniListQueries.AnimeCast, Vars());
        result.Status.Should().Be(AniListApiStatus.NotFound);
    }

    [Fact]
    public async Task GraphQlServerError_WithNullData_IsUnavailable()
    {
        var (pipeline, handler) = CreatePipeline();
        handler.Enqueue(HttpStatusCode.OK, """{"errors":[{"message":"Internal Server Error","status":500}],"data":null}""");

        var result = await pipeline.PostQueryAsync<AniListMediaCastData>(AniListQueries.AnimeCast, Vars());
        result.Status.Should().Be(AniListApiStatus.Unavailable);
        result.Detail.Should().Contain("Internal Server Error");
    }

    [Fact]
    public async Task BadRequest400_IsNotRetried()
    {
        var (pipeline, handler) = CreatePipeline();
        handler.Enqueue(HttpStatusCode.BadRequest);

        var result = await pipeline.PostQueryAsync<AniListMediaCastData>(AniListQueries.AnimeCast, Vars());
        result.Status.Should().Be(AniListApiStatus.Unavailable);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task NetworkFault_RetriesWithBackoff_AndRecovers()
    {
        var (pipeline, handler) = CreatePipeline();
        handler.EnqueueFault(); // first retry after 1s fault backoff
        handler.Enqueue(HttpStatusCode.OK, """{"data":{"Staff":null}}""");

        var result = await pipeline.PostQueryAsync<AniListStaffData>(AniListQueries.StaffProfile, new Dictionary<string, object?> { ["id"] = 9 });
        result.Status.Should().Be(AniListApiStatus.Success);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task CamelCaseDeserialization_MapsStaffPayload()
    {
        var (pipeline, handler) = CreatePipeline();
        handler.Enqueue(HttpStatusCode.OK, """
        {
          "data": {
            "Staff": {
              "id": 96895,
              "name": { "full": "Bisco Hatori", "native": "葉鳥ビスコ" },
              "image": { "medium": "https://img/hatori.png" },
              "dateOfBirth": { "year": 1974, "month": 8, "day": 30 },
              "bloodType": "AB",
              "description": "Manga artist."
            }
          }
        }
        """);

        var result = await pipeline.PostQueryAsync<AniListStaffData>(AniListQueries.StaffProfile, new Dictionary<string, object?> { ["id"] = 96895 });
        result.Status.Should().Be(AniListApiStatus.Success);
        var staff = result.Data!.Staff!;
        staff.Id.Should().Be(96895);
        staff.Name!.Full.Should().Be("Bisco Hatori");
        staff.Name.Native.Should().Be("葉鳥ビスコ");
        staff.BloodType.Should().Be("AB");
        staff.DateOfBirth!.Year.Should().Be(1974);
        staff.Image!.GetImageUrl().Should().Be("https://img/hatori.png"); // large missing → medium fallback
    }

    [Fact]
    public void RetryDelay_HonorsRetryAfter_CappedAtFiveSeconds()
    {
        AniListHttpPipeline.ComputeRetryDelay(new RetryConditionHeaderValue(TimeSpan.FromSeconds(60)), 0)
            .Should().Be(TimeSpan.FromSeconds(5));
        AniListHttpPipeline.ComputeRetryDelay(new RetryConditionHeaderValue(TimeSpan.FromSeconds(2)), 1)
            .Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RetryDelay_FallsBackToBackoffPolicy()
    {
        AniListHttpPipeline.ComputeRetryDelay(null, 0).Should().Be(TimeSpan.FromSeconds(2)); // 2s/4s for 429/503
        AniListHttpPipeline.ComputeRetryDelay(null, 1).Should().Be(TimeSpan.FromSeconds(4));
        AniListHttpPipeline.FaultBackoffDelay(0).Should().Be(TimeSpan.FromSeconds(1)); // 1s/2s for faults
        AniListHttpPipeline.FaultBackoffDelay(1).Should().Be(TimeSpan.FromSeconds(2));
    }
}
