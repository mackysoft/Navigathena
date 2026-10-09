using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace RepositoryTools.Tests;

public sealed class UnityConsumerTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("navigathena-unity-configuration-");

    [Fact]
    public void Default_configuration_keeps_the_consumer_editor_and_external_dependencies ()
    {
        Configure("{}");

        using JsonDocument manifest = ReadManifest();
        JsonElement dependencies = manifest.RootElement.GetProperty("dependencies");
        Assert.Equal("unitask-git-package", dependencies.GetProperty("com.cysharp.unitask").GetString());
        Assert.Equal("2.9.1", dependencies.GetProperty("com.unity.addressables").GetString());
        Assert.Contains("m_EditorVersion: 6000.5.5f1", File.ReadAllText(Path.Combine(temporary.FullName, "ProjectSettings", "ProjectVersion.txt")));
    }

    [Fact]
    public void Older_editor_configuration_selects_its_editor_and_package_versions ()
    {
        Configure("{\"editorVersion\":\"2022.3.62f3\",\"packageVersions\":{\"com.unity.addressables\":\"1.21.21\",\"com.unity.ugui\":\"1.0.0\"}}");

        using JsonDocument manifest = ReadManifest();
        JsonElement dependencies = manifest.RootElement.GetProperty("dependencies");
        Assert.Equal("1.21.21", dependencies.GetProperty("com.unity.addressables").GetString());
        Assert.Equal("1.0.0", dependencies.GetProperty("com.unity.ugui").GetString());
        Assert.Equal("unitask-git-package", dependencies.GetProperty("com.cysharp.unitask").GetString());
        Assert.Contains("m_EditorVersion: 2022.3.62f3", File.ReadAllText(Path.Combine(temporary.FullName, "ProjectSettings", "ProjectVersion.txt")));
    }

    [Fact]
    public void Configuration_cannot_replace_the_NuGet_consumer_with_a_Navigathena_UPM_package ()
    {
        Assert.Throws<InvalidDataException>(() => Configure("{\"packageVersions\":{\"com.mackysoft.navigathena\":\"file:source\"}}"));

        using JsonDocument manifest = ReadManifest();
        Assert.False(manifest.RootElement.GetProperty("dependencies").TryGetProperty("com.mackysoft.navigathena", out _));
    }

    [Fact]
    public void Input_System_configuration_enables_the_backend_and_keeps_other_player_settings ()
    {
        Configure("{\"activeInputHandling\":2,\"packageVersions\":{\"com.unity.inputsystem\":\"1.17.0\"}}");

        using JsonDocument manifest = ReadManifest();
        Assert.Equal("1.17.0", manifest.RootElement.GetProperty("dependencies").GetProperty("com.unity.inputsystem").GetString());
        string settings = File.ReadAllText(Path.Combine(temporary.FullName, "ProjectSettings", "ProjectSettings.asset"));
        Assert.Contains("  activeInputHandler: 2", settings);
        Assert.Contains("  productName: Test consumer", settings);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Unsupported_input_handling_modes_fail_instead_of_generating_invalid_settings (int mode)
        => Assert.Throws<InvalidDataException>(() => Configure("{\"activeInputHandling\":" + mode + "}"));

    [Fact]
    public void Results_must_include_the_device_tests_when_Input_System_is_installed ()
    {
        Configure("{\"packageVersions\":{\"com.unity.inputsystem\":\"1.17.0\"}}");
        string results = Path.Combine(temporary.FullName, "results");
        Directory.CreateDirectory(results);
        XDocument report = new(new XElement("test-run", PassedAssembly("MackySoft.Navigathena.Unity.Tests.dll")));
        string path = Path.Combine(results, "results.xml");
        report.Save(path);

        Assert.Throws<InvalidDataException>(() => UnityConsumer.VerifyResults(results, temporary.FullName));
        report.Root!.Add(PassedAssembly("MackySoft.Navigathena.Unity.InputSystem.Tests.dll"));
        report.Save(path);
        UnityConsumer.VerifyResults(results, temporary.FullName);
        report.Root!.Elements("test-suite").Last().SetAttributeValue("result", "Failed");
        report.Save(path);
        Assert.Throws<InvalidDataException>(() => UnityConsumer.VerifyResults(results, temporary.FullName));
    }

    private static XElement PassedAssembly (string name) => new("test-suite",
        new XAttribute("type", "Assembly"), new XAttribute("name", name), new XAttribute("total", 1),
        new XAttribute("passed", 1), new XAttribute("result", "Passed"),
        new XElement("properties", new XElement("property", new XAttribute("name", "platform"), new XAttribute("value", "PlayMode"))));

    private void Configure (string configuration)
    {
        string project = temporary.FullName;
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        Directory.CreateDirectory(Path.Combine(project, "Packages"));
        Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        File.WriteAllText(Path.Combine(project, "Packages", "manifest.json"), "{\"dependencies\":{\"com.cysharp.unitask\":\"unitask-git-package\",\"com.unity.addressables\":\"2.9.1\",\"com.unity.ugui\":\"2.5.0\",\"com.github-glitchenzo.nugetforunity\":\"nugetforunity-git-package\"}}");
        File.WriteAllText(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.5.5f1\n");
        File.WriteAllText(Path.Combine(project, "ProjectSettings", "ProjectSettings.asset"), "PlayerSettings:\n  activeInputHandler: 0\n  productName: Test consumer\n");
        string[] packages =
        [
            "MackySoft.Navigathena", "MackySoft.Navigathena.Extensions.DependencyInjection", "MackySoft.Navigathena.Unity",
            "MackySoft.Navigathena.Unity.UGUI", "MackySoft.Navigathena.Unity.UIToolkit", "MackySoft.Navigathena.Unity.Addressables", "MackySoft.Navigathena.VContainer"
        ];
        new XDocument(new XElement("packages", packages.Select(name => new XElement("package", new XAttribute("id", name), new XAttribute("version", "2.0.0")))))
            .Save(Path.Combine(project, "Assets", "packages.config"));
        File.WriteAllText(Path.Combine(project, "Assets", "NuGet.config"), "<configuration><packageSources><add key=\"local\" value=\"old-feed\" /></packageSources><config /></configuration>");
        string configurationPath = Path.Combine(project, "configuration.json");
        File.WriteAllText(configurationPath, configuration);
        UnityConsumer.Configure(project, "2.0.0", configurationPath);
    }

    private JsonDocument ReadManifest () => JsonDocument.Parse(File.ReadAllText(Path.Combine(temporary.FullName, "Packages", "manifest.json")));

    public void Dispose () => temporary.Delete(true);
}
