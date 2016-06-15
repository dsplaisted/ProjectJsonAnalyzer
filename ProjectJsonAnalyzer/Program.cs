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
            MainAsync().Wait();
        }

        static async Task MainAsync()
        {
            try
            {
                CancellationTokenSource cancellationSource = new CancellationTokenSource();

                var storage = new ResultStorage(Path.Combine(Directory.GetCurrentDirectory(), "Storage"));

                ILogger logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.LiterateConsole(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
                    .WriteTo.Seq("http://localhost:5341")
                    .CreateLogger();

                Console.CancelKeyPress += (o, e) =>
                {
                    e.Cancel = true;
                    logger.Information("Cancellation requested");
                    cancellationSource.Cancel();
                };

                string accessToken = null;
                string tokenFile = @"C:\git\ProjectJsonAnalyzer\ProjectJsonAnalyzer\token.txt";
                if (File.Exists(tokenFile))
                {
                    accessToken = File.ReadAllLines(tokenFile).First();
                }

                var finder = new ProjectJsonFinder(storage, logger, cancellationSource.Token, accessToken);
                await finder.FindProjectJsonAsync(@"C:\Users\daplaist\OneDrive - Microsoft\MSBuild for .NET Core\DotNetRepos10000.txt");
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
    }
}
