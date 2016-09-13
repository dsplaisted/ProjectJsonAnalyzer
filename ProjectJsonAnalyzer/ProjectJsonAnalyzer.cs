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

            if (ret.PropertiesDefined.Contains("runtimes"))
            {
                foreach (var runtime in json["runtimes"].Children<JProperty>())
                {
                    if (runtime.Value is JObject)
                    {
                        ret.AddInterestingValues((JObject)runtime.Value, runtime.Name);
                    }
                }
            }

            return ret;
        }

        void AddInterestingValues(JObject frameworkOrRootObject, string frameworkOrRuntime)
        {
            InterestingValues.Add(new ProjectJsonValue()
            {
                Name = string.Empty,
                Path = frameworkOrRootObject.Path,
                FrameworkOrRuntime = frameworkOrRuntime,
                Value = string.Empty
            });
            
            AddInterestingValue(frameworkOrRootObject["compile"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["content"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["resource"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["preprocess"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["publishExclude"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["shared"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["packInclude"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["exclude"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["contentBuiltIn"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["compileBuiltIn"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["resourceBuiltIn"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["excludeBuiltIn"], frameworkOrRuntime);

            
            
            AddInterestingValue(frameworkOrRootObject["scripts"]?["precompile"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["scripts"]?["postcompile"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["scripts"]?["prepublish"], frameworkOrRuntime);
            AddInterestingValue(frameworkOrRootObject["scripts"]?["postpublish"], frameworkOrRuntime);

            
            AddInterestingDictionary(frameworkOrRootObject["commands"], frameworkOrRuntime);
            AddInterestingDictionary(frameworkOrRootObject["tools"], frameworkOrRuntime);
            AddInterestingDictionary(frameworkOrRootObject["dependencies"], frameworkOrRuntime);

        }

        void AddInterestingValue(JToken token, string framework)
        {
            if (token != null)
            {
                InterestingValues.Add(new ProjectJsonValue()
                {
                    Name = GetTokenName(token),
                    Path = token.Path,
                    FrameworkOrRuntime = framework,
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
                        FrameworkOrRuntime = framework,
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
        public string FrameworkOrRuntime { get; set; }
        public string Value { get; set; }
    }
}
