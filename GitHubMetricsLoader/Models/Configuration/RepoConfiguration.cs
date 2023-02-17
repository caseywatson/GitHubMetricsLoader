using System.Text.Json.Serialization;

namespace GitHubMetricsLoader.Models.Configuration
{
    public class RepoConfiguration
    {
        [JsonPropertyName("repo_owner_name")]
        public string RepoOwnerName { get; set; }

        [JsonPropertyName("repo_name")]
        public string RepoName { get; set; }

        public override string ToString() => $"{RepoOwnerName}/{RepoName}";
    }
}
