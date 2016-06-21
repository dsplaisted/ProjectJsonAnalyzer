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
        string _repoListFile;
        string _storageRoot;


        public ResultStorage(string storageRoot, string repoListFile)
        {
            _storageRoot = storageRoot;
            _repoListFile = repoListFile;
        }

        public IEnumerable<GitHubRepo> GetAllRepos()
        {
            foreach (var line in File.ReadLines(_repoListFile))
            {
                var repo = GitHubRepo.Parse(line);
                yield return repo;
            }

        }

        string GetRepoFolder(string owner, string name)
        {
            return Path.Combine(_storageRoot, owner, name);
        }

        public string GetFilePath(string owner, string name, string path)
        {
            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                path = path.Substring(1);
            }
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

        //  Returns true if the repo has been renamed
        public GitHubRepo GetRenamedRepo(GitHubRepo repo)
        {
            string renameFile = Path.Combine(GetRepoFolder(repo.Owner, repo.Name), "rename.txt");
            if (File.Exists(renameFile))
            {
                var line = File.ReadAllLines(renameFile).First();
                return GitHubRepo.Parse(line);
            }
            else
            {
                return null;
            }
        }

        public void SaveRenamedRepo(string oldOwner, string oldName, GitHubRepo newRepo)
        {
            string renameFile = Path.Combine(GetRepoFolder(oldOwner, oldName), "rename.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(renameFile));
            File.WriteAllText(renameFile, newRepo.ToString());
        }

        public bool IsNotFound(string owner, string name)
        {
            string notFoundMarkerFile = Path.Combine(GetRepoFolder(owner, name), "notfound.txt");
            return File.Exists(notFoundMarkerFile);
        }

        public void SaveNotFound(string owner, string name, bool value)
        {
            string notFoundMarkerFile = Path.Combine(GetRepoFolder(owner, name), "notfound.txt");
            if (value)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(notFoundMarkerFile));
                File.WriteAllText(notFoundMarkerFile, "");
            }
            else
            {
                if (File.Exists(notFoundMarkerFile))
                {
                    File.Delete(notFoundMarkerFile);
                }
            }
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
