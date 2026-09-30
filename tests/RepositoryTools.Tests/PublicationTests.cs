using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RepositoryTools.Tests;

public sealed class PublicationTests : IDisposable
{
    private const string Version = "2.0.0-preview.1";
    private static readonly string[] Packages =
    [
        "MackySoft.Navigathena",
        "MackySoft.Navigathena.Extensions.DependencyInjection",
        "MackySoft.Navigathena.Unity",
        "MackySoft.Navigathena.Unity.UGUI",
        "MackySoft.Navigathena.Unity.UIToolkit",
        "MackySoft.Navigathena.Unity.Addressables",
        "MackySoft.Navigathena.VContainer"
    ];
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("navigathena-publication-test-");
    private readonly string artifacts;
    private readonly string pending;

    public PublicationTests ()
    {
        artifacts = Path.Combine(temporary.FullName, "artifacts");
        pending = Path.Combine(temporary.FullName, "pending");
        Directory.CreateDirectory(artifacts);
        foreach (string name in Packages)
        {
            WritePackage(Path.Combine(artifacts, $"{name}.{Version}.nupkg"), name);
        }
    }

    [Fact]
    public async Task New_release_stages_all_packages ()
    {
        Assert.True(await Publication.PrepareAsync(artifacts, Version, pending, Fetch([])));
        Assert.Equal(Filenames(artifacts), Filenames(pending));
    }

    [Fact]
    public async Task Complete_release_does_not_republish_signed_packages ()
    {
        Assert.False(await Publication.PrepareAsync(artifacts, Version, pending, Fetch(Packages)));
        Assert.Empty(Directory.EnumerateFiles(pending));
    }

    [Fact]
    public async Task Partial_release_stages_only_missing_packages ()
    {
        Assert.True(await Publication.PrepareAsync(artifacts, Version, pending, Fetch(Packages[..3])));
        Assert.Equal(Packages[3..].Select(name => $"{name}.{Version}.nupkg").Order(), Filenames(pending));
        foreach (string path in Directory.EnumerateFiles(pending))
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(artifacts, Path.GetFileName(path))), File.ReadAllBytes(path));
        }
    }

    [Fact]
    public async Task Different_published_payload_stops_before_staging ()
    {
        static Task<bool> FetchDifferent (string name, string version, string destination)
        {
            if (name != Packages[^1])
            {
                return Task.FromResult(false);
            }
            WritePackage(destination, "different dependency manifest", signed: true);
            return Task.FromResult(true);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => Publication.PrepareAsync(artifacts, Version, pending, FetchDifferent));
        Assert.False(Directory.Exists(pending));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Transport_failure_does_not_count_as_a_missing_package (int status)
    {
        using HttpClient client = new(new HttpFailure((HttpStatusCode)status));
        await Assert.ThrowsAsync<HttpRequestException>(() => Publication.PrepareAsync(artifacts, Version, pending,
            (name, version, destination) => Publication.DownloadAsync(client, name, version, destination)));
        Assert.False(Directory.Exists(pending));
    }

    [Fact]
    public async Task Staging_directory_cannot_retain_packages_from_another_run ()
    {
        Directory.CreateDirectory(pending);
        await Assert.ThrowsAsync<InvalidDataException>(() => Publication.PrepareAsync(artifacts, Version, pending, Fetch([])));
    }

    [Fact]
    public async Task Postpublication_requires_every_package ()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Publication.VerifyAsync(artifacts, Version, Path.Combine(temporary.FullName, "published"), Fetch(Packages[..3])));
    }

    [Fact]
    public async Task Postpublication_downloads_and_compares_all_packages ()
    {
        string published = Path.Combine(temporary.FullName, "published");
        await Publication.VerifyAsync(artifacts, Version, published, Fetch(Packages));
        Assert.Equal(Filenames(artifacts), Filenames(published));
    }

    [Fact]
    public async Task Http_not_found_is_an_unpublished_package ()
    {
        string destination = Path.Combine(temporary.FullName, "download.nupkg");
        using HttpClient client = new(new HttpFailure(HttpStatusCode.NotFound));
        Assert.False(await Publication.DownloadAsync(client, Packages[0], Version, destination));
        Assert.False(File.Exists(destination));
    }

    public void Dispose () => temporary.Delete(true);

    private static Func<string, string, string, Task<bool>> Fetch (IReadOnlyCollection<string> present)
    {
        return (name, version, destination) =>
        {
            Assert.Equal(Version, version);
            if (!present.Contains(name))
            {
                return Task.FromResult(false);
            }
            WritePackage(destination, name, signed: true);
            return Task.FromResult(true);
        };
    }

    private static IEnumerable<string> Filenames (string directory) => Directory.EnumerateFiles(directory).Select(path => Path.GetFileName(path)).Order();

    private static void WritePackage (string path, string content, bool signed = false)
    {
        using ZipArchive package = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add (string name, string value)
        {
            using StreamWriter writer = new(package.CreateEntry(name).Open());
            writer.Write(value);
        }
        Add("package.nuspec", content);
        Add("lib/netstandard2.1/Library.dll", "binary");
        Add("[Content_Types].xml", signed ? "signed" : "unsigned");
        if (signed)
        {
            Add(".signature.p7s", "repository signature");
        }
    }

    private sealed class HttpFailure (HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }
}
