using Azure.Storage.Blobs;
using GitHubMetricsLoader.Loaders.Interfaces;
using GitHubMetricsLoader.Models;
using GitHubMetricsLoader.Models.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace GitHubMetricsLoader.Loaders
{
    public class RepoTrafficMetricsLoader : IRepoMetricsLoader
    {
        private readonly ILogger log;
        private readonly HttpClient gitHubApiClient;
        private readonly BlobContainerClient metricsContainerClient;

        public RepoTrafficMetricsLoader(ILogger log, HttpClient gitHubApiClient, BlobContainerClient metricsContainerClient)
        {
            ArgumentNullException.ThrowIfNull(log, nameof(log));
            ArgumentNullException.ThrowIfNull(gitHubApiClient, nameof(gitHubApiClient));
            ArgumentNullException.ThrowIfNull(metricsContainerClient, nameof(metricsContainerClient));

            this.log = log;
            this.gitHubApiClient = gitHubApiClient;
            this.metricsContainerClient = metricsContainerClient;
        }

        public async Task LoadRepoMetrics(RepoConfiguration repoConfig)
        {
            ArgumentNullException.ThrowIfNull(repoConfig, nameof(repoConfig));

            var repoMetrics = await GetMetricsFromGitHub(repoConfig);

            if (repoMetrics.Any())
            {
                if (await metricsContainerClient.ExistsAsync())
                {

                }
                else
                {

                }
            }
            else
            {
                log.LogWarning($"Repo [{repoConfig}] not currently available.");
            }

            throw new NotImplementedException();
        }

        private BlobClient GetMetricsBlobClient(RepoConfiguration repoConfig) =>
            metricsContainerClient.GetBlobClient($"{repoConfig.RepoOwnerName}/{repoConfig.RepoName}/repo_traffic.csv");

        private async Task<List<RepoTrafficMetrics>> GetMetricsFromGitHub(RepoConfiguration repoConfig)
        {
            var httpRequest = new HttpRequestMessage(
                HttpMethod.Get, $"/repos/{repoConfig.RepoOwnerName}/{repoConfig.RepoName}/traffic/clones");

            var httpResponse = await gitHubApiClient.SendAsync(httpRequest);

            httpResponse.EnsureSuccessStatusCode();

            var httpContent = await httpResponse.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<RepoTrafficMetricsApiResponse>(httpContent);

            return apiResponse.RepoMetrics.ToList();
        }

        private string CreateCsvHeaderRow() =>
            @"""Date"",""Total clones"",""Total unique clones""";

        private string CreateCsvDataRow(RepoTrafficMetrics repoMetrics) =>
            $@"""{repoMetrics.Date}"",{repoMetrics.TotalClones},{repoMetrics.UniqueClones}";
    }
}
