using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
                var storage = new ResultStorage(Path.Combine(Directory.GetCurrentDirectory(), "Storage"));

                ILogger logger = new LoggerConfiguration()
                    .WriteTo.LiterateConsole()
                    .WriteTo.Seq("http://localhost:5341")
                    .CreateLogger();

                var finder = new ProjectJsonFinder(storage, logger);
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
