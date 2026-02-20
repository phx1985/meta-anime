using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models;

public partial class Paging
{
    /// <summary>
    /// Gets a value indicating whether this instance has next page.
    /// </summary>
    [JsonIgnore]
    public bool HasNextPage => !string.IsNullOrWhiteSpace(Next);

}
