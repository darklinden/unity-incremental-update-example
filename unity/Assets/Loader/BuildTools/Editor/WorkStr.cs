using System.IO;
using HybridCLR.Editor;
using UnityEditor;
using UnityEngine;


namespace BuildTool
{
    public static class WorkStr
    {
        public static string MainVersion => Resources.Load<TextAsset>("Version")?.text.Trim();
        public static string ProjectPath => Path.GetDirectoryName(Application.dataPath);
        public static string BuildTarget => EditorUserBuildSettings.activeBuildTarget.ToString();
        public static string HotUpdateDllPath => Path.Combine(SettingsUtil.HotUpdateDllsRootOutputDir, BuildTarget);

        // Assets/Game/DLL/HotUpdate
        public static string HotUpdateDestinationPath => Path.Combine(Application.dataPath, "Game", "DLL", "HotUpdate");

        public static string MetaDataDLLPath => Path.Combine(SettingsUtil.AssembliesPostIl2CppStripDir, BuildTarget);

        // Assets/Game/DLL/AOTMeta
        public static string MetaDataDestinationPath => Path.Combine(Application.dataPath, "Game", "DLL", "AOTMeta");

        public static string AOTGenericReferencesPath => Path.Combine(Application.dataPath, "HybridCLRGenerate", "AOTGenericReferences.cs");

        // BuildData For AOT Check
        public static string BuildDataPath => Path.Combine(ProjectPath, "BuildData", BuildTarget);

        public static string ServerDataPath => Path.Combine(ProjectPath, "ServerData", BuildTarget);
    }
}