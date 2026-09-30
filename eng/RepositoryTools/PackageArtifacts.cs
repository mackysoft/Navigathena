using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RepositoryTools;

internal static class PackageArtifacts
{
    internal const string ContentPrefix = "contentFiles/any/any/";
    internal static readonly string[] BinaryPackages = ["MackySoft.Navigathena", "MackySoft.Navigathena.Extensions.DependencyInjection"];
    internal static readonly Dictionary<string, string> SourcePackages = new(StringComparer.Ordinal)
    {
        ["MackySoft.Navigathena.Unity"] = "MackySoft.Navigathena",
        ["MackySoft.Navigathena.Unity.UGUI"] = "MackySoft.Navigathena.Unity",
        ["MackySoft.Navigathena.Unity.UIToolkit"] = "MackySoft.Navigathena.Unity",
        ["MackySoft.Navigathena.Unity.Addressables"] = "MackySoft.Navigathena.Unity",
        ["MackySoft.Navigathena.VContainer"] = "MackySoft.Navigathena"
    };
    internal static readonly string[] PackageIds = [.. BinaryPackages, .. SourcePackages.Keys];

    internal static string[] Paths (string directory, string version)
    {
        string[] expected = PackageIds.Select(name => $"{name}.{version}.nupkg").ToArray();
        HashSet<string> actual = Directory.EnumerateFiles(directory, "*.nupkg").Select(path => Path.GetFileName(path)).ToHashSet(StringComparer.Ordinal);
        Require(actual.SetEquals(expected), "Expected exactly the seven release packages.");
        return expected.Select(name => Path.GetFullPath(Path.Combine(directory, name))).ToArray();
    }

    internal static void Verify (string directory, string repository, string version, string commit)
    {
        HashSet<string> guids = new(StringComparer.Ordinal);
        foreach (string path in Paths(directory, version))
        {
            Dictionary<string, byte[]> package = ReadArchive(path);
            string manifest = package.Keys.Single(name => name.EndsWith(".nuspec", StringComparison.Ordinal));
            using MemoryStream manifestStream = new(package[manifest], writable: false);
            XElement metadata = XDocument.Load(manifestStream).Root!.Elements().Single(element => element.Name.LocalName == "metadata");
            XNamespace ns = metadata.Name.Namespace;
            string name = metadata.Element(ns + "id")!.Value;
            Require(PackageIds.Contains(name) && Path.GetFileName(path) == $"{name}.{version}.nupkg", "Unexpected package identity: " + path);
            Require(metadata.Element(ns + "version")?.Value == version && metadata.Element(ns + "license")?.Value == "MIT" && metadata.Element(ns + "readme")?.Value == "README.md", "Incorrect package version, license, or readme: " + name);
            XElement? source = metadata.Element(ns + "repository");
            Require((string?)source?.Attribute("url") == "https://github.com/mackysoft/Navigathena" && (string?)source?.Attribute("type") == "git" && (string?)source?.Attribute("commit") == commit, "Incorrect repository metadata: " + name);

            Dictionary<string, string> dependencies = metadata.Descendants(ns + "dependency").ToDictionary(item => item.Attribute("id")!.Value, item => item.Attribute("version")!.Value);
            Dictionary<string, string> expectedDependencies = new();
            if (SourcePackages.TryGetValue(name, out string? dependency))
            {
                expectedDependencies.Add(dependency, version);
            }
            else if (name == "MackySoft.Navigathena.Extensions.DependencyInjection")
            {
                XDocument versions = XDocument.Load(Path.Combine(repository, "Directory.Packages.props"));
                expectedDependencies.Add("MackySoft.Navigathena", version);
                expectedDependencies.Add("Microsoft.Extensions.DependencyInjection", versions.Descendants("PackageVersion").Single(item => (string?)item.Attribute("Include") == "Microsoft.Extensions.DependencyInjection").Attribute("Version")!.Value);
            }
            Require(dependencies.Count == expectedDependencies.Count && expectedDependencies.All(item => dependencies.TryGetValue(item.Key, out string? value) && value == item.Value), "Incorrect package dependencies: " + name);

            HashSet<string> libraries = package.Keys.Where(entry => entry.StartsWith("lib/", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
            if (SourcePackages.ContainsKey(name))
            {
                Require(libraries.SetEquals(["lib/netstandard2.1/_._"]) && package["lib/netstandard2.1/_._"].Length == 0, "Source packages must not embed assemblies: " + name);
                Dictionary<string, byte[]> assets = SourceAssets(repository, name);
                Require(SameFiles(assets, package.Where(entry => entry.Key.StartsWith("contentFiles/", StringComparison.Ordinal)).ToDictionary()), "Packaged sources or assets differ from the release source: " + name);
                XElement[] rules = metadata.Elements(ns + "contentFiles").Elements(ns + "files").ToArray();
                Require(rules.Select(rule => (string?)rule.Attribute("include")).ToHashSet().SetEquals(assets.Keys.Select(entry => entry["contentFiles/".Length..])) && rules.All(rule => (string?)rule.Attribute("buildAction") == "None"), "Incorrect Unity contentFiles declarations: " + name);
                foreach (KeyValuePair<string, byte[]> asset in assets.Where(item => item.Key.EndsWith(".meta", StringComparison.Ordinal)))
                {
                    Match match = Regex.Match(Encoding.UTF8.GetString(asset.Value), "^guid: ([0-9a-f]{32})\\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
                    Require(match.Success && guids.Add(match.Groups[1].Value), "Missing or duplicate Unity asset GUID: " + asset.Key);
                }
            }
            else
            {
                Require(libraries.SetEquals([$"lib/netstandard2.1/{name}.dll", $"lib/netstandard2.1/{name}.xml"]) && !package.Keys.Any(entry => entry.StartsWith("contentFiles/", StringComparison.Ordinal)), "Unexpected binary package contents: " + name);
            }

            HashSet<string> required = [$"{name}.nuspec", "README.md", "LICENSE"];
            Require(required.IsSubsetOf(package.Keys) && Encoding.UTF8.GetString(package["LICENSE"]).Contains("MIT License", StringComparison.Ordinal), "Missing package readme, manifest, or license: " + name);
            required.UnionWith(libraries);
            required.UnionWith(package.Keys.Where(entry => entry.StartsWith(ContentPrefix, StringComparison.Ordinal)));
            Require(Payload(package).Keys.All(required.Contains), "Unexpected package payload: " + name);
            Console.WriteLine($"package: {name} {version}");
        }
    }

    internal static Dictionary<string, byte[]> ReadArchive (string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using Stream source = entry.Open();
            using MemoryStream data = new();
            source.CopyTo(data);
            Require(entries.TryAdd(entry.FullName, data.ToArray()), "Duplicate archive entry: " + entry.FullName);
        }
        return entries;
    }

    internal static void RequireSamePayload (string expected, string actual)
    {
        Require(SameFiles(Payload(ReadArchive(expected)), Payload(ReadArchive(actual))), "Published package differs from the verified artifact: " + Path.GetFileName(expected));
    }

    internal static bool SameFiles (Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
        => expected.Count == actual.Count && expected.All(item => actual.TryGetValue(item.Key, out byte[]? value) && item.Value.AsSpan().SequenceEqual(value));

    internal static void Require (bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private static Dictionary<string, byte[]> SourceAssets (string repository, string name)
    {
        string root = Path.Combine(repository, "src", name);
        string[] extensions = [".cs", ".asmdef", ".uss", ".meta"];
        Dictionary<string, byte[]> assets = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "Runtime"), "*", SearchOption.AllDirectories).Prepend(Path.Combine(root, "Runtime.meta")))
        {
            Require(!File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) && extensions.Contains(Path.GetExtension(path)), "Unexpected source package asset: " + path);
            Require(Path.GetExtension(path) == ".meta" || File.Exists(path + ".meta"), "Unity asset metadata missing: " + path);
            assets.Add(ContentPrefix + Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllBytes(path));
        }
        return assets;
    }

    private static Dictionary<string, byte[]> Payload (Dictionary<string, byte[]> package)
    {
        // Repository signatures and ZIP container metadata can change after publication.
        return package.Where(entry => entry.Key is not ".signature.p7s" and not "[Content_Types].xml"
            && !entry.Key.StartsWith("_rels/", StringComparison.Ordinal)
            && !entry.Key.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)
            && !entry.Key.EndsWith('/')).ToDictionary();
    }
}
