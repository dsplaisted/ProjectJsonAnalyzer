using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                var finder = new ProjectJsonFinder();
                await finder.RunAsync();
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
