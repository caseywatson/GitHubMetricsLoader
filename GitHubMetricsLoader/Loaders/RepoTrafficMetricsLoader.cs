using Azure.Storage.Blobs;
using GitHubMetricsLoader.Loaders.Interfaces;
using GitHubMetricsLoader.Models;
using GitHubMetricsLoader.Models.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GitHubMetricsLoader.Loaders
{
    public class RepoTrafficMetricsLoader : IRepoMetricsLoader
    {
        public const string MetricsBlobWatermarkDatePropertyName = "watermark_date";

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

            var metrics = await GetMetricsFromGitHub(repoConfig);

            if (metrics.Any())
            {
                var metricsBlobClient = GetMetricsBlobClient(repoConfig);

                if (await metricsBlobClient.ExistsAsync())
                {
                    var watermarkDate = await UpdateMetricsBlob(metrics, metricsBlobClient);

                    log.LogInformation(
                        $"Repo [{repoConfig}] traffic metrics blob [{metricsBlobClient.Name}] successfully [updated]. " +
                        $"Latest metric (watermark) date is [{watermarkDate}].");
                }
                else
                {
                    var watermarkDate = await CreateMetricsBlob(metrics, metricsBlobClient);

                    log.LogInformation(
                        $"Repo [{repoConfig}] traffic metrics blob [{metricsBlobClient.Name}] successfully [created]. " +
                        $"Latest metric (watermark) date is [{watermarkDate}].");
                }
            }
            else
            {
                log.LogWarning($"No repo [{repoConfig}] traffic metrics currently available.");
            }
        }

        private async Task<DateTime> CreateMetricsBlob(List<RepoTrafficMetrics> metrics, BlobClient metricsBlobClient)
        {
            var contentBuilder = new StringBuilder();
            var watermarkDate = metrics.Max(m => m.Date);

            contentBuilder.AppendLine(CreateCsvHeaderRow());

            AppendMetricsToCsvContent(contentBuilder, metrics);

            await UploadBlobContent(metricsBlobClient, contentBuilder.ToString(), watermarkDate);

            return watermarkDate;
        }

        private async Task<DateTime> UpdateMetricsBlob(List<RepoTrafficMetrics> metrics, BlobClient metricsBlobClient)
        {
            var watermarkDate = await TryGetMetricsBlobWatermarkDate(metricsBlobClient);

            if (watermarkDate != null)
            {
                metrics = metrics.Where(m => m.Date > watermarkDate).ToList();
            }

            watermarkDate = metrics.Max(m => m.Date);

            var content = await DownloadBlobContent(metricsBlobClient);
            var contentBuilder = new StringBuilder(content);

            AppendMetricsToCsvContent(contentBuilder, metrics);

            await UploadBlobContent(metricsBlobClient, contentBuilder.ToString(), watermarkDate.Value);

            return watermarkDate.Value;
        }

        private async Task<string> DownloadBlobContent(BlobClient metricsBlobClient)
        {
            var contentReponse = await metricsBlobClient.DownloadContentAsync();
            var contentBytes = contentReponse.Value.Content.ToArray();
            var content = Encoding.UTF8.GetString(contentBytes);

            return content;
        }

        private async Task UploadBlobContent(BlobClient metricsBlobClient, string content, DateTime watermarkDate)
        {
            var contentBytes = Encoding.UTF8.GetBytes(content);

            await metricsBlobClient.UploadAsync(new BinaryData(contentBytes), overwrite: true);
            await metricsBlobClient.SetMetadataAsync(new Dictionary<string, string> { [MetricsBlobWatermarkDatePropertyName] = watermarkDate.ToString("s") });
        }

        private async Task<DateTime?> TryGetMetricsBlobWatermarkDate(BlobClient metricsBlobClient)
        {
            var blobProps = (await metricsBlobClient.GetPropertiesAsync()).Value;

            if (blobProps.Metadata.ContainsKey(MetricsBlobWatermarkDatePropertyName))
            {
                return DateTime.Parse(blobProps.Metadata[MetricsBlobWatermarkDatePropertyName]);
            }
            else
            {
                return null;
            }
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

        private string CreateCsvMetricRow(RepoTrafficMetrics repoMetrics) =>
            $@"""{repoMetrics.Date}"",{repoMetrics.TotalClones},{repoMetrics.UniqueClones}";

        private void AppendMetricsToCsvContent(StringBuilder contentBuilder, List<RepoTrafficMetrics> metrics)
        {
            foreach (var metric in metrics)
            {
                contentBuilder.AppendLine(CreateCsvMetricRow(metric));
            }
        }
    }
}
