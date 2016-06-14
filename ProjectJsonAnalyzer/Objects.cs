using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectJsonAnalyzer
{
    class SearchResult
    {
        public string RepoOwner { get; }
        public string RepoName { get; }
        public string ResultPath { get; }

        public SearchResult(string repoOwner, string repoName, string resultPath)
        {
            RepoOwner = repoOwner;
            RepoName = repoName;
            ResultPath = resultPath;
        }

        public SearchResult(SearchCode searchCode)
            : this(searchCode.Repository.Owner.Login, searchCode.Repository.Name, searchCode.Path)
        {
        }

    }
}
