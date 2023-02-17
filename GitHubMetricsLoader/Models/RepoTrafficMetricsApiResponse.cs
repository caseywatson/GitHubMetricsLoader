using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GitHubMetricsLoader.Models
{
    public class RepoMetricsApiResponse
    {
        [JsonPropertyName("clones")]
        public List<DailyTrafficMetrics> RepoMetrics { get; set;} = new List<DailyTrafficMetrics>();
    }
}
