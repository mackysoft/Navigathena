using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MackySoft.Navigathena.Unity.Verification.Editor
{
    public static class WebGLVerificationBuilder
    {
        public static void Build ()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int outputIndex = Array.IndexOf(arguments, "-buildOutput");
            if (outputIndex < 0 || outputIndex + 1 >= arguments.Length)
            {
                throw new InvalidOperationException("Pass the WebGL verification output using -buildOutput.");
            }
            string output = Path.GetFullPath(arguments[outputIndex + 1]);
            Directory.CreateDirectory(output);
            const string scenePath = "Assets/Verification/WebGLVerification.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Navigation verification").AddComponent<WebGLNavigationVerification>();
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                throw new InvalidOperationException("Could not save the WebGL verification scene.");
            }
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.runInBackground = true;
            BuildReport report = BuildPipeline.BuildPlayer(new[] { scenePath }, output, BuildTarget.WebGL, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("The WebGL verification build failed: " + report.summary.result);
            }
        }
    }
}
