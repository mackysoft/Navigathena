using System;

using Microsoft.Unity.VisualStudio.Editor;
using Unity.CodeEditor;

namespace MackySoft.Navigathena.Unity.Editor.WorkspaceSynchronization
{

    /// <summary>Synchronizes the C# workspace through the currently selected Visual Studio editor installation.</summary>
    public static class VisualStudioWorkspaceSynchronization
    {
        /// <summary>Synchronizes all generated C# project files for the current Unity project.</summary>
        public static void SyncAll ()
        {
            string currentEditorPath = CodeEditor.CurrentEditorPath;
            if (string.IsNullOrWhiteSpace(currentEditorPath))
            {
                throw new InvalidOperationException("A current external code editor path is required to synchronize the C# workspace.");
            }

            if (CodeEditor.Editor.CurrentCodeEditor is not VisualStudioEditor visualStudioEditor)
            {
                throw new InvalidOperationException("The current external code editor must be provided by the Visual Studio Editor package.");
            }

            if (!visualStudioEditor.TryGetInstallationForPath(currentEditorPath, out _))
            {
                throw new InvalidOperationException($"The Visual Studio Editor package cannot provide the current external code editor path: {currentEditorPath}");
            }

            visualStudioEditor.SyncAll();
        }
    }

}
