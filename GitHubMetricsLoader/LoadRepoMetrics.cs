using Azure.Storage.Blobs;
using GitHubMetricsLoader.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Environment;

namespace GitHubMetricsLoader
{
    public class LoadRepoMetrics
    {
        public const string LoaderConfigBlobPath = "config/loader_config.json";

        public const string MetricsBlobName = "repo_metrics.csv";
        public const string MetricsBlobContainerName = "repo_metrics";
        public const string MetricsBlobWatermarkDatePropertyName = "watermark_date";

        public const string Env_GitHubPat = "GitHubPat";
        public const string Env_StorageConnectionString = "StorageConnectionString";

        [FunctionName("LoadRepoMetrics")]
        public async Task Run(
            [TimerTrigger("0 0 6 * * *")] TimerInfo time,
            [Blob(LoaderConfigBlobPath, FileAccess.Read, Connection = Env_StorageConnectionString)] string loaderConfigContents,
            ILogger log)
        {
            try
            {
                var loaderConfig = JsonSerializer.Deserialize<List<LoaderRepoConfig>>(loaderConfigContents);

                if (loaderConfig.Any())
                {
                    var blobContainerClient = CreateMetricsBlobContainerClient();
                    var gitHubApiClient = CreateGitHubHttpClient();

                    log.LogInformation($"Loading metrics for [{loaderConfig.Count}] repo(s)...");

                    foreach (var repoConfig in loaderConfig)
                    {
                        try
                        {
                            log.LogInformation($"Loading metrics for repo [{repoConfig}]...");

                            var repoMetrics = await GetRepoMetricsFromGitHub(repoConfig, gitHubApiClient, log);

                            if (repoMetrics.Any())
                            {
                                var metricsBlobName = $"{repoConfig}/{MetricsBlobName}".ToLower();
                                var metricsBlobClient = blobContainerClient.GetBlobClient(metricsBlobName);

                                if (await metricsBlobClient.ExistsAsync())
                                {
                                    await UpdateMetricsBlob(metricsBlobClient, repoMetrics);
                                }
                                else
                                {
                                    await CreateMetricsBlob(metricsBlobClient, repoMetrics);
                                }

                                log.LogInformation($"Repo metrics [{repoConfig}] successfully loaded.");
                            }
                            else
                            {
                                log.LogWarning($"Repo [{repoConfig}] has no metrics available.");
                            }
                        }
                        catch (Exception ex)
                        {
                            log.LogError($"An error occurred while trying to load metrics for repo [{repoConfig}]: [{ex.Message}].");
                        }
                    }
                }
                else
                {
                    log.LogWarning($"No repos configured in [{LoaderConfigBlobPath}]. Going back to sleep...");
                }
            }
            catch (Exception ex)
            {
                log.LogError($"An error occurred while trying to load repo metrics: [{ex.Message}]. Going back to sleep...");
            }
        }

        private async Task CreateMetricsBlob(BlobClient metricsBlobClient, IEnumerable<DailyRepoMetrics> repoMetrics)
        {
            var csvBuilder = new StringBuilder(CreateCsvHeaderRow());

            AppendMetricsToCsvBuilder(csvBuilder, repoMetrics);

            await SaveMetricsBlob(csvBuilder, metricsBlobClient, CalculateMetricsWatermarkDate(repoMetrics));
        }

        private async Task UpdateMetricsBlob(BlobClient metricsBlobClient, IEnumerable<DailyRepoMetrics> repoMetrics)
        {
            var blobWatermarkDate = await TryGetMetricsBlobWatermarkDate(metricsBlobClient);

            if (blobWatermarkDate != null)
            {
                repoMetrics = repoMetrics.Where(m => m.Date > blobWatermarkDate).ToList();
            }

            var csvContentBytes = (await metricsBlobClient.DownloadContentAsync()).Value.Content.ToArray();
            var csvContent = Encoding.UTF8.GetString(csvContentBytes);
            var csvBuilder = new StringBuilder(csvContent);

            AppendMetricsToCsvBuilder(csvBuilder, repoMetrics);

            await SaveMetricsBlob(csvBuilder, metricsBlobClient, CalculateMetricsWatermarkDate(repoMetrics));
        }

        private async Task SaveMetricsBlob(StringBuilder csvBuilder, BlobClient metricsBlobClient, DateTime watermarkDate)
        {
            var csvContent = csvBuilder.ToString();
            var csvContentBytes = Encoding.UTF8.GetBytes(csvContent);

            await metricsBlobClient.UploadAsync(new BinaryData(csvContentBytes), overwrite: true);
            await metricsBlobClient.SetMetadataAsync(new Dictionary<string, string> { [MetricsBlobWatermarkDatePropertyName] = watermarkDate.ToString() });
        }

        private void AppendMetricsToCsvBuilder(StringBuilder csvBuilder, IEnumerable<DailyRepoMetrics> repoMetrics)
        {
            foreach (var repoMetric in repoMetrics)
            {
                csvBuilder.AppendLine(CreateCsvDataRow(repoMetric));
            }
        }

        private string GetGitHubPat() =>
            GetEnvironmentVariable(Env_GitHubPat)
            ?? throw new InvalidOperationException($"[{Env_GitHubPat}] environment variable not configured.");

        private string GetStorageConnectionString() =>
            GetEnvironmentVariable(Env_StorageConnectionString)
            ?? throw new InvalidOperationException($"[{Env_StorageConnectionString}] environment variable not configured.");

        private BlobContainerClient CreateMetricsBlobContainerClient()
        {
            var storageConnString = GetStorageConnectionString();
            var blobServiceClient = new BlobServiceClient(storageConnString);

            return blobServiceClient.GetBlobContainerClient(MetricsBlobContainerName);
        }

        private string CreateCsvHeaderRow() =>
            @"""Date"",""Total clones"",""Total unique clones""";

        private string CreateCsvDataRow(DailyRepoMetrics repoMetrics) =>
            $@"""{repoMetrics.Date}"",{repoMetrics.TotalClones},{repoMetrics.UniqueClones}";

        private DateTime CalculateMetricsWatermarkDate(IEnumerable<DailyRepoMetrics> repoMetrics) =>
            repoMetrics.Max(m => m.Date);

        private async Task<DateTime?> TryGetMetricsBlobWatermarkDate(BlobClient metricsBlobClient)
        {
            var blobProperties = (await metricsBlobClient.GetPropertiesAsync()).Value;

            if (blobProperties.Metadata.ContainsKey(MetricsBlobWatermarkDatePropertyName))
            {
                return DateTime.Parse(blobProperties.Metadata[MetricsBlobWatermarkDatePropertyName]);
            }
            else
            {
                return null;
            }
        }

        private async Task<IEnumerable<DailyRepoMetrics>> GetRepoMetricsFromGitHub(LoaderRepoConfig repoConfig, HttpClient gitHubApiClient, ILogger log)
        {
            var httpRequest = new HttpRequestMessage(
                HttpMethod.Get, $"/repos/{repoConfig.RepoOwnerName}/{repoConfig.RepoName}/traffic/clones");

            var httpResponse = await gitHubApiClient.SendAsync(httpRequest);

            httpResponse.EnsureSuccessStatusCode();

            var httpContent = await httpResponse.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<RepoMetricsApiResponse>(httpContent);

            return apiResponse.RepoMetrics;
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
