using GitHubMetricsLoader.Models.Configuration;
using System.Threading.Tasks;

namespace GitHubMetricsLoader.Loaders.Interfaces
{
    public interface IRepoMetricsLoader
    {
        Task LoadRepoMetrics(RepoConfiguration repoConfig);
    }
}
