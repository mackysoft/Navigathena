using System;
using System.IO;
using System.Threading.Tasks;

namespace RepositoryTools;

internal static class Program
{
    private static async Task<int> Main (string[] args)
    {
        try
        {
            switch (args)
            {
                case ["package-paths", string directory, string version]:
                    foreach (string path in PackageArtifacts.Paths(directory, version))
                    {
                        Console.WriteLine(path);
                    }
                    break;
                case ["verify-packages", string root, string directory, string version, string commit]:
                    PackageArtifacts.Verify(directory, root, version, commit);
                    break;
                case ["configure-unity", string project, string version, string configuration]:
                    UnityConsumer.Configure(project, version, configuration);
                    break;
                case ["verify-unity-packages", string feed, string project]:
                    UnityConsumer.VerifyPackages(feed, project);
                    break;
                case ["verify-unity-results", string directory]:
                    UnityConsumer.VerifyResults(directory);
                    break;
                case ["prepare-publication", string artifacts, string version, string destination]:
                    bool required = await Publication.PrepareAsync(artifacts, version, destination);
                    Console.WriteLine($"Publication required: {required}");
                    if (Environment.GetEnvironmentVariable("GITHUB_OUTPUT") is string output)
                    {
                        await File.AppendAllTextAsync(output, $"publish-required={required.ToString().ToLowerInvariant()}\n");
                    }
                    break;
                case ["verify-publication", string artifacts, string version, string destination]:
                    await Publication.VerifyAsync(artifacts, version, destination);
                    break;
                case ["set-version", string root, string version]:
                    ReleaseVersion.Update(root, version);
                    break;
                default:
                    Console.Error.WriteLine("Specify package-paths, verify-packages, configure-unity, verify-unity-packages, verify-unity-results, prepare-publication, verify-publication, or set-version with its required arguments.");
                    return 2;
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
