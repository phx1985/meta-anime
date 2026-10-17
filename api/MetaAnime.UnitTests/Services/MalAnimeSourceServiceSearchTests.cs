using FluentAssertions;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.MyAnimeList;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.MyAnimeList.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Web;
using MalConstants = MetaAnime.Infrastructure.MyAnimeList.Constants;

namespace MetaAnime.UnitTests.Services;

/// <summary>
/// Wire-level tests for the MAL title search. A loopback TCP server plays MAL so the tests assert on the
/// real HTTP request RestSharp emits and on how MAL's real reply shapes (JSON errors, HTML 403 pages,
/// empty bodies, dropped connections) are handled.
/// </summary>
public class MalAnimeSourceServiceSearchTests
{
    private const string TwoResultsJson = """
        {"data":[
          {"node":{"id":1,"title":"Cowboy Bebop","main_picture":{"medium":"https://cdn.example/1m.jpg","large":"https://cdn.example/1l.jpg"},"start_date":"1998-04-03","media_type":"tv"}},
          {"node":{"id":2,"title":"Untitled Special"}}
        ],"paging":{}}
        """;

    private const string MalBadRequestJson = """{"message":"invalid q","error":"bad_request"}""";

    #region Request building

    [Fact]
    public async Task SearchByTitleAsync_SendsExpectedRequest_AndMapsResults()
    {
        await using var server = new FakeMalServer(_ => FakeResponse.Json(TwoResultsJson));
        var sut = CreateSut(server.BaseUrl);

        var result = await sut.SearchByTitleAsync("Dragon Ball Super: Galactic Patrol!");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(
            new MalSearchResultItem("1", "Cowboy Bebop", "https://cdn.example/1m.jpg", 1998, "tv", false),
            new MalSearchResultItem("2", "Untitled Special", null, null, null, false));

        var request = server.Requests.Should().ContainSingle().Subject;
        RequestPath(request).Should().Be("/v2/anime");
        QueryParam(request, "q").Should().Be("Dragon Ball Super Galactic Patrol");
        QueryParam(request, "limit").Should().Be("10");
        QueryParam(request, "fields").Should().Be("id,title,main_picture,start_date,media_type");
        request.Should().ContainEquivalentOf("Authorization: Bearer tok123");
    }

    [Fact]
    public async Task SearchByTitleRichAsync_SendsExpectedRequest_AndReturnsNodes()
    {
        await using var server = new FakeMalServer(_ => FakeResponse.Json(TwoResultsJson));
        var sut = CreateSut(server.BaseUrl);

        var result = await sut.SearchByTitleRichAsync("Re:Zero kara Hajimeru Isekai Seikatsu");

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(a => (a.Id, a.Title)).Should().Equal((1, "Cowboy Bebop"), (2, "Untitled Special"));

        var request = server.Requests.Should().ContainSingle().Subject;
        RequestPath(request).Should().Be("/v2/anime");
        QueryParam(request, "q").Should().Be("Re Zero kara Hajimeru Isekai Seikatsu");
        QueryParam(request, "limit").Should().Be("100");
        QueryParam(request, "nsfw").Should().Be("true");
        QueryParam(request, "fields").Should().Contain("alternative_titles{en,ja}").And.Contain("start_season{year,season}");
        request.Should().ContainEquivalentOf("Authorization: Bearer tok123");
    }

    [Theory]
    [InlineData("Dragon Ball Super: Galactic Patrol!", "Dragon Ball Super Galactic Patrol")]
    [InlineData("What?!  Is   this", "What Is this")]
    [InlineData("  K-On!!  ", "K-On")]
    [InlineData("Tab\tseparated", "Tab separated")]
    [InlineData("Fate/stay night", "Fate/stay night")]
    [InlineData("Frieren's journey", "Frieren's journey")]
    [InlineData("Bleach + Sennen", "Bleach + Sennen")]
    [InlineData("100% Pascal-sensei", "100% Pascal-sensei")]
    [InlineData("進撃の巨人", "進撃の巨人")]
    public async Task Search_NormalisesQuery_AndEncodesItOnTheWire(string query, string expectedQ)
    {
        foreach (var rich in new[] { false, true })
        {
            await using var server = new FakeMalServer(_ => FakeResponse.Json(TwoResultsJson));
            var sut = CreateSut(server.BaseUrl);

            var outcome = await RunSearchAsync(sut, rich, query);

            outcome.IsSuccess.Should().BeTrue();
            QueryParam(server.Requests.Should().ContainSingle().Subject, "q").Should().Be(expectedQ);
        }
    }

    [Theory]
    [InlineData(false, 500, "100")]
    [InlineData(true, 500, "100")]
    [InlineData(false, 0, "1")]
    [InlineData(true, -3, "1")]
    [InlineData(false, 25, "25")]
    [InlineData(true, 100, "100")]
    public async Task Search_ClampsLimitToWhatMalAccepts(bool rich, int limit, string expectedLimit)
    {
        await using var server = new FakeMalServer(_ => FakeResponse.Json(TwoResultsJson));
        var sut = CreateSut(server.BaseUrl);

        var outcome = await RunSearchAsync(sut, rich, "Naruto", limit);

        outcome.IsSuccess.Should().BeTrue();
        QueryParam(server.Requests.Should().ContainSingle().Subject, "limit").Should().Be(expectedLimit);
    }

    #endregion

    #region Query validation

    public static TheoryData<string, bool> TooShortQueries()
    {
        var data = new TheoryData<string, bool>();
        foreach (var rich in new[] { false, true })
            foreach (var query in new[] { "", "  ", "ab", "Re:", "A!!", "?!" })
                data.Add(query, rich);
        return data;
    }

    [Theory]
    [MemberData(nameof(TooShortQueries))]
    public async Task Search_WhenQueryIsTooShortAfterNormalising_FailsWithoutCallingMal(string query, bool rich)
    {
        await using var server = new FakeMalServer(_ => FakeResponse.Json(TwoResultsJson));
        var sut = CreateSut(server.BaseUrl);

        var outcome = await RunSearchAsync(sut, rich, query);

        outcome.IsSuccess.Should().BeFalse();
        outcome.Error!.Code.Should().Be(MalConstants.Errors.InvalidSearchQuery);
        outcome.Error.Message.Should().Contain("at least 3 characters");
        server.Requests.Should().BeEmpty();
    }

    #endregion

    #region Failure handling

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_WhenMalRejectsTheRequest_SurfacesMalsOwnExplanation(bool rich)
    {
        await using var server = new FakeMalServer(_ => new FakeResponse(400, MalBadRequestJson));
        var sut = CreateSut(server.BaseUrl);

        var outcome = await RunSearchAsync(sut, rich, "Naruto");

        outcome.IsSuccess.Should().BeFalse();
        outcome.Error!.Code.Should().Be(ExpectedFailureCode(rich));
        outcome.Error.Message.Should().Contain("HTTP 400").And.Contain("invalid q").And.Contain("bad_request");
    }

    [Theory]
    [InlineData(false, 403, "text/html", "<html><body>Forbidden</body></html>")]
    [InlineData(true, 403, "text/html", "<html><body>Forbidden</body></html>")]
    [InlineData(false, 502, "text/html", "<html>Bad Gateway</html>")]
    [InlineData(true, 503, "application/json", "")]
    [InlineData(true, 500, "text/plain", "boom")]
    public async Task Search_WhenMalAnswersWithANonJsonErrorBody_ReturnsFailureInsteadOfThrowing(
        bool rich, int status, string contentType, string body)
    {
        await using var server = new FakeMalServer(_ => new FakeResponse(status, body, contentType));
        var sut = CreateSut(server.BaseUrl);

        var outcome = await RunSearchAsync(sut, rich, "Naruto");

        outcome.IsSuccess.Should().BeFalse();
        outcome.Error!.Code.Should().Be(ExpectedFailureCode(rich));
        outcome.Error.Message.Should().Contain($"HTTP {status}");
    }

    [Fact]
    public async Task Search_WhenMalForbidsTheRequest_HintsAtRateLimiting()
    {
        // MAL documents 403 as "DoS detected etc."
        await using var server = new FakeMalServer(_ => new FakeResponse(403, "<html>blocked</html>", "text/html"));
        var sut = CreateSut(server.BaseUrl);

        var result = await sut.SearchByTitleRichAsync("Naruto");

        result.Error.Message.Should().Contain("HTTP 403").And.Contain("rate limiting");
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "not json")]
    [InlineData(true, "<html>maintenance</html>")]
    [InlineData(false, """{"data":5}""")]
    public async Task Search_WhenSuccessBodyIsUnreadable_ReturnsFailureInsteadOfThrowing(bool rich, string body)
    {
        await using var server = new FakeMalServer(_ => FakeResponse.Json(body));
        var sut = CreateSut(server.BaseUrl);

        var outcome = await RunSearchAsync(sut, rich, "Naruto");

        outcome.IsSuccess.Should().BeFalse();
        outcome.Error!.Code.Should().Be(ExpectedFailureCode(rich));
        outcome.Error.Message.Should().Contain("could not be read");
    }

    [Theory]
    [InlineData(false, """{}""")]
    [InlineData(true, """{"data":null}""")]
    [InlineData(false, """{"data":[],"paging":{}}""")]
    public async Task Search_WhenMalFindsNothing_ReturnsEmptySuccess(bool rich, string body)
    {
        await using var server = new FakeMalServer(_ => FakeResponse.Json(body));
        var sut = CreateSut(server.BaseUrl);

        var outcome = await RunSearchAsync(sut, rich, "Naruto");

        outcome.IsSuccess.Should().BeTrue();
        outcome.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_WhenConnectionDropsWithoutAResponse_ReturnsFailureInsteadOfThrowing(bool rich)
    {
        await using var server = new FakeMalServer(_ => FakeResponse.Drop);
        var sut = CreateSut(server.BaseUrl);

        var outcome = await RunSearchAsync(sut, rich, "Naruto");

        outcome.IsSuccess.Should().BeFalse();
        outcome.Error!.Code.Should().Be(ExpectedFailureCode(rich));
        outcome.Error.Message.Should().StartWith("Could not reach MyAnimeList");
    }

    [Fact]
    public void MalError_ExposesBothMachineCodeAndHumanReadableDetail()
    {
        var error = JsonSerializer.Deserialize<MalError>(MalBadRequestJson)!;

        error.Message.Should().Be("bad_request");
        error.Detail.Should().Be("invalid q");
    }

    #endregion

    #region Authentication

    [Fact]
    public async Task Search_WhenMalReturns401_RefreshesTheTokenAndRetries()
    {
        await using var server = new FakeMalServer(i => i == 0
            ? new FakeResponse(401, """{"error":"invalid_token"}""")
            : FakeResponse.Json(TwoResultsJson));
        var tokens = new Mock<IMalTokenService>();
        tokens.SetupSequence(t => t.GetAccessTokenAsync()).ReturnsAsync("tok123").ReturnsAsync("newtok");
        tokens.Setup(t => t.RefreshTokenAsync()).ReturnsAsync(Result.Success());
        var sut = CreateSut(server.BaseUrl, tokens.Object);

        var result = await sut.SearchByTitleAsync("Naruto");

        result.IsSuccess.Should().BeTrue();
        server.Requests.Should().HaveCount(2);
        server.Requests[0].Should().ContainEquivalentOf("Authorization: Bearer tok123");
        server.Requests[1].Should().ContainEquivalentOf("Authorization: Bearer newtok");
    }

    [Fact]
    public async Task Search_WhenTokenCannotBeRefreshed_ThrowsMalAuthenticationException()
    {
        await using var server = new FakeMalServer(_ => new FakeResponse(401, """{"error":"invalid_token"}"""));
        var tokens = new Mock<IMalTokenService>();
        tokens.Setup(t => t.GetAccessTokenAsync()).ReturnsAsync("tok123");
        tokens.Setup(t => t.RefreshTokenAsync())
            .ReturnsAsync(Result.Failure(new Error(MalConstants.Errors.RefreshFailed, "refresh token expired")));
        var sut = CreateSut(server.BaseUrl, tokens.Object);

        var act = () => sut.SearchByTitleRichAsync("Naruto");

        await act.Should().ThrowAsync<MalAuthenticationException>();
    }

    [Fact]
    public async Task Search_WhenRetryIsStillUnauthorized_ThrowsMalAuthenticationException()
    {
        await using var server = new FakeMalServer(_ => new FakeResponse(401, """{"error":"invalid_token"}"""));
        var tokens = new Mock<IMalTokenService>();
        tokens.Setup(t => t.GetAccessTokenAsync()).ReturnsAsync("tok123");
        tokens.Setup(t => t.RefreshTokenAsync()).ReturnsAsync(Result.Success());
        var sut = CreateSut(server.BaseUrl, tokens.Object);

        var act = () => sut.SearchByTitleAsync("Naruto");

        await act.Should().ThrowAsync<MalAuthenticationException>();
        server.Requests.Should().HaveCount(2);
    }

    #endregion

    #region Helpers

    private sealed record SearchOutcome(bool IsSuccess, Error? Error, int Count);

    private static string ExpectedFailureCode(bool rich)
        => rich ? MalConstants.Errors.SearchByTitleRichFailed : MalConstants.Errors.SearchByTitleFailed;

    private static async Task<SearchOutcome> RunSearchAsync(MalAnimeSourceService sut, bool rich, string query, int? limit = null)
    {
        if (rich)
        {
            var result = limit is { } l ? await sut.SearchByTitleRichAsync(query, l) : await sut.SearchByTitleRichAsync(query);
            return new SearchOutcome(result.IsSuccess, result.IsFailure ? result.Error : null, result.IsSuccess ? result.Value.Count : 0);
        }

        var simple = limit is { } l2 ? await sut.SearchByTitleAsync(query, l2) : await sut.SearchByTitleAsync(query);
        return new SearchOutcome(simple.IsSuccess, simple.IsFailure ? simple.Error : null, simple.IsSuccess ? simple.Value.Count : 0);
    }

    private static MalAnimeSourceService CreateSut(string baseUrl, IMalTokenService? tokenService = null)
    {
        var repo = new Mock<IPropertyBagRepository>();
        repo.Setup(r => r.GetByKeyAsync(SettingsKeys.MalApiBaseUrl))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.MalApiBaseUrl, Value = baseUrl });

        if (tokenService is null)
        {
            var tokens = new Mock<IMalTokenService>();
            tokens.Setup(t => t.GetAccessTokenAsync()).ReturnsAsync("tok123");
            tokenService = tokens.Object;
        }

        return new MalAnimeSourceService(repo.Object, tokenService, Mock.Of<ILogger<MalAnimeSourceService>>());
    }

    private static string RequestTarget(string rawRequest) => rawRequest.Split("\r\n")[0].Split(' ')[1];

    private static string RequestPath(string rawRequest) => new Uri("http://localhost" + RequestTarget(rawRequest)).AbsolutePath;

    private static string? QueryParam(string rawRequest, string name)
        => HttpUtility.ParseQueryString(new Uri("http://localhost" + RequestTarget(rawRequest)).Query)[name];

    private sealed record FakeResponse(int Status, string Body, string ContentType = "application/json")
    {
        /// <summary>Closes the connection without replying, like a dropped network link.</summary>
        public static readonly FakeResponse Drop = new(0, string.Empty);

        public static FakeResponse Json(string body) => new(200, body);
    }

    /// <summary>Minimal HTTP/1.1 server on a free loopback port; answers one request per connection.</summary>
    private sealed class FakeMalServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Func<int, FakeResponse> _respond;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<string> _requests = [];
        private readonly Task _acceptLoop;

        public FakeMalServer(Func<int, FakeResponse> respond)
        {
            _respond = respond;
            _listener.Start();
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/v2";

        /// <summary>Raw request text (request line + headers) in arrival order.</summary>
        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                    return [.. _requests];
            }
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    try
                    {
                        await HandleAsync(client);
                    }
                    catch (IOException)
                    {
                        // The client went away mid-request; keep serving.
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using var stream = client.GetStream();
            var buffer = new byte[8192];
            var raw = new StringBuilder();
            while (!raw.ToString().Contains("\r\n\r\n"))
            {
                var read = await stream.ReadAsync(buffer, _cts.Token);
                if (read == 0)
                    return;
                raw.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            int index;
            lock (_requests)
            {
                index = _requests.Count;
                _requests.Add(raw.ToString());
            }

            var response = _respond(index);
            if (response.Status == 0)
                return;

            var payload = Encoding.UTF8.GetBytes(response.Body);
            var head = $"HTTP/1.1 {response.Status} {(HttpStatusCode)response.Status}\r\n" +
                       $"Content-Type: {response.ContentType}\r\n" +
                       $"Content-Length: {payload.Length}\r\n" +
                       "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _cts.Token);
            await stream.WriteAsync(payload, _cts.Token);
            await stream.FlushAsync(_cts.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            await Task.WhenAny(_acceptLoop, Task.Delay(TimeSpan.FromSeconds(2)));
            _cts.Dispose();
        }
    }

    #endregion
}
