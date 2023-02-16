using System;
using System.Text.Json.Serialization;

namespace GitHubMetricsLoader.Models
{
    public class DailyRepoMetrics
    {
        [JsonPropertyName("timestamp")]
        public DateTime Date { get; set; }

        [JsonPropertyName("count")]
        public int TotalClones { get; set; }

        [JsonPropertyName("uniques")]
        public int UniqueClones { get; set; }
    }
}
