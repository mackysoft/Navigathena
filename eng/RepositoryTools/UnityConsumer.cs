using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RepositoryTools;

internal static class UnityConsumer
{
    internal static void Configure (string project, string version, string configuration)
    {
        string manifestPath = Path.Combine(project, "Packages", "manifest.json");
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        JsonObject dependencies = manifest["dependencies"]!.AsObject();
        using JsonDocument settingsDocument = JsonDocument.Parse(File.ReadAllText(configuration));
        JsonElement configurationSettings = settingsDocument.RootElement;
        if (configurationSettings.TryGetProperty("packageVersions", out JsonElement packageVersions))
        {
            foreach (JsonProperty item in packageVersions.EnumerateObject())
            {
                dependencies[item.Name] = item.Value.GetString();
            }
        }
        PackageArtifacts.Require(!dependencies.Any(property => property.Key.StartsWith("com.mackysoft.navigathena", StringComparison.Ordinal)), "Navigathena must be restored from NuGet, not from UPM or repository sources.");
        Dictionary<string, string> packages = PackageVersions(project);
        PackageArtifacts.Require(packages.Values.All(value => value == version), "Unity packages.config does not match the release version.");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        if (configurationSettings.TryGetProperty("activeInputHandling", out JsonElement inputHandling))
        {
            int mode = inputHandling.GetInt32();
            PackageArtifacts.Require(mode is >= 0 and <= 2, "The Unity input handling mode must be legacy (0), Input System (1), or both (2).");
            string settingsPath = Path.Combine(project, "ProjectSettings", "ProjectSettings.asset");
            string settings = File.ReadAllText(settingsPath);
            const string inputPattern = "^  activeInputHandler: [0-2]\\r?$";
            PackageArtifacts.Require(Regex.Matches(settings, inputPattern, RegexOptions.Multiline).Count == 1, "The Unity consumer requires one active input handling setting.");
            File.WriteAllText(settingsPath, Regex.Replace(settings, inputPattern, "  activeInputHandler: " + mode, RegexOptions.Multiline));
        }
        if (configurationSettings.TryGetProperty("editorVersion", out JsonElement editorVersion))
        {
            File.WriteAllText(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), $"m_EditorVersion: {editorVersion.GetString()}\n");
        }
        string path = Path.Combine(project, "Assets", "NuGet.config");
        XDocument config = XDocument.Load(path);
        config.Root!.Element("packageSources")!.Elements("add").Single(item => (string?)item.Attribute("key") == "local").SetAttributeValue("value", "../../unity-feed");
        XElement nugetSettings = config.Root.Element("config")!;
        XElement? cache = nugetSettings.Elements("add").SingleOrDefault(item => (string?)item.Attribute("key") == "InstallFromCache");
        if (cache is null)
        {
            nugetSettings.Add(new XElement("add", new XAttribute("key", "InstallFromCache"), new XAttribute("value", "false")));
        }
        else
        {
            cache.SetAttributeValue("value", "false");
        }
        config.Save(path);
    }

    internal static void VerifyPackages (string feed, string project)
    {
        string assets = Path.GetFullPath(Path.Combine(project, "Assets"));
        foreach (KeyValuePair<string, string> item in PackageVersions(project))
        {
            string name = item.Key;
            string version = item.Value;
            string root = Path.Combine(assets, "Packages", $"{name}.{version}");
            Dictionary<string, byte[]> package = PackageArtifacts.ReadArchive(Path.Combine(feed, $"{name}.{version}.nupkg"));
            if (PackageArtifacts.BinaryPackages.Contains(name))
            {
                string relative = $"lib/netstandard2.1/{name}.dll";
                string expected = Path.Combine(root, relative);
                PackageArtifacts.Require(Directory.EnumerateFiles(assets, name + ".dll", SearchOption.AllDirectories).SequenceEqual([expected]) && File.ReadAllBytes(expected).AsSpan().SequenceEqual(package[relative]), "Missing, duplicate, or stale restored assembly: " + name);
            }
            else
            {
                string restored = Path.Combine(root, "Sources");
                Dictionary<string, byte[]> expected = package.Where(entry => entry.Key.StartsWith(PackageArtifacts.ContentPrefix, StringComparison.Ordinal)).ToDictionary(entry => entry.Key[PackageArtifacts.ContentPrefix.Length..], entry => entry.Value);
                Dictionary<string, byte[]> actual = Directory.EnumerateFiles(restored, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(restored, path).Replace('\\', '/'), File.ReadAllBytes);
                PackageArtifacts.Require(expected.Count > 0 && PackageArtifacts.SameFiles(expected, actual), "Restored sources, assets, or metadata differ from the package: " + name);
                PackageArtifacts.Require(Directory.EnumerateFiles(assets, name + ".asmdef", SearchOption.AllDirectories).SequenceEqual([Path.Combine(restored, "Runtime", name + ".asmdef")]) && !Directory.EnumerateFiles(assets, name + ".dll", SearchOption.AllDirectories).Any(), "Missing or duplicate source assembly: " + name);
            }
            Console.WriteLine($"NuGetForUnity: {name} {version} verified");
        }
    }

    internal static void VerifyResults (string directory, string project)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "Packages", "manifest.json")));
        List<string> expected = ["MackySoft.Navigathena.Unity.Tests.dll"];
        if (manifest.RootElement.GetProperty("dependencies").TryGetProperty("com.unity.inputsystem", out _))
        {
            expected.Add("MackySoft.Navigathena.Unity.InputSystem.Tests.dll");
        }
        XElement[] results = Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Descendants("test-suite"))
            .Where(element => (string?)element.Attribute("type") == "Assembly").ToArray();
        foreach (string name in expected)
        {
            XElement[] assemblies = results.Where(element => (string?)element.Attribute("name") == name).ToArray();
            PackageArtifacts.Require(assemblies.Length == 1, $"Expected one result for {name}; found {assemblies.Length}.");
            XElement assembly = assemblies[0];
            PackageArtifacts.Require(assembly.Elements("properties").Elements("property").Any(item => (string?)item.Attribute("name") == "platform" && (string?)item.Attribute("value") == "PlayMode"), "Navigathena Unity tests must execute in PlayMode.");
            int total = (int?)assembly.Attribute("total") ?? 0;
            PackageArtifacts.Require(total > 0 && (string?)assembly.Attribute("result") == "Passed" && (int?)assembly.Attribute("passed") == total, "All Navigathena Unity tests must execute and pass, without skipped tests: " + name);
            Console.WriteLine($"{name}: {total} Unity PlayMode tests passed");
        }
    }

    private static Dictionary<string, string> PackageVersions (string project)
    {
        Dictionary<string, string> packages = XDocument.Load(Path.Combine(project, "Assets", "packages.config")).Root!.Elements("package")
            .Where(item => item.Attribute("id")!.Value.StartsWith("MackySoft.Navigathena", StringComparison.Ordinal))
            .ToDictionary(item => item.Attribute("id")!.Value, item => item.Attribute("version")!.Value);
        PackageArtifacts.Require(packages.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(PackageArtifacts.PackageIds), "The Unity consumer must restore all seven Navigathena packages.");
        return packages;
    }
}
