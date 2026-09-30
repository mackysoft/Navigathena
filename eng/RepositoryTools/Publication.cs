using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace RepositoryTools;

internal static class Publication
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    internal static async Task<bool> PrepareAsync (string artifacts, string version, string pending, Func<string, string, string, Task<bool>>? fetch = null)
    {
        PackageArtifacts.Require(!Path.Exists(pending), "Use a new publication staging directory: " + pending);
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("navigathena-publication-");
        List<string> missing;
        try
        {
            missing = await InspectAsync(artifacts, version, temporary.FullName, fetch ?? DownloadAsync);
        }
        finally
        {
            temporary.Delete(true);
        }
        // Stage nothing until every already-published payload has been checked.
        Directory.CreateDirectory(pending);
        foreach (string artifact in missing)
        {
            File.Copy(artifact, Path.Combine(pending, Path.GetFileName(artifact)));
        }
        return missing.Count > 0;
    }

    internal static async Task VerifyAsync (string artifacts, string version, string destination, Func<string, string, string, Task<bool>>? fetch = null)
    {
        PackageArtifacts.Require(!Path.Exists(destination), "Use a new publication verification directory: " + destination);
        Directory.CreateDirectory(destination);
        List<string> missing = await InspectAsync(artifacts, version, destination, fetch ?? DownloadAsync);
        PackageArtifacts.Require(missing.Count == 0, "Published packages are missing: " + string.Join(", ", missing));
    }

    internal static Task<bool> DownloadAsync (string package, string version, string destination)
        => DownloadAsync(Client, package, version, destination);

    internal static async Task<bool> DownloadAsync (HttpClient client, string package, string version, string destination)
    {
        string name = package.ToLowerInvariant();
        version = version.ToLowerInvariant();
        using HttpResponseMessage response = await client.GetAsync($"https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        response.EnsureSuccessStatusCode();
        await File.WriteAllBytesAsync(destination, await response.Content.ReadAsByteArrayAsync());
        ProcessStartInfo start = new("dotnet")
        {
            UseShellExecute = false
        };
        start.ArgumentList.Add("nuget");
        start.ArgumentList.Add("verify");
        start.ArgumentList.Add(destination);
        start.ArgumentList.Add("--all");
        using Process verification = Process.Start(start) ?? throw new IOException("Could not start NuGet signature verification.");
        await verification.WaitForExitAsync();
        PackageArtifacts.Require(verification.ExitCode == 0, "NuGet signature verification failed: " + Path.GetFileName(destination));
        return true;
    }

    private static async Task<List<string>> InspectAsync (string artifacts, string version, string destination, Func<string, string, string, Task<bool>> fetch)
    {
        string[] paths = PackageArtifacts.Paths(artifacts, version);
        List<string> missing = [];
        for (int index = 0; index < paths.Length; index++)
        {
            string artifact = paths[index];
            string published = Path.Combine(destination, Path.GetFileName(artifact));
            if (await fetch(PackageArtifacts.PackageIds[index], version, published))
            {
                PackageArtifacts.RequireSamePayload(artifact, published);
            }
            else
            {
                missing.Add(artifact);
            }
        }
        return missing;
    }
}
