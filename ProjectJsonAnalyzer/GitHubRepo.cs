using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectJsonAnalyzer
{
    class GitHubRepo
    {
        public string ID { get; set; }
        public string Owner { get; set; }
        public string Name { get; set; }
        public string Language { get; set; }
        public int Stars { get; set; }

        public static GitHubRepo Parse(string line)
        {
            var fields = line.Split('\t');
            var ret = new GitHubRepo();
            ret.ID = fields[0];
            ret.Owner = fields[1];
            ret.Name = fields[2];
            ret.Language = fields[3];
            ret.Stars = int.Parse(fields[4]);
            return ret;
        }

    }
}
