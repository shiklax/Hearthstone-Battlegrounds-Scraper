using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HearthstoneScraper.ApiModels
{
    public class LeaderboardApiResponse
    {
        [JsonPropertyName("seasonId")] public int SeasonId { get; set; }
        [JsonPropertyName("leaderboard")] public LeaderboardData Leaderboard { get; set; }
    }

    public class LeaderboardData
    {
        [JsonPropertyName("rows")] public List<LeaderboardRow> Rows { get; set; }
        [JsonPropertyName("pagination")] public PaginationData Pagination { get; set; }
    }

    public class LeaderboardRow
    {
        [JsonPropertyName("rank")] public int Rank { get; set; }
        [JsonPropertyName("accountid")] public string AccountId { get; set; }
        [JsonPropertyName("rating")] public int Rating { get; set; }
    }

    public class PaginationData
    {
        [JsonPropertyName("totalPages")] public int TotalPages { get; set; }
    }
}