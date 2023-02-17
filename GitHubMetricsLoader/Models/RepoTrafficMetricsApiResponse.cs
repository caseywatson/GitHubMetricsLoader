using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GitHubMetricsLoader.Models
{
    public class RepoTrafficMetricsApiResponse
    {
        [JsonPropertyName("clones")]
        public List<RepoTrafficMetrics> RepoMetrics { get; set;} = new List<RepoTrafficMetrics>();
    }
}
