using Octokit;
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
        GitHubClient _client;
        HttpClient _httpClient;
        string _storageRoot;

        GitHubThrottler _throttler = new GitHubThrottler();
        GitHubThrottler _searchThrottler = new GitHubThrottler();

        public ProjectJsonFinder()
        {
            _client = new GitHubClient(new ProductHeaderValue("dsplaisted-project-json-analysis"));
            _httpClient = new HttpClient();
            _storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "Storage");
        }

        public async Task FindProjectJsonAsync(string repoListPath)
        {
            TransformManyBlock<GitHubRepo, SearchCode> repoSearchBlock = new TransformManyBlock<GitHubRepo, SearchCode>(repo => SearchRepoAsync(repo));

            ActionBlock<SearchCode> downloadFileBlock = new ActionBlock<SearchCode>(DownloadFileAsync, new ExecutionDataflowBlockOptions()
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

        public async Task<IEnumerable<SearchCode>> SearchRepoAsync(GitHubRepo repo)
        {
            List<SearchCode> ret = new List<SearchCode>();

            var request = new SearchCodeRequest()
            {
                FileName = "project.json",
            };
            request.Repos.Add(repo.Owner, repo.Name);

            int totalResultsReturned = 0;

            while (true)
            {
                var result = await 
                    _searchThrottler.RunAsync( () =>
                        _client.Search.SearchCode(request)
                    );

                foreach (var item in result.Items)
                {
                    //Console.WriteLine(item.HtmlUrl);
                    //resultProcessor(item);
                    ret.Add(item);
                }

                if (result.IncompleteResults)
                {
                    Console.WriteLine($"Incomplete results for {repo}");
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

            return ret;
        }

        public async Task DownloadFileAsync(SearchCode item)
        {
            string path = Path.Combine(_storageRoot, item.Repository.Owner.Login, item.Repository.Name, item.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var file = await _throttler.RunAsync( () =>
                    _client.Repository.Content.GetFileContents(item.Repository.Owner.Login, item.Repository.Name, item.Path)
                );

            File.WriteAllText(path, file.Content);

            //var uri = item.GitUrl;

            //var response = await _httpClient.GetAsync(uri);
            //using (var fs = File.OpenWrite(path))
            //{
            //    await response.Content.CopyToAsync(fs);
            //}
        }
    }
}
