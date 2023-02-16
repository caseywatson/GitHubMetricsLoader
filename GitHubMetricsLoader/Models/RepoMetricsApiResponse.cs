using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GitHubMetricsLoader.Models
{
    public class RepoMetricsApiResponse
    {
        [JsonPropertyName("clones")]
        public List<DailyRepoMetrics> RepoMetrics { get; set;} = new List<DailyRepoMetrics>();
    }
}
