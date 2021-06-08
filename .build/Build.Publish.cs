using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NuGet.Versioning;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.NuGet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Helpers;
using static Nuke.Common.Tools.NuGet.NuGetTasks;


partial class Build : NukeBuild
{
    HashSet<string> ProtectedVersions = new HashSet<string>
    {
        "11.3.0-rc.0",
        "11.3.0-rc.1",
        "11.3.0-rc.2",
        
        "12.0.0-preview.14",
        "12.0.0-preview.15",
        "12.0.0-preview.16",
        "12.0.0-preview.17"
    };

    HashSet<(string, string)> ErrorVersions = new HashSet<(string, string)>
    {
        // ("HotChocolate", "11.2.0"),
    };

    List<string> Keys = new List<string>
    {
        Environment.GetEnvironmentVariable("NUGET_API_KEY0"),
        Environment.GetEnvironmentVariable("NUGET_API_KEY1"),
        Environment.GetEnvironmentVariable("NUGET_API_KEY2"),
        Environment.GetEnvironmentVariable("NUGET_API_KEY3"),
        Environment.GetEnvironmentVariable("NUGET_API_KEY4"),
        Environment.GetEnvironmentVariable("NUGET_API_KEY5"),
        Environment.GetEnvironmentVariable("NUGET_API_KEY6"),
        Environment.GetEnvironmentVariable("NUGET_API_KEY7"),
        Environment.GetEnvironmentVariable("NUGET_API_KEY8"),
        Environment.GetEnvironmentVariable("NUGET_API_KEY9")
    };

    // IEnumerable<string> ChangelogSectionNotes => ExtractChangelogSectionNotes(ChangelogFile);
    [Parameter("NuGet Source for Packages")] readonly string NuGetSource = "https://api.nuget.org/v3/index.json";
    [Parameter("NuGet Api Key")] readonly string NuGetApiKey;

    Target Pack => _ => _
        .DependsOn(Restore)
        .Produces(PackageDirectory / "*.nupkg")
        .Produces(PackageDirectory / "*.snupkg")
        .Requires(() => Configuration.Equals(Configuration.Release))
        .Executes(() =>
        {
            if (!InvokedTargets.Contains(Restore))
            {
                DotNetBuildSonarSolution(AllSolutionFile);
            }

            DotNetPack(c => c
                .SetProject(AllSolutionFile)
                .SetNoBuild(InvokedTargets.Contains(Compile))
                .SetConfiguration(Configuration)
                .SetOutputDirectory(PackageDirectory)
                .SetVersion(GitVersion.SemVer));

            var projFile = File.ReadAllText(StarWarsProj);
            File.WriteAllText(StarWarsProj, projFile.Replace("11.0.0-dev.14", GitVersion.SemVer));

            projFile = File.ReadAllText(EmptyServerProj);
            File.WriteAllText(EmptyServerProj, projFile.Replace("11.0.0-dev.14", GitVersion.SemVer));

            NuGetPack(c => c
                .SetVersion(GitVersion.SemVer)
                .SetOutputDirectory(PackageDirectory)
                .SetConfiguration(Configuration)
                .CombineWith(
                    t => t.SetTargetPath(StarWarsTemplateNuSpec),
                    t => t.SetTargetPath(EmptyServerTemplateNuSpec)));

            /*
            NuGetPack(c => c
                .SetVersion(GitVersion.SemVer)
                .SetOutputDirectory(PackageDirectory)
                .SetConfiguration(Configuration)
                .CombineWith(
                    t => t.SetTargetPath(StrawberryShakeNuSpec),
                    t => t.SetTargetPath(StarWarsTemplateNuSpec),
                    t => t.SetTargetPath(EmptyServerTemplateNuSpec)));
                    */

            //.SetPackageReleaseNotes(GetNuGetReleaseNotes(ChangelogFile, GitRepository)));
        });

    Target Publish => _ => _
        .DependsOn(Clean, Test, Pack)
        .Consumes(Pack)
        .Requires(() => NuGetSource)
        .Requires(() => NuGetApiKey)
        .Requires(() => Configuration.Equals(Configuration.Release))
        .Executes(() =>
        {
            IReadOnlyCollection<AbsolutePath> packages = PackageDirectory.GlobFiles("*.nupkg");

            DotNetNuGetPush(_ => _
                    .SetSource(NuGetSource)
                    .SetApiKey(NuGetApiKey)
                    .CombineWith(packages, (_, v) => _
                        .SetTargetPath(v)),
                degreeOfParallelism: 2,
                completeOnFailure: true);
        });


    Target CleanVersions => _ => _
        .Executes(async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            var complete = true;
            var key = 0;

            File.Delete(RootDirectory / "completed.txt");

            ISet<string> completed = File.Exists(RootDirectory / "completed.txt")
                ? File.ReadLines(RootDirectory / "completed.txt").Select(t => t.Trim())
                    .Distinct()
                    .OrderBy(t => t).ToHashSet()
                : new HashSet<string>();

            ISet<string> completedVersions =
                File.Exists(RootDirectory / "completed_versions.txt")
                    ? File.ReadLines(RootDirectory / "completed_versions.txt")
                        .Select(t => t.Trim()).Distinct().OrderBy(t => t).ToHashSet()
                    : new HashSet<string>();

            do
            {
                var requestInfo = new RequestInfo();
                using var httpClient = new HttpClient();

                Logger.Info($"Current key: {key}.");

                foreach (var packagedId in File.ReadLines(RootDirectory / "packages.txt")
                    .Select(t => t.Trim()).Distinct().OrderBy(t => t))
                {
                    if (!completed.Contains(packagedId))
                    {
                        foreach(var error in ErrorVersions) 
                        {
                            if(packagedId.StartsWith(error.Item1))
                            {
                                await TryRemoveVersions(httpClient, packagedId, error.Item2,
                                    requestInfo, completedVersions, key);
                            }
                        }

                        string[] versions = await TryGetVersions(httpClient, packagedId);

                        if (!await TryRemovePreviewVersions(httpClient, packagedId, versions,
                            requestInfo, completedVersions, key))
                        {
                            complete = false;
                            break;
                        }

                        completed.Add(packagedId);
                    }
                }

                File.WriteAllText(
                    RootDirectory / "completed.txt",
                    string.Join(Environment.NewLine, completed.OrderBy(t => t)));

                File.WriteAllText(
                    RootDirectory / "completed_versions.txt",
                    string.Join(Environment.NewLine, completedVersions.OrderBy(t => t)));

                Logger.Success("Batch Completed.");

                if (!complete && key == Keys.Count - 1)
                {
                    key = 0;
                    TimeSpan waitTime = TimeSpan.FromHours(1) - stopwatch.Elapsed;
                    DateTime nextRun = DateTime.Now.Add(waitTime);

                    while (nextRun > DateTime.Now)
                    {
                        Logger.Info($"Next run in {(nextRun - DateTime.Now).Minutes} minutes.");
                        await Task.Delay(TimeSpan.FromMinutes(1));
                    }

                    stopwatch.Restart();
                }
                else
                {
                    key++;
                }
            } while (!complete);

            Logger.Success("All packages cleaned up.");
        });

    async Task<string[]> TryGetVersions(HttpClient httpClient, string packagedId)
    {
        HttpResponseMessage responseMessage =
            await httpClient.GetAsync(
                $"https://api.nuget.org/v3-flatcontainer/{packagedId.ToLower()}/index.json");

        if (responseMessage.IsSuccessStatusCode)
        {
            VersionInfo versionInfo =
                JsonConvert.DeserializeObject<VersionInfo>(
                    await responseMessage.Content.ReadAsStringAsync());

            return versionInfo.Versions
                .Where(s => s.Contains("-"))
                .Where(v => !ProtectedVersions.Contains(v))
                .OrderBy(SemanticVersion.Parse)
                .ToArray();
        }

        return null;
    }

    async Task<bool> TryRemovePreviewVersions(
        HttpClient httpClient,
        string packagedId,
        string[] versions,
        RequestInfo requestInfo,
        ISet<string> completedVersions,
        int key)
    {
        var completed = true;

        var versionInfos = versions.Select(v => (Version: v, Key: $"{packagedId}.{v}"))
            .Where(vi => !completedVersions.Contains(vi.Key)).ToArray();

        Logger.Info($"{packagedId} has {versionInfos.Length} left to remove.");

        foreach (var info in versionInfos)
        {
            if (requestInfo.Add())
            {
                var apiKey = Keys[key];
                using var response = new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"https://www.nuget.org/api/v2/package/{packagedId}/{info.Version}");
                response.Headers.Add("X-NuGet-ApiKey", apiKey);

                using HttpResponseMessage responseMessage = await httpClient.SendAsync(response);

                if (responseMessage.IsSuccessStatusCode)
                {
                    Logger.Success($"{requestInfo.Count} Unlisted {info.Key}");
                    completedVersions.Add(info.Key);
                }
                else
                {
                    Logger.Warn($"Request Failed: {responseMessage.StatusCode}");
                    completed = false;
                    break;
                }
            }
            else
            {
                Logger.Warn($"Limit used up.");
                completed = false;
                break;
            }
        }

        await File.WriteAllTextAsync(
            RootDirectory / "completed_versions.txt",
            string.Join(Environment.NewLine, completedVersions));

        return completed;
    }

    async Task<bool> TryRemoveVersions(
        HttpClient httpClient,
        string packagedId,
        string version,
        RequestInfo requestInfo,
        ISet<string> completedVersions,
        int key)
    {
        var completed = true;

        if (requestInfo.Add())
        {
            var apiKey = Keys[key];
            using var response = new HttpRequestMessage(
                HttpMethod.Delete,
                $"https://www.nuget.org/api/v2/package/{packagedId}/{version}");
            response.Headers.Add("X-NuGet-ApiKey", apiKey);

            using HttpResponseMessage responseMessage = await httpClient.SendAsync(response);

            if (responseMessage.IsSuccessStatusCode)
            {
                Logger.Success($"{requestInfo.Count} Unlisted {version}");
                completedVersions.Add(version);
            }
            else
            {
                Logger.Warn($"Request Failed: {responseMessage.StatusCode}");
                completed = false;
            }
        }
        else
        {
            Logger.Warn($"Limit used up.");
            completed = false;
        }

        await File.WriteAllTextAsync(
            RootDirectory / "completed_versions.txt",
            string.Join(Environment.NewLine, completedVersions));

        return completed;
    }

    class VersionInfo
    {
        public string[] Versions { get; set; }
    }

    class RequestInfo
    {
        public int Count { get; set; }
        public int Limit { get; set; } = 300 * 10;

        public bool Add()
        {
            if (Count < Limit)
            {
                Count++;
                return true;
            }

            return false;
        }
    }
}
