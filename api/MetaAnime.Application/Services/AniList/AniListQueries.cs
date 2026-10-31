namespace MetaAnime.Application.Services.AniList;

/// <summary>
/// GraphQL documents for the AniList API. Field selections are deliberately explicit —
/// GraphQL only returns what is requested, and extra fields cost rate-limit budget.
/// </summary>
public static class AniListQueries
{
    /// <summary>
    /// Full cast for an anime by MAL id, one page of 25 character edges per call ordered by
    /// role/relevance/id. VAs come sorted most-relevant first so the "keep first" dedupe keeps
    /// the primary VA per character+language. MUST be paged via <c>$page</c> until
    /// <c>pageInfo.hasNextPage</c> is false — some anime have 126+ character entries.
    /// </summary>
    public const string AnimeCast =
        """
        query ($malId: Int, $page: Int) {
          Media(idMal: $malId, type: ANIME) {
            idMal
            title { romaji english }
            characters(page: $page, perPage: 25, sort: [ROLE, RELEVANCE, ID]) {
              pageInfo { hasNextPage }
              edges {
                role
                node { id name { full } image { large medium } }
                voiceActors(sort: [RELEVANCE, ID]) {
                  id
                  name { full native }
                  languageV2
                  image { large medium }
                  dateOfBirth { year month day }
                  bloodType
                  description
                }
              }
            }
          }
        }
        """;

    /// <summary>Staff (VA) profile by AniList staff id. AniList carries no height/weight.</summary>
    public const string StaffProfile =
        """
        query ($id: Int) {
          Staff(id: $id) {
            id
            name { full native }
            image { large medium }
            dateOfBirth { year month day }
            bloodType
            description
          }
        }
        """;
}
