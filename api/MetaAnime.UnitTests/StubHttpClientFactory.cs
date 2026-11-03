using System.Net.Http;

namespace MetaAnime.UnitTests;

/// <summary>
/// Hands out plain <see cref="HttpClient"/> instances for tests that exercise config-guard
/// paths or talk to a local fake server (where the default socket handler is what we want).
/// </summary>
public sealed class StubHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
