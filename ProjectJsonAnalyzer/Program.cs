using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectJsonAnalyzer
{
    class Program
    {
        static void Main(string[] args)
        {
            new Program().MainAsync().Wait();
            //new Program().Analyze();
            //new Program().DeleteFiles();
        }

        ResultStorage _storage;

        ILogger _logger;

        public Program()
        {
            //string repoListFile = @"C:\Users\daplaist\OneDrive - Microsoft\MSBuild for .NET Core\DotNetRepos10000.txt";
            string repoListFile = @"C:\Users\daplaist\OneDrive - Microsoft\MSBuild for .NET Core\DotNetReposAll.txt";

            _storage = new ResultStorage(Path.Combine(Directory.GetCurrentDirectory(), "Storage"),
                repoListFile);

            _logger = new LoggerConfiguration()
                   .MinimumLevel.Verbose()
                   .WriteTo.LiterateConsole(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
                   .WriteTo.Seq("http://localhost:5341")
                   .CreateLogger();
        }

        async Task MainAsync()
        {
            try
            {
                CancellationTokenSource cancellationSource = new CancellationTokenSource();

                Console.CancelKeyPress += (o, e) =>
                {
                    e.Cancel = true;
                    _logger.Information("Cancellation requested");
                    cancellationSource.Cancel();
                };

                string accessToken = null;
                string tokenFile = @"C:\git\ProjectJsonAnalyzer\ProjectJsonAnalyzer\token.txt";
                if (File.Exists(tokenFile))
                {
                    accessToken = File.ReadAllLines(tokenFile).First();
                }

                var finder = new ProjectJsonFinder(_storage, _logger, cancellationSource.Token, accessToken);
                await finder.FindProjectJsonAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }
                throw;
            }
        }

        void Analyze()
        {
            int totalRepos = 0;
            int totalReposSearched = 0;
            int notFoundRepos = 0;
            int remainingRepos = 0;
            int totalResults = 0;
            int downloadedFiles = 0;
            int remainingFiles = 0;

            Dictionary<string, int> ownerCounts = new Dictionary<string, int>();


            foreach (var repo in _storage.GetAllRepos())
            {
                totalRepos++;

                if (_storage.HasRepoResults(repo.Owner, repo.Name))
                {
                    totalReposSearched++;

                    foreach (var result in _storage.GetRepoResults(repo.Owner, repo.Name))
                    {
                        if (ownerCounts.ContainsKey(repo.Owner))
                        {
                            ownerCounts[repo.Owner]++;
                        }
                        else
                        {
                            ownerCounts[repo.Owner] = 1;
                        }

                        totalResults++;
                        if (_storage.HasFile(repo.Owner, repo.Name, result.ResultPath))
                        {
                            downloadedFiles++;
                        }
                        else
                        {
                            remainingFiles++;
                        }
                    }

                }
                else if (_storage.IsNotFound(repo.Owner, repo.Name))
                {
                    notFoundRepos++;
                }
                else
                {
                    remainingRepos++;
                }
            }

            Console.WriteLine($"Total repos:        {totalRepos}");
            Console.WriteLine($"Repos searched:     {totalReposSearched}");
            Console.WriteLine($"Not found repos:    {notFoundRepos}");
            Console.WriteLine($"Remaining repos:    {remainingRepos}");
            Console.WriteLine($"Total results:      {totalResults}");
            Console.WriteLine($"Results downloaded: {downloadedFiles}");
            Console.WriteLine($"Remaining files:    {remainingFiles}");

            Console.WriteLine();

            foreach (var kvp in ownerCounts.OrderByDescending(kvp => kvp.Value).Take(20))
            {
                Console.WriteLine($"{kvp.Key}\t{kvp.Value}");
            }

        }

        void DeleteFiles()
        {
            foreach (var repo in _storage.GetAllRepos())
            {
                if (_storage.HasRepoResults(repo.Owner, repo.Name))
                {
                    bool changed = false;
                    var results = _storage.GetRepoResults(repo.Owner, repo.Name);
                    List<SearchResult> newResults = new List<SearchResult>();
                    foreach (var r in results)
                    {
                        string filePath = _storage.GetFilePath(repo.Owner, repo.Name, r.ResultPath);
                        if (Path.GetFileName(filePath).Equals("project.json", StringComparison.OrdinalIgnoreCase))
                        {
                            newResults.Add(r);
                        }
                        else
                        {
                            changed = true;
                            if (File.Exists(filePath))
                            {
                                _logger.Information("Deleting {Path} from {Repo}", r.ResultPath, repo.Owner + "/" + repo.Name);
                                File.Delete(filePath);
                            }
                            else
                            {
                                _logger.Information("{Path} not present to delete from {Repo}", r.ResultPath, repo.Owner + "/" + repo.Name);
                            }
                        }
                    }

                    if (changed)
                    {
                        _storage.RecordRepoResults(repo.Owner, repo.Name, newResults);
                    }
                }
            }
        }
    }
}
