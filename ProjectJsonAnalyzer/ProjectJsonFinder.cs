using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ProjectJsonAnalyzer
{
    class ProjectJsonFinder
    {
        ResultStorage _storage;
        ILogger _logger;
        CancellationToken _cancelToken;

        GitHubClient _client;
        HttpClient _httpClient;

        GitHubThrottler _throttler;
        GitHubThrottler _searchThrottler;

        public ProjectJsonFinder(ResultStorage storage, ILogger logger, CancellationToken cancelToken, string accessToken = null)
        {
            _storage = storage;
            _logger = logger;
            _cancelToken = cancelToken;

            _throttler = new GitHubThrottler(logger);
            _searchThrottler = new GitHubThrottler(logger);

            _client = new GitHubClient(new ProductHeaderValue("dsplaisted-project-json-analysis"));
            if (accessToken != null)
            {
                _client.Credentials = new Credentials(accessToken);
            }
            _httpClient = new HttpClient();
        }

        public async Task FindProjectJsonAsync(string repoListPath)
        {
            TransformManyBlock<GitHubRepo, SearchResult> repoSearchBlock = new TransformManyBlock<GitHubRepo, SearchResult>(repo => SearchRepoAsync(repo),
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = 1
                });

            ActionBlock<SearchResult> downloadFileBlock = new ActionBlock<SearchResult>(DownloadFileAsync, new ExecutionDataflowBlockOptions()
            {
                 //MaxDegreeOfParallelism = Environment.ProcessorCount * 4
                 MaxDegreeOfParallelism = 1
            });

            repoSearchBlock.LinkTo(downloadFileBlock, new DataflowLinkOptions() { PropagateCompletion = true });

            foreach (var line in File.ReadLines(repoListPath)/*.Take(1)*/)
            {
                if (_cancelToken.IsCancellationRequested)
                {
                    break;
                }
                var repo = GitHubRepo.Parse(line);
                repoSearchBlock.Post(repo);
            }

            repoSearchBlock.Complete();
            await downloadFileBlock.Completion;
        }

        async Task<IEnumerable<SearchResult>> SearchRepoAsync(GitHubRepo repo)
        {
            object operation = null;

            try
            {
                List<SearchResult> ret = new List<SearchResult>();

                if (_storage.HasRepoResults(repo.Owner, repo.Name))
                {
                    _logger.Information("{Repo} already downloaded", repo.Owner + "/" + repo.Name);
                    ret = _storage.GetRepoResults(repo.Owner, repo.Name).ToList();
                }
                else
                {
                    var request = new SearchCodeRequest()
                    {
                        FileName = "project.json",
                    };
                    request.Repos.Add(repo.Owner, repo.Name);

                    int totalResultsReturned = 0;

                    while (true)
                    {
                        if (_cancelToken.IsCancellationRequested)
                        {
                            return Enumerable.Empty<SearchResult>();
                        }

                        operation = new { Operation = "Search", Repo = repo.Owner + "/" + repo.Name, Page = request.Page };
                        var result = await
                            _searchThrottler.RunAsync(
                                () => _client.Search.SearchCode(request),
                                operation
                            );

                        foreach (var item in result.Items)
                        {
                            ret.Add(new SearchResult(item));
                        }

                        if (result.IncompleteResults)
                        {
                            _logger.Error("Incomplete search results for {Repo}", repo.Owner + "/" + repo.Name);
                            break;
                        }

                        totalResultsReturned += result.Items.Count;

                        if (totalResultsReturned >= result.TotalCount)
                        {
                            break;
                        }
                        else
                        {
                            request.Page += 1;
                        }
                    }

                    _storage.RecordRepoResults(repo.Owner, repo.Name, ret);
                    _logger.Information("Completed searching repo {Repo}", repo.Owner + "/" + repo.Name);
                }
                

                return ret;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "{Operation} failed", new[] { operation });
                return Enumerable.Empty<SearchResult>();
            }
        }

        async Task DownloadFileAsync(SearchResult searchResult)
        {
            var operation = new { Operation = "Download", Repo = searchResult.RepoOwner + "/" + searchResult.RepoName, Path = searchResult.ResultPath };

            if (_storage.HasFile(searchResult.RepoOwner, searchResult.RepoName, searchResult.ResultPath))
            {
                _logger.Information("{Path} was already downloaded from {Repo}", searchResult.ResultPath,
                    searchResult.RepoOwner + "/" + searchResult.RepoName);
            }
            else
            {
                try
                {
                    var file = await _throttler.RunAsync(
                            () => _client.Repository.Content.GetFileContents(searchResult.RepoOwner, searchResult.RepoName, searchResult.ResultPath),
                            operation
                        );

                    _storage.StoreFile(searchResult.RepoOwner, searchResult.RepoName, searchResult.ResultPath, file.Content);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "{Operation} failed", new[] { operation });
                }
            }
        }

        void RecordRepoCompleted(GitHubRepo repo, IEnumerable<SearchResult> results)
        {
            _storage.RecordRepoResults(repo.Owner, repo.Name, results);
            _logger.Information("Completed repo {Repo}", repo.Owner + "/" + repo.Name);
        }
    }
}
