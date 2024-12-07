using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using BuildTool;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

public static class BuildDLLAndAddrs
{
    static bool ZipAddressables(string srcPlatformPath, string targetZipPath)
    {
        var targetDir = Directory.GetParent(targetZipPath).FullName;
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        if (File.Exists(targetZipPath))
        {
            File.Delete(targetZipPath);
        }

        // Zip Addressables
        Debug.Log("Zip Addressables");
        ZipFile.CreateFromDirectory(srcPlatformPath, targetZipPath);

        return true;
    }

    private enum SizeUnits
    {
        Byte, KB, MB, GB, TB, PB, EB, ZB, YB
    }

    private static string ToSize(long value, SizeUnits unit)
    {
        return (value / (double)System.Math.Pow(1024, (long)unit)).ToString("0.00");
    }

    // https://discussions.unity.com/t/contentupdateschema-static-vs-non-static/725389/25
    // Just for context, marking a group as Static is telling the Addressables system 
    // “I never want anything in this group to change.” 
    // The Check for Content Update Restrictions workflow is only for when you’ve made some 
    // critical error and something does need to change in a Static group.
    private static bool BuildAddressables()
    {
        // Build Addressables
        Debug.Log("Building Addressables");

        // Clean old addressables
        var srcPlatformPath = Path.Combine(WorkStr.ProjectPath, "ServerData", WorkStr.BuildTarget);
        if (Directory.Exists(srcPlatformPath))
        {
            // clean old addressables
            var files = Directory.GetFiles(srcPlatformPath);
            foreach (var file in files)
            {
                if (file.ToLower().EndsWith(".bundle")
                    || file.ToLower().EndsWith(".bin")
                    || file.ToLower().EndsWith(".json")
                    || file.ToLower().EndsWith(".hash"))
                {
                    File.Delete(file);
                }
            }
        }

        // Go through all groups and Warn if any group is marked as Static
        foreach (var group in AddressableAssetSettingsDefaultObject.Settings.groups)
        {
            if (!group.HasSchema<ContentUpdateGroupSchema>())
            {
                Debug.LogWarning($"Group {group.Name} does not have ContentUpdateGroupSchema, this is not recommended for server data.");
            }
            else
            {
                var updateSchema = group.GetSchema<ContentUpdateGroupSchema>();
                if (updateSchema.StaticContent)
                {
                    Debug.LogWarning($"Group {group.Name} is marked as Static. This is not recommended for server data.");
                }
            }
        }

        // Build Addressables version using player version to keep the catalog version consistent
        AddressableAssetSettingsDefaultObject.Settings.OverridePlayerVersion = WorkStr.MainVersion;
        // TODO: If you need to use Addressables Resource in StartLoader, 
        // you need to set this to true, And Manually call Addressable.UpdateCatalogs() in StartLoader
        AddressableAssetSettingsDefaultObject.Settings.DisableCatalogUpdateOnStartup = false;
        // When building player, we don't want to build addressables
        AddressableAssetSettingsDefaultObject.Settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;

        AddressableAssetSettings.CleanPlayerContent();
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult rst);

        return string.IsNullOrEmpty(rst.Error);
    }

    private static bool OnlyBuildPlayer()
    {
        var options = new BuildPlayerOptions();
        BuildPlayerOptions playerSettings = BuildPlayerWindow.DefaultBuildMethods.GetBuildPlayerOptions(options);
        var report = BuildPipeline.BuildPlayer(playerSettings);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError("BuildPlayer Failed");
            return false;
        }
        return true;
    }

    [MenuItem("Tools/Build Main Package With Server Data")]
    public static void DebugMainPackage()
    {
        // Show Dialog
        if (!EditorUtility.DisplayDialog("Build Main Package With Server Data",
            $"Project: {WorkStr.ProjectPath}\nBuild Target: {WorkStr.BuildTarget}\nVersion: {WorkStr.MainVersion}\n\nContinue?",
            "Start", "Cancel"))
        {
            Debug.Log("Build Main Package With Server Data: User Canceled");
            return;
        }

        if (Directory.Exists(WorkStr.ServerDataPath))
            Directory.Delete(WorkStr.ServerDataPath, true);

        var success = HybridHotUpdateEditorHelper.BuildHotUpdateDlls(isBuildPlayer: true);
        if (!success)
        {
            Debug.LogError("Build Main Package With Server Data: BuildHotUpdateDlls Error");
            return;
        }

        success = BuildAddressables();
        if (!success)
        {
            Debug.LogError("Build Main Package With Server Data: BuildAddressables Error");
            return;
        }

        success = OnlyBuildPlayer();
        if (!success)
        {
            Debug.LogError("Build Main Package With Server Data: OnlyBuildPlayer Error");
            return;
        }

        // Copy Addressables to test server
        // Show Dialog if Editor has Graphics
        if (!EditorUtility.DisplayDialog("Build Main Package With Server Data",
            $"Zip Addressables For Local Testing\n\nContinue?",
            "Start", "Cancel"))
        {
            Debug.Log("Build Main Package With Server Data: Zip Addressables For Local Testing - User Canceled");
            return;
        }

        // debug version will always be the current time
        var debug_version = $"debug-{System.DateTime.Now:yyyyMMddHHmmss}";

        // write commit to version file
        var versionFilePath = Path.Combine(WorkStr.ServerDataPath, "Version.txt");
        File.WriteAllText(versionFilePath, debug_version);

        var versionPath = Path.Combine(Path.GetDirectoryName(WorkStr.ProjectPath), "host", "serve", WorkStr.MainVersion).Normalize();
        if (!Directory.Exists(versionPath))
        {
            Directory.CreateDirectory(versionPath);
        }

        var platformPath = Path.Combine(versionPath, WorkStr.BuildTarget);
        if (Directory.Exists(platformPath))
        {
            Directory.Delete(platformPath, true);
        }
        Directory.CreateDirectory(platformPath);

        var targetZipPath = Path.Combine(platformPath, $"{debug_version}-full.zip");
        ZipAddressables(WorkStr.ServerDataPath, targetZipPath);

        var zipFile = new FileInfo(targetZipPath);
        var size = zipFile.Length;
        var sizeStr = ToSize(size, SizeUnits.MB);

        var targetInfoPath = Path.Combine(platformPath, "update_info");
        File.WriteAllText(targetInfoPath, $"{{\"ver\":\"{debug_version}\",\"down\":\"{debug_version}-full.zip\",\"size\":\"{sizeStr} MB\",\"vers\":[\"a\",\"b\",\"c\"],\"downs\":[\"a\",\"b\",\"c\"],\"sizes\":[\"a\",\"b\",\"c\"]}}");
    }

    public static void ReleaseMainPackage()
    {
        if (Directory.Exists(WorkStr.ServerDataPath))
            Directory.Delete(WorkStr.ServerDataPath, true);

        var success = HybridHotUpdateEditorHelper.BuildHotUpdateDlls(isBuildPlayer: true);
        if (!success)
        {
            Debug.LogError("Build Main Package With Server Data: BuildHotUpdateDlls Error");
            return;
        }

        success = BuildAddressables();
        if (!success)
        {
            Debug.LogError("Build Main Package With Server Data: BuildAddressables Error");
            return;
        }

        success = OnlyBuildPlayer();
        if (!success)
        {
            Debug.LogError("Build Main Package With Server Data: OnlyBuildPlayer Error");
            return;
        }

        Debug.Log("ReleaseDllAndAddrs Build Success");
    }

    [MenuItem("Tools/Build Incremental Server Data")]
    public static void DebugIncrementalServerData()
    {
        // Show Dialog
        if (!EditorUtility.DisplayDialog("Build Incremental Server Data",
            $"Project: {WorkStr.ProjectPath}\nBuild Target: {WorkStr.BuildTarget}\nVersion: {WorkStr.MainVersion}\n\nContinue?",
            "Start", "Cancel"))
        {
            Debug.Log("Build Incremental Server Data: User Canceled");
            return;
        }

        var success = HybridHotUpdateEditorHelper.BuildHotUpdateDlls(isBuildPlayer: false);
        if (!success)
        {
            Debug.LogError("Build Incremental Server Data: BuildHotUpdateDlls Error");
            return;
        }

        success = BuildAddressables();
        if (!success)
        {
            Debug.LogError("Build Incremental Server Data: BuildAddressables Error");
            return;
        }

        // Copy Addressables to test server
        // Show Dialog if Editor has Graphics
        if (!EditorUtility.DisplayDialog("Build Incremental Server Data",
            $"Zip Addressables For Local Testing\n\nContinue?",
            "Start", "Cancel"))
        {
            Debug.Log("Build Incremental Server Data: Zip Addressables For Local Testing - User Canceled");
            return;
        }

        // debug version will always be the current time
        var debug_version = $"debug-{System.DateTime.Now:yyyyMMddHHmmss}";

        // write commit to version file
        var versionFilePath = Path.Combine(WorkStr.ServerDataPath, "Version.txt");
        File.WriteAllText(versionFilePath, debug_version);

        var versionPath = Path.Combine(Path.GetDirectoryName(WorkStr.ProjectPath), "host", "serve", WorkStr.MainVersion).Normalize();
        if (!Directory.Exists(versionPath))
        {
            Directory.CreateDirectory(versionPath);
        }

        var platformPath = Path.Combine(versionPath, WorkStr.BuildTarget);
        if (Directory.Exists(platformPath))
        {
            Directory.Delete(platformPath, true);
        }
        Directory.CreateDirectory(platformPath);

        var targetZipPath = Path.Combine(platformPath, $"{debug_version}-full.zip");
        ZipAddressables(WorkStr.ServerDataPath, targetZipPath);

        var zipFile = new FileInfo(targetZipPath);
        var size = zipFile.Length;
        var sizeStr = ToSize(size, SizeUnits.MB);

        var targetInfoPath = Path.Combine(platformPath, "update_info");
        File.WriteAllText(targetInfoPath, $"{{\"ver\":\"{debug_version}\",\"down\":\"{debug_version}-full.zip\",\"size\":\"{sizeStr} MB\",\"vers\":[\"a\",\"b\",\"c\"],\"downs\":[\"a\",\"b\",\"c\"],\"sizes\":[\"a\",\"b\",\"c\"]}}");

        Debug.Log("Build Incremental Server Data Success");
    }

    public static void ReleaseIncrementalServerData()
    {
        Debug.Log("ReleaseIncrementalServerData");
        Debug.Log($"BuildTarget: {WorkStr.BuildTarget}");
        Debug.Log($"MainVersion: {WorkStr.MainVersion}");

        var success = HybridHotUpdateEditorHelper.BuildHotUpdateDlls(isBuildPlayer: false);
        if (!success)
        {
            Debug.LogError("Build Incremental Server Data: BuildHotUpdateDlls Error");
            return;
        }

        success = BuildAddressables();
        if (!success)
        {
            Debug.LogError("Build Incremental Server Data: BuildAddressables Error");
            return;
        }

        Debug.Log("ReleaseIncrementalServerData Build Success");
    }
}
