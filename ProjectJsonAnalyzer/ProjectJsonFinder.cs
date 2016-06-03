using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ProjectJsonAnalyzer
{
    class ProjectJsonFinder
    {
        GitHubClient _client;

        public ProjectJsonFinder()
        {
            _client = new GitHubClient(new ProductHeaderValue("dsplaisted-project-json-analysis"));
        }

        public async Task RunAsync()
        {
            await SearchRepo("dotnet/corefx", item => Console.WriteLine(item.HtmlUrl));
        }

        public async Task SearchRepo(string repo, Action<SearchCode> resultProcessor)
        {
            var request = new SearchCodeRequest()
            {
                FileName = "project.json",
            };
            request.Repos.Add(repo);

            int totalResultsReturned = 0;

            while (true)
            {
                var result = await _client.Search.SearchCode(request);

                foreach (var item in result.Items)
                {
                    //Console.WriteLine(item.HtmlUrl);
                    resultProcessor(item);
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
        }

   
    }
}
