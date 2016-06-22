using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
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

        public async Task FindProjectJsonAsync()
        {
            TransformManyBlock<GitHubRepo, SearchResult> repoSearchBlock = new TransformManyBlock<GitHubRepo, SearchResult>(repo => SearchRepoAsync(repo),
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount * 4
                    //MaxDegreeOfParallelism = 1
                });

            ActionBlock<SearchResult> downloadFileBlock = new ActionBlock<SearchResult>(DownloadFileAsync, new ExecutionDataflowBlockOptions()
            {
                 MaxDegreeOfParallelism = Environment.ProcessorCount * 4
                 //MaxDegreeOfParallelism = 1
            });

            repoSearchBlock.LinkTo(downloadFileBlock, new DataflowLinkOptions() { PropagateCompletion = true });

            foreach (var repo in _storage.GetAllRepos())
            {
                if (_cancelToken.IsCancellationRequested)
                {
                    break;
                }
                repoSearchBlock.Post(repo);
            }

            repoSearchBlock.Complete();
            await downloadFileBlock.Completion;
        }

        async Task<IEnumerable<SearchResult>> SearchRepoAsync(GitHubRepo repo, bool handleRenamedRepos = true)
        {
            object operation = null;

            try
            {
                List<SearchResult> ret = new List<SearchResult>();

                if (_storage.IsNotFound(repo.Owner, repo.Name))
                {
                    _logger.Verbose("{Repo} previously not found, skipping", repo.Owner + "/" + repo.Name);
                    return Enumerable.Empty<SearchResult>();
                }

                if (_storage.HasRepoResults(repo.Owner, repo.Name))
                {
                    _logger.Verbose("{Repo} already downloaded", repo.Owner + "/" + repo.Name);
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

                        SearchCodeResult result;
                        ApiValidationException validationException = null;
                        ExceptionDispatchInfo validationExceptionDispatchInfo = null;

                        result = await
                            _searchThrottler.RunAsync<SearchCodeResult>(
                                async () =>
                                {
                                    //  Do a try/catch inside here so that renamed repos don't get logged as failures by the throttler
                                    try
                                    {
                                        return await _client.Search.SearchCode(request);
                                    }
                                    catch (ApiValidationException ex) when (handleRenamedRepos)
                                    {
                                        validationException = ex;
                                        validationExceptionDispatchInfo = ExceptionDispatchInfo.Capture(ex);
                                        return null;
                                    }
                                },
                                operation
                            );
                        
                        if (result == null && validationException != null)
                        {
                            _logger.Debug(validationException, "Api validation exception for {Operation}, checking for renamed repo", operation);

                            var renameOperation = new { Operation = "RenameCheck", Repo = repo.Owner + "/" + repo.Name };

                            var potentiallyRenamedRepo = await _throttler.RunAsync<Repository>(
                                async () =>
                                {
                                    try
                                    {
                                        return await _client.Repository.Get(repo.Owner, repo.Name);
                                    }
                                    catch (NotFoundException)
                                    {
                                        return null;
                                    }
                                },
                                renameOperation
                                );

                            if (potentiallyRenamedRepo == null)
                            {
                                _logger.Information("Repo {Repo} not found", renameOperation.Repo);
                                _storage.SaveNotFound(repo.Owner, repo.Name, true);
                                return Enumerable.Empty<SearchResult>();
                            }

                            if (potentiallyRenamedRepo.Owner.Login == repo.Owner && potentiallyRenamedRepo.Name == repo.Name)
                            {
                                _logger.Error("Repo was not renamed, Api validation must have failed for some other reason for {Operation}", operation);
                                validationExceptionDispatchInfo.Throw();
                            }

                            var newRepo = repo.Clone();

                            newRepo.Owner = potentiallyRenamedRepo.Owner.Login;
                            newRepo.Name = potentiallyRenamedRepo.Name;

                            _logger.Information("Repo {OldRepo} has been renamed to {Repo}", renameOperation.Repo, newRepo.Owner + "/" + newRepo.Name);

                            _storage.SaveRenamedRepo(repo.Owner, repo.Name, newRepo);

                            return await SearchRepoAsync(newRepo, false);
                        }

                        foreach (var item in result.Items)
                        {
                            string destFile = _storage.GetFilePath(repo.Owner, repo.Name, item.Path);
                            if (Path.GetFileName(destFile).Equals("project.json", StringComparison.OrdinalIgnoreCase))
                            {
                                ret.Add(new SearchResult(item));
                            }
                            else
                            {
                                _logger.Information("{Path} was not a project.json file in {Repo}, ignoring", item.Path, repo.Owner + "/" + repo.Name);
                            }
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
                _logger.Error(ex, "{Operation} failed", operation);
                return Enumerable.Empty<SearchResult>();
            }
        }

        async Task DownloadFileAsync(SearchResult searchResult)
        {
            if (_cancelToken.IsCancellationRequested)
            {
                return;
            }

            var operation = new { Operation = "Download", Repo = searchResult.RepoOwner + "/" + searchResult.RepoName, Path = searchResult.ResultPath };

            if (_storage.HasFile(searchResult.RepoOwner, searchResult.RepoName, searchResult.ResultPath))
            {
                _logger.Verbose("{Path} was already downloaded from {Repo}", searchResult.ResultPath,
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
