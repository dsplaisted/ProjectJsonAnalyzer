using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectJsonAnalyzer
{
    class ResultStorage
    {
        string _storageRoot;

        public ResultStorage(string storageRoot)
        {
            _storageRoot = storageRoot;
        }

        string GetRepoFolder(string owner, string name)
        {
            return Path.Combine(_storageRoot, owner, name);
        }

        string GetFilePath(string owner, string name, string path)
        {
            return Path.Combine(GetRepoFolder(owner, name), path.Replace('/', Path.DirectorySeparatorChar));
        }

        public bool HasFile(string owner, string name, string path)
        {
            return File.Exists(GetFilePath(owner, name, path));
        }

        public void StoreFile(string owner, string name, string path, string contents)
        {
            string storagePath = GetFilePath(owner, name, path);
            Directory.CreateDirectory(Path.GetDirectoryName(storagePath));

            File.WriteAllText(storagePath, contents);
        }

        string GetResultsFilePath(string owner, string name)
        {
            return Path.Combine(GetRepoFolder(owner, name), "results.txt");
        }

        public bool HasRepoResults(string owner, string name)
        {
            return File.Exists(GetResultsFilePath(owner, name));
        }

        public void RecordRepoResults(string owner, string name, IEnumerable<SearchResult> results)
        {
            string storagePath = GetResultsFilePath(owner, name);
            Directory.CreateDirectory(Path.GetDirectoryName(storagePath));
            using (var sw = new StreamWriter(storagePath))
            {
                foreach (var result in results)
                {
                    sw.WriteLine(result.ResultPath);
                }
            }
        }

        public IEnumerable<SearchResult> GetRepoResults(string owner, string name)
        {
            if (!HasRepoResults(owner, name))
            {
                return Enumerable.Empty<SearchResult>();
            }

            return File.ReadAllLines(GetResultsFilePath(owner, name))
                .Select(line => new SearchResult(owner, name, line));
        }
    }
}
