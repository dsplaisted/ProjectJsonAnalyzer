using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectJsonAnalyzer
{
    class ProjectJsonAnalysis
    {
        public static string[] PropertyNames = new[]
        {
            "name",
            "version",
            "summary",
            "description",
            "copyright",
            "title",
            "entryPoint",
            "projectUrl",
            "licenseUrl",
            "iconUrl",
            "compilerName",
            "testRunner",
            "authors",
            "owners",
            "tags",
            "language",
            "releaseNotes",
            "requireLicenseAcceptance",
            "embedInteropTypes",
            "compile",
            "content",
            "resource",
            "preprocess",
            "publishExclude",
            "shared",
            "namedResource",
            "packInclude",
            "exclude",
            "contentBuiltIn",
            "compileBuiltIn",
            "resourceBuiltIn",
            "excludeBuiltIn",
            "dependencies",
            "tools",
            "commands",
            "scripts",
        };

        public List<string> Frameworks { get; set; }
        public HashSet<string> PropertiesDefined { get; set; }
        public string ParsingError { get; set; }

        public ProjectJsonAnalysis()
        {
            Frameworks = new List<string>();
            PropertiesDefined = new HashSet<string>();
            ParsingError = string.Empty;
        }

        public static ProjectJsonAnalysis Analyze(string jsonContents)
        {
            ProjectJsonAnalysis ret = new ProjectJsonAnalysis();

            JObject json;

            try
            {
                json = JObject.Parse(jsonContents);
            }
            catch (JsonException ex)
            {
                ret.ParsingError = ex.Message;
                return ret;
            }
            ret.PropertiesDefined = new HashSet<string>(json.Children().Cast<JProperty>().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            if (ret.PropertiesDefined.Contains("frameworks"))
            {
                ret.Frameworks = json["frameworks"].Children().Cast<JProperty>().Select(p => p.Name).ToList();
            }

            return ret;
        }
    }
}
