#if UNITY_EDITOR
using System.IO;
using System.IO.Compression;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

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

    private static (string, string, string, string) Enticements()
    {
        var assetsDir = Path.GetFullPath(Application.dataPath);
        Debug.Log("Assets Dir: " + assetsDir);
        var projectDir = Directory.GetParent(assetsDir).FullName;
        Debug.Log("Project Dir: " + projectDir);
        var buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
        Debug.Log("Build Target: " + buildTarget);
        var versionStr = Resources.Load<TextAsset>("Version")?.text.Trim();
        Debug.Log("Version: " + versionStr);
        return (assetsDir, projectDir, buildTarget, versionStr);
    }

    private static bool BuildDLL(string projectDir, string assetsDir, string buildTarget)
    {
        // Build Dll
        Debug.Log("Building Dll");

        // disable build with player
        AddressableAssetSettingsDefaultObject.Settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;

        try
        {
            HybridCLR.Editor.Commands.PrebuildCommand.GenerateAll();
        }
        catch (System.Exception e)
        {
            Debug.LogError("BuildDLL Failed: " + e.Message);
            return false;
        }

        // Copy Dll
        // HybridCLRSettings
        var setting = HybridCLRSettings.LoadOrCreate();

        var dllSrcPath = Path.Combine(projectDir, setting.hotUpdateDllCompileOutputRootDir, buildTarget, "Game.dll");
        if (!File.Exists(dllSrcPath))
        {
            Debug.LogError("BuildDLL Failed: Dll Not Found");
            return false;
        }

        var dllDstPath = Path.Combine(assetsDir, "Game/DLL", "Game.dll.bytes");
        File.Copy(dllSrcPath, dllDstPath, true);

        AssetDatabase.Refresh();

        Debug.Log("Build Dll Success");

        return true;
    }

    private static bool BuildAddressables(string projectDir, string buildTarget, string versionStr)
    {
        // Build Addressables
        Debug.Log("Building Addressables");

        var srcPlatformPath = Path.Combine(projectDir, "ServerData", buildTarget);
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

        // Build Addressables version using player version
        AddressableAssetSettingsDefaultObject.Settings.OverridePlayerVersion = versionStr;

        AddressableAssetSettings.CleanPlayerContent();
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult rst);

        return string.IsNullOrEmpty(rst.Error);
    }

    [MenuItem("Tools/Build Dll And Addrs For Test")]
    public static void DebugDllAndAddrs()
    {
        var (assetsDir, projectDir, buildTarget, versionStr) = Enticements();

        // Show Dialog
        if (!EditorUtility.DisplayDialog("Build Dll And Addressables",
            $"Assets Dir: {assetsDir}\nProject Dir: {projectDir}\nBuild Target: {buildTarget}\nVersion: {versionStr}\n\nContinue?",
            "Start", "Cancel"))
        {
            Debug.Log("Build Dll And Addressables: User Canceled");
            return;
        }

        // Build Dll
        var success = BuildDLL(projectDir, assetsDir, buildTarget);
        if (!success)
        {
            Debug.LogError("Build Dll Failed");
            return;
        }

        // Build Addressables
        success = BuildAddressables(projectDir, buildTarget, versionStr);
        if (!success)
        {
            Debug.LogError("Build Addressables Failed");
            return;
        }

        // Copy Addressables to test server
        // Show Dialog if Editor has Graphics
        if (!EditorUtility.DisplayDialog("Build Dll And Addressables",
            $"Zip Addressables For Local Testing\n\nContinue?",
            "Start", "Cancel"))
        {
            Debug.Log("Build Dll And Addressables: Zip Addressables For Local Testing - User Canceled");
            return;
        }

        // debug version will always be the current time
        var debug_version = $"debug-{System.DateTime.Now:yyyyMMddHHmmss}";

        var srcPlatformPath = Path.Combine(projectDir, "ServerData", buildTarget);

        // write commit to version file
        var versionFilePath = Path.Combine(srcPlatformPath, "Version.txt");
        File.WriteAllText(versionFilePath, debug_version);

        var versionPath = Path.Combine(Directory.GetParent(projectDir).FullName, "host/serve/", versionStr).Normalize();
        if (!Directory.Exists(versionPath))
        {
            Directory.CreateDirectory(versionPath);
        }

        var platformPath = Path.Combine(versionPath, buildTarget);
        if (Directory.Exists(platformPath))
        {
            Directory.Delete(platformPath, true);
        }
        Directory.CreateDirectory(platformPath);

        var targetZipPath = Path.Combine(platformPath, $"{debug_version}-full.zip");
        ZipAddressables(srcPlatformPath, targetZipPath);

        var zipFile = new FileInfo(targetZipPath);
        var size = zipFile.Length;
        var sizeStr = ToSize(size, SizeUnits.MB);

        var targetInfoPath = Path.Combine(platformPath, "update_info");
        File.WriteAllText(targetInfoPath, $"{{\"ver\":\"{debug_version}\",\"down\":\"{debug_version}-full.zip\",\"size\":\"{sizeStr} MB\",\"vers\":[\"a\",\"b\",\"c\"],\"downs\":[\"a\",\"b\",\"c\"],\"sizes\":[\"a\",\"b\",\"c\"]}}");
    }

    public static void ReleaseDllAndAddrs()
    {
        var (assetsDir, projectDir, buildTarget, versionStr) = Enticements();

        var success = BuildDLL(projectDir, assetsDir, buildTarget);
        if (!success)
        {
            return;
        }

        // Build Addressables
        success = BuildAddressables(projectDir, buildTarget, versionStr);

        if (!success)
        {
            return;
        }
        if (success)
        {
            Debug.Log("ReleaseDllAndAddrs Build Success");
        }
        else
        {
            Debug.LogError("ReleaseDllAndAddrs Build Failed");
        }
    }
}

#endif