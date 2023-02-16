using System.Text.Json.Serialization;

namespace GitHubMetricsLoader.Models
{
    public class LoaderRepoConfig
    {
        [JsonPropertyName("repo_owner_name")]
        public string RepoOwnerName { get; set; }

        [JsonPropertyName("repo_name")]
        public string RepoName { get; set; }

        public override string ToString() =>
            $"{RepoOwnerName}/{RepoName}";
    }
}
