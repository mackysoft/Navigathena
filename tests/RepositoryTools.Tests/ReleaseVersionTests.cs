using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace RepositoryTools.Tests;

public sealed class ReleaseVersionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("navigathena-version-test-");

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("2.0.0-preview.2")]
    public void Version_update_changes_release_packages_without_changing_external_dependencies (string version)
    {
        string props = Path.Combine(temporary.FullName, "Directory.Build.props");
        string packages = Path.Combine(temporary.FullName, "tests", "Unity", "Assets", "packages.config");
        Directory.CreateDirectory(Path.GetDirectoryName(packages)!);
        File.WriteAllText(props, "<Project>\n  <PropertyGroup>\n    <Version>1.0.0</Version>\n  </PropertyGroup>\n</Project>\n");
        File.WriteAllText(packages, "<packages><package id=\"MackySoft.Navigathena\" version=\"1.0.0\" /><package id=\"Microsoft.Extensions.DependencyInjection\" version=\"6.0.0\" /></packages>");

        ReleaseVersion.Update(temporary.FullName, version);

        Assert.Equal(version, XDocument.Load(props).Descendants("Version").Single().Value);
        XElement[] entries = XDocument.Load(packages).Root!.Elements("package").ToArray();
        Assert.Equal(version, entries.Single(entry => (string?)entry.Attribute("id") == "MackySoft.Navigathena").Attribute("version")!.Value);
        Assert.Equal("6.0.0", entries.Single(entry => (string?)entry.Attribute("id") == "Microsoft.Extensions.DependencyInjection").Attribute("version")!.Value);
        Assert.EndsWith("\n", File.ReadAllText(props));
    }

    [Theory]
    [InlineData("v2.0.0")]
    [InlineData("2.0.0+build")]
    [InlineData("2.0.0-preview.01")]
    [InlineData("02.0.0")]
    public void Invalid_version_is_rejected_before_any_files_are_written (string version)
    {
        Assert.Throws<InvalidDataException>(() => ReleaseVersion.Update(temporary.FullName, version));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temporary.FullName));
    }

    public void Dispose () => temporary.Delete(true);
}
