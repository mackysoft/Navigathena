using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RepositoryTools;

internal static class ReleaseVersion
{
    internal static void Update (string root, string version)
    {
        PackageArtifacts.Require(Regex.IsMatch(version, "\\A(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?\\z", RegexOptions.CultureInvariant), "Use a SemVer version without a v prefix or build metadata.");
        int separator = version.IndexOf('-');
        if (separator >= 0)
        {
            PackageArtifacts.Require(!version[(separator + 1)..].Split('.').Any(part => part.Length > 1 && part[0] == '0' && part.All(char.IsDigit)), "Numeric prerelease identifiers cannot have leading zeroes.");
        }
        string propsPath = Path.Combine(root, "Directory.Build.props");
        string configPath = Path.Combine(root, "tests", "Unity", "Assets", "packages.config");
        XDocument props = XDocument.Load(propsPath, LoadOptions.PreserveWhitespace);
        XDocument config = XDocument.Load(configPath, LoadOptions.PreserveWhitespace);
        props.Descendants("Version").Single().Value = version;
        foreach (XElement package in config.Root!.Elements("package").Where(package => package.Attribute("id")!.Value.StartsWith("MackySoft.Navigathena", StringComparison.Ordinal)))
        {
            package.SetAttributeValue("version", version);
        }
        Save(props, propsPath);
        Save(config, configPath);
        Console.WriteLine("Release version: " + version);
    }

    private static void Save (XDocument document, string path)
    {
        using XmlWriter writer = XmlWriter.Create(path, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = document.Declaration is null,
            NewLineChars = "\n"
        });
        document.Save(writer);
    }
}
