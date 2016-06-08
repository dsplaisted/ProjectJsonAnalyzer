using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ProjectJsonAnalyzer
{
    class ProjectJsonFinder
    {
        ResultStorage _storage;
        ILogger _logger;

        GitHubClient _client;
        HttpClient _httpClient;

        GitHubThrottler _throttler;
        GitHubThrottler _searchThrottler;

        class SearchResult
        {
            public SearchCode SearchCode { get; }
            public TaskCompletionSource<bool> TaskCompletionSource { get; }
            public SearchResult(SearchCode searchCode)
            {
                SearchCode = searchCode;
                TaskCompletionSource = new TaskCompletionSource<bool>();
            }

        }

        public ProjectJsonFinder(ResultStorage storage, ILogger logger)
        {
            _storage = storage;
            _logger = logger;

            _throttler = new GitHubThrottler(logger);
            _searchThrottler = new GitHubThrottler(logger);

            _client = new GitHubClient(new ProductHeaderValue("dsplaisted-project-json-analysis"));
            _httpClient = new HttpClient();
        }

        public async Task FindProjectJsonAsync(string repoListPath)
        {
            TransformManyBlock<GitHubRepo, SearchResult> repoSearchBlock = new TransformManyBlock<GitHubRepo, SearchResult>(repo => SearchRepoAsync(repo));

            ActionBlock<SearchResult> downloadFileBlock = new ActionBlock<SearchResult>(DownloadFileAsync, new ExecutionDataflowBlockOptions()
            {
                 MaxDegreeOfParallelism = Environment.ProcessorCount * 4
                 //MaxDegreeOfParallelism = 1
            });

            repoSearchBlock.LinkTo(downloadFileBlock, new DataflowLinkOptions() { PropagateCompletion = true });

            foreach (var line in File.ReadLines(repoListPath)/*.Take(1)*/)
            {
                var repo = GitHubRepo.Parse(line);
                repoSearchBlock.Post(repo);
            }

            repoSearchBlock.Complete();
            await downloadFileBlock.Completion;
        }

        async Task<IEnumerable<SearchResult>> SearchRepoAsync(GitHubRepo repo)
        {
            List<SearchResult> ret = new List<SearchResult>();

            if (_storage.HasRepoResults(repo.Owner, repo.Name))
            {
                _logger.Information("{Repo} already downloaded", repo.Owner + "/" + repo.Name);
                return ret;
            }

            var request = new SearchCodeRequest()
            {
                FileName = "project.json",
            };
            request.Repos.Add(repo.Owner, repo.Name);

            int totalResultsReturned = 0;

            while (true)
            {
                var result = await 
                    _searchThrottler.RunAsync(
                        () => _client.Search.SearchCode(request),
                        new { Operation = "Search", Repo = repo.Owner + "/" + repo.Name, Page = request.Page }
                    );

                foreach (var item in result.Items)
                {
                    //Console.WriteLine(item.HtmlUrl);
                    //resultProcessor(item);
                    ret.Add(new SearchResult(item));
                }

                if (result.IncompleteResults)
                {
                    _logger.Error("Incomplete search results for {Repo}", repo.Owner + "/" + repo.Name);
                    //Console.WriteLine($"Incomplete results for {repo}");
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

            var ignore = Task.WhenAll(ret.Select(sr => sr.TaskCompletionSource.Task)).ContinueWith(_ => RecordRepoCompleted(repo, ret));

            return ret;
        }

        async Task DownloadFileAsync(SearchResult searchResult)
        {
            var item = searchResult.SearchCode;

            if (_storage.HasFile(searchResult.SearchCode.Repository.Owner.Login, searchResult.SearchCode.Repository.Name, searchResult.SearchCode.Path))
            {
                _logger.Information("{Path} was already downloaded from {Repo}", searchResult.SearchCode.Path,
                    searchResult.SearchCode.Repository.Owner.Login + "/" + searchResult.SearchCode.Repository.Name);
            }
            else
            {
                var file = await _throttler.RunAsync(
                        () => _client.Repository.Content.GetFileContents(item.Repository.Owner.Login, item.Repository.Name, item.Path),
                        new { Operation = "Download", Repo = item.Repository.Owner.Login + "/" + item.Repository.Name, Path = item.Path }
                    );

                _storage.StoreFile(item.Repository.Owner.Login, item.Repository.Name, item.Path, file.Content);
            }

            searchResult.TaskCompletionSource.SetResult(true);
        }

        void RecordRepoCompleted(GitHubRepo repo, IEnumerable<SearchResult> results)
        {
            _storage.RecordRepoResults(repo.Owner, repo.Name, results.Select(r => r.SearchCode));
            _logger.Information("Completed repo {Repo}", repo.Owner + "/" + repo.Name);
        }
    }
}
