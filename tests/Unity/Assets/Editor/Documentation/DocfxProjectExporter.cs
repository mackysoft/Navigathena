using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace MackySoft.Navigathena.Unity.Tests.Editor.Documentation
{
    /// <summary>Exports Unity's compilation inputs as relocatable projects for source-based API documentation.</summary>
    public static class DocfxProjectExporter
    {
        private const string ExportArgument = "-exportDocfxProjects";

        [InitializeOnLoadMethod]
        private static void ScheduleCommandLineExport ()
        {
            if (!Environment.GetCommandLineArgs().Contains(ExportArgument) || SessionState.GetBool(ExportArgument, false))
            {
                return;
            }
            EditorApplication.update += ExportWhenReady;
        }

        private static void ExportWhenReady ()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }
            EditorApplication.update -= ExportWhenReady;
            try
            {
                Export();
                SessionState.SetBool(ExportArgument, true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Uses the prepared package consumer; does not require an installed IDE or checked-in Unity projects.</summary>
        public static void Export ()
        {
            string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            if (!File.Exists(Path.Combine(repository, "Documentation/docfx.json")))
            {
                throw new InvalidOperationException("Run documentation export from the repository's prepared Unity consumer.");
            }
            string output = Path.Combine(repository, "artifacts/documentation/unity");
            if (Directory.Exists(output))
            {
                Directory.Delete(output, true);
            }
            string references = Path.Combine(output, "references");
            Directory.CreateDirectory(references);

            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                .ToDictionary(assembly => assembly.name, StringComparer.Ordinal);
            var copiedReferences = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] sourcePackages = Directory.GetDirectories(Path.Combine(repository, "src"))
                .Where(directory => File.Exists(Path.Combine(directory, "Runtime", Path.GetFileName(directory) + ".asmdef")))
                .OrderBy(directory => directory, StringComparer.Ordinal)
                .ToArray();
            foreach (string package in sourcePackages)
            {
                string name = Path.GetFileName(package);
                if (!assemblies.TryGetValue(name, out Assembly assembly))
                {
                    throw new InvalidOperationException($"Unity has not compiled the documentation input: {name}");
                }
                string directory = Path.Combine(output, name);
                Directory.CreateDirectory(directory);
                var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup",
                        new XElement("TargetFramework", "netstandard2.1"),
                        new XElement("AssemblyName", name),
                        new XElement("RootNamespace", assembly.rootNamespace),
                        new XElement("LangVersion", assembly.compilerOptions.LanguageVersion),
                        new XElement("DefineConstants", string.Join(";", assembly.defines)),
                        new XElement("AllowUnsafeBlocks", assembly.compilerOptions.AllowUnsafeCode),
                        new XElement("EnableDefaultItems", false),
                        new XElement("GenerateAssemblyInfo", false),
                        new XElement("GenerateTargetFrameworkAttribute", false),
                        new XElement("DisableImplicitFrameworkReferences", true),
                        new XElement("NoStdLib", true)),
                    new XElement("ItemGroup", assembly.sourceFiles.Select(source =>
                    {
                        string marker = "/Sources/";
                        string normalized = source.Replace('\\', '/');
                        int offset = normalized.IndexOf(marker, StringComparison.Ordinal);
                        if (offset < 0)
                        {
                            throw new InvalidOperationException($"Documentation requires the restored source package: {source}");
                        }
                        string original = Path.Combine(package, normalized.Substring(offset + marker.Length));
                        if (!File.Exists(original))
                        {
                            throw new FileNotFoundException("The packaged source has no repository source.", original);
                        }
                        return new XElement("Compile", new XAttribute("Include", RelativePath(directory, original)));
                    })),
                    new XElement("ItemGroup", assembly.allReferences.OrderBy(path => path, StringComparer.Ordinal).Select(reference =>
                    {
                        string path = Path.GetFullPath(reference);
                        if (!copiedReferences.TryGetValue(path, out string copy))
                        {
                            copy = Path.Combine(references, Path.GetFileName(path));
                            if (File.Exists(copy) && !File.ReadAllBytes(copy).SequenceEqual(File.ReadAllBytes(path)))
                            {
                                throw new InvalidOperationException($"Conflicting documentation reference: {Path.GetFileName(path)}");
                            }
                            File.Copy(path, copy, true);
                            copiedReferences.Add(path, copy);
                        }
                        return new XElement("Reference", new XAttribute("Include", Path.GetFileNameWithoutExtension(path)),
                            new XElement("HintPath", RelativePath(directory, copy)));
                    })));
                new XDocument(project).Save(Path.Combine(directory, name + ".csproj"));
                Debug.Log($"Documentation project: {name}");
            }
        }

        private static string RelativePath (string directory, string path)
        {
            return Path.GetRelativePath(directory, path).Replace('\\', '/');
        }
    }
}
