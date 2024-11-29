#if UNITY_EDITOR
using System.IO;
using System.IO.Compression;
using HybridCLR.Editor;
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
            HybridCLR.Editor.Commands.CompileDllCommand.CompileDll(EditorUserBuildSettings.activeBuildTarget);
            HybridCLR.Editor.Commands.Il2CppDefGeneratorCommand.GenerateIl2CppDef();

            // 这几个生成依赖HotUpdateDlls
            HybridCLR.Editor.Commands.LinkGeneratorCommand.GenerateLinkXml(EditorUserBuildSettings.activeBuildTarget);

            // 生成裁剪后的aot dll
            HybridCLR.Editor.Commands.StripAOTDllCommand.GenerateStripedAOTDlls(EditorUserBuildSettings.activeBuildTarget);

            // 桥接函数生成依赖于AOT dll，必须保证已经build过，生成AOT dll
            HybridCLR.Editor.Commands.MethodBridgeGeneratorCommand.GenerateMethodBridgeAndReversePInvokeWrapper(EditorUserBuildSettings.activeBuildTarget);
            HybridCLR.Editor.Commands.AOTReferenceGeneratorCommand.GenerateAOTGenericReference(EditorUserBuildSettings.activeBuildTarget);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Build Dll Failed: " + e.Message);
            return false;
        }

        // Copy AOT Meta Dlls
        var oldDlls = Directory.GetFiles(Path.Combine(assetsDir, "Game/DLL"), "*.bytes", SearchOption.AllDirectories);
        foreach (var oldDll in oldDlls)
        {
            File.Delete(oldDll);
        }

        // 获取AOTGenericReferences.cs文件的路径
        string aotReferencesFilePath = Path.Combine(
            Application.dataPath,
            SettingsUtil.HybridCLRSettings.outputAOTGenericReferenceFile
        );

        if (!File.Exists(aotReferencesFilePath))
        {
            Debug.LogError("AOTGenericReferences.cs file does not exist! Abort the build!");
            return false;
        }

        // 读取AOTGenericReferences.cs文件内容
        string[] aotReferencesFileContent = File.ReadAllLines(aotReferencesFilePath);

        // 查找PatchedAOTAssemblyList列表
        for (int i = 0; i < aotReferencesFileContent.Length; i++)
        {
            if (aotReferencesFileContent[i].Contains("PatchedAOTAssemblyList"))
            {
                while (!aotReferencesFileContent[i].Contains("};"))
                {
                    if (aotReferencesFileContent[i].Contains("\""))
                    {
                        int startIndex = aotReferencesFileContent[i].IndexOf("\"") + 1;
                        int endIndex = aotReferencesFileContent[i].LastIndexOf("\"");
                        string dllName = aotReferencesFileContent[i].Substring(
                            startIndex,
                            endIndex - startIndex
                        );

                        var stripedDll = Path.Combine(SettingsUtil.AssembliesPostIl2CppStripDir, buildTarget, dllName);
                        var dstPath = Path.Combine(assetsDir, "Game/DLL/AOTMeta", Path.GetFileName(dllName) + ".bytes");
                        Debug.Log("Copy Striped Dll: " + stripedDll + " to " + dstPath);
                        File.Copy(stripedDll, dstPath, true);
                    }
                    i++;
                }
                break;
            }
        }

        // Copy HotUpdate Dll
        var hotUpdateDllFolder = Path.Combine(SettingsUtil.HotUpdateDllsRootOutputDir, buildTarget);
        foreach (var dll in SettingsUtil.HotUpdateAssemblyNamesExcludePreserved)
        {
            var srcPath = Path.Combine(hotUpdateDllFolder, dll + ".dll");
            var dstPath = Path.Combine(assetsDir, "Game/DLL/HotUpdate", Path.GetFileName(dll) + ".dll.bytes");
            Debug.Log("Copy HotUpdate Dll: " + srcPath + " to " + dstPath);
            File.Copy(srcPath, dstPath, true);
        }

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