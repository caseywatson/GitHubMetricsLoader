using Azure.Storage.Blobs;
using GitHubMetricsLoader.Loaders;
using GitHubMetricsLoader.Loaders.Interfaces;
using GitHubMetricsLoader.Models.Configuration;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Environment;

namespace GitHubMetricsLoader
{
    public class LoadRepoMetrics
    {
        private const string Env_GitHubPat = "GitHubPat";
        private const string Env_StorageConnectionString = "StorageConnectionString";
        private const string LoaderConfigBlobPath = "config/loader_config.json";
        private const string MetricsBlobContainerName = "metrics";

        [FunctionName("LoadRepoMetrics")]
        public async Task Run(
            [TimerTrigger("0 0 6 * * *", RunOnStartup = true)] TimerInfo time,
            [Blob(LoaderConfigBlobPath, FileAccess.Read, Connection = Env_StorageConnectionString)] string loaderConfigContents,
            ILogger log)
        {
            try
            {
                var loaderConfig = JsonSerializer.Deserialize<List<RepoConfiguration>>(loaderConfigContents);

                if (loaderConfig.Any())
                {
                    var gitHubApiClient = CreateGitHubHttpClient();
                    var metricsContainerClient = await CreateMetricsBlobContainerClient();

                    var loaders = new List<IRepoMetricsLoader>
                    {
                        new RepoTrafficMetricsLoader(log, gitHubApiClient, metricsContainerClient)
                    };

                    log.LogInformation($"Trying to load metrics for [{loaderConfig.Count}] GitHub repo(s)...");

                    foreach (var repoConfig in loaderConfig)
                    {
                        log.LogInformation($"Trying to load metrics for GitHub repo [{repoConfig}]...");

                        try
                        {
                            foreach (var loader in loaders)
                            {
                                await loader.LoadRepoMetrics(repoConfig);
                            }
                        }
                        catch (Exception ex)
                        {
                            log.LogError($"An error occurred while trying to load metrics for GitHub repo [{repoConfig}]: [{ex.Message}].");
                        }
                    }
                }
                else
                {
                    log.LogWarning($"No GitHub repos configured in [{LoaderConfigBlobPath}].");
                }
            }
            catch (Exception ex)
            {
                log.LogError($"An error occurred while trying to load GitHub repo metrics: [{ex.Message}].");
            }
        }

        private string GetGitHubPat() =>
            GetEnvironmentVariable(Env_GitHubPat)
            ?? throw new InvalidOperationException($"[{Env_GitHubPat}] environment variable not configured.");

        private string GetStorageConnectionString() =>
            GetEnvironmentVariable(Env_StorageConnectionString)
            ?? throw new InvalidOperationException($"[{Env_StorageConnectionString}] environment variable not configured.");

        private async Task<BlobContainerClient> CreateMetricsBlobContainerClient()
        {
            var storageConnString = GetStorageConnectionString();
            var blobServiceClient = new BlobServiceClient(storageConnString);
            var blobContainerClient = blobServiceClient.GetBlobContainerClient(MetricsBlobContainerName);

            await blobContainerClient.CreateIfNotExistsAsync();

            return blobContainerClient;
        }

        private HttpClient CreateGitHubHttpClient()
        {
            var pat = GetGitHubPat();
            var httpClient = new HttpClient { BaseAddress = new Uri("https://api.github.com") };

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            httpClient.DefaultRequestHeaders.Add("Authorization", $"token {pat}");
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHub-Metrics-Loader");

            return httpClient;
        }
    }
}
