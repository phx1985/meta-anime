#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models
{
    public partial class Paging
    {
        [JsonPropertyName("next")]
        public string Next { get; set; }
    }
}
