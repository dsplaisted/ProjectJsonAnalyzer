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
            "code",
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

        public string ParsingError { get; set; }
        public List<string> Frameworks { get; set; }
        public HashSet<string> PropertiesDefined { get; set; }
        public int TopLevelDependencies { get; set; }
        public int FrameworkSpecificDependencies { get; set; }
        public List<ProjectJsonValue> InterestingValues { get; set; }
        

        public ProjectJsonAnalysis()
        {
            Frameworks = new List<string>();
            PropertiesDefined = new HashSet<string>();
            ParsingError = string.Empty;
            InterestingValues = new List<ProjectJsonValue>();
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

            ret.AddInterestingValues(json, string.Empty);

            if (json["dependencies"] != null)
            {
                ret.TopLevelDependencies = json["dependencies"].Children().Count();
            }

            if (ret.PropertiesDefined.Contains("frameworks"))
            {
                ret.Frameworks = json["frameworks"].Children().Cast<JProperty>().Select(p => p.Name).ToList();

                foreach (var framework in json["frameworks"].Children<JProperty>())
                {

                    if (framework.Value is JObject)
                    {
                        ret.AddInterestingValues((JObject)framework.Value, framework.Name);
                    }
                    else
                    {

                    }
                    if (framework.Value["dependencies"] != null)
                    {
                        ret.FrameworkSpecificDependencies += framework.Value["dependencies"].Children().Count();
                    }
                }
            }

            return ret;
        }

        void AddInterestingValues(JObject frameworkOrRootObject, string framework)
        {
            AddInterestingValue(frameworkOrRootObject["compile"], framework);
            AddInterestingValue(frameworkOrRootObject["content"], framework);
            AddInterestingValue(frameworkOrRootObject["resource"], framework);
            AddInterestingValue(frameworkOrRootObject["preprocess"], framework);
            AddInterestingValue(frameworkOrRootObject["publishExclude"], framework);
            AddInterestingValue(frameworkOrRootObject["shared"], framework);
            AddInterestingValue(frameworkOrRootObject["packInclude"], framework);
            AddInterestingValue(frameworkOrRootObject["exclude"], framework);
            AddInterestingValue(frameworkOrRootObject["contentBuiltIn"], framework);
            AddInterestingValue(frameworkOrRootObject["compileBuiltIn"], framework);
            AddInterestingValue(frameworkOrRootObject["resourceBuiltIn"], framework);
            AddInterestingValue(frameworkOrRootObject["excludeBuiltIn"], framework);

            
            
            AddInterestingValue(frameworkOrRootObject["scripts"]?["precompile"], framework);
            AddInterestingValue(frameworkOrRootObject["scripts"]?["postcompile"], framework);
            AddInterestingValue(frameworkOrRootObject["scripts"]?["prepublish"], framework);
            AddInterestingValue(frameworkOrRootObject["scripts"]?["postpublish"], framework);

            
            AddInterestingDictionary(frameworkOrRootObject["commands"], framework);
            AddInterestingDictionary(frameworkOrRootObject["tools"], framework);
            AddInterestingDictionary(frameworkOrRootObject["dependencies"], framework);

        }

        void AddInterestingValue(JToken token, string framework)
        {
            if (token != null)
            {
                InterestingValues.Add(new ProjectJsonValue()
                {
                    Name = GetTokenName(token),
                    Path = token.Path,
                    Framework = framework,
                    Value = token.ToString(Formatting.None)
                });
            }
        }

        void AddInterestingDictionary(JToken token, string framework)
        {
            JObject jobject = token as JObject;
            if (jobject != null)
            {
                foreach (var child in jobject.Children<JProperty>())
                {
                    InterestingValues.Add(new ProjectJsonValue()
                    {
                        Name = GetTokenName(token),
                        Path = token.Path,
                        Framework = framework,
                        Value = child.ToString(Formatting.None)
                    });
                }
            }
            else if (jobject != null)
            {

            }
        }

        string GetTokenName(JToken token)
        {
            if (token.Parent is JProperty)
            {
                return ((JProperty)token.Parent).Name;
            }
            else
            {
                return "<unknown>";
            }
        }
    }

    public class ProjectJsonValue
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Framework { get; set; }
        public string Value { get; set; }
    }
}
