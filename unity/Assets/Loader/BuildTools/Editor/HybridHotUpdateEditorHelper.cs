using System;
using System.Collections.Generic;
using System.IO;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.HotUpdate;
using HybridCLR.Editor.Installer;
using UnityEditor;
using UnityEngine;

// Original https://github.com/H2ojunjun/HybridCLR_Addressables_Demo
// Some Code Changed
namespace BuildTool
{
    public static class HybridHotUpdateEditorHelper
    {
        /// <summary>
        /// 执行一次HybridCLR的generate all，并将生成的dll拷贝到assets中
        /// </summary>
        public static bool BuildHotUpdateDlls(bool isBuildPlayer)
        {
            // 如果未安装，安装
            var controller = new InstallerController();
            if (!controller.HasInstalledHybridCLR())
            {
                controller.InstallDefaultHybridCLR();
            }

            //执行HybridCLR
            PrebuildCommand.GenerateAll();

            // 如果是更新，则检查热更代码中是否引用了被裁减的AOT代码
            if (!isBuildPlayer)
            {
                if (!CheckAccessMissingMetadata())
                {
                    return false;
                }
            }

            // 拷贝dll
            DeleteOldDll();
            CopyHotUpdateDll();
            CopyMetaDataDll();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 如果是发包，则拷贝AOT dll
            if (isBuildPlayer)
            {
                CopyAotDllsForStripCheck();
            }

            return true;
        }

        private static void DeleteOldDll()
        {
            var oldDlls = Directory.GetFiles(WorkStr.HotUpdateDestinationPath, "*.bytes", SearchOption.AllDirectories);
            foreach (var oldDll in oldDlls)
            {
                Debug.Log("Delete Old HotUpdate Dll: " + oldDll);
                File.Delete(oldDll);
            }
            oldDlls = Directory.GetFiles(WorkStr.MetaDataDestinationPath, "*.bytes", SearchOption.AllDirectories);
            foreach (var oldDll in oldDlls)
            {
                Debug.Log("Delete Old MetaData Dll: " + oldDll);
                File.Delete(oldDll);
            }
        }

        private static void CopyHotUpdateDll()
        {
            var hotUpdateAssemblyNamesExcludePreserved = SettingsUtil.HotUpdateAssemblyNamesExcludePreserved;
            foreach (var dll in hotUpdateAssemblyNamesExcludePreserved)
            {
                var srcPath = Path.Combine(WorkStr.HotUpdateDllPath, dll + ".dll");
                var dstPath = Path.Combine(WorkStr.HotUpdateDestinationPath, Path.GetFileName(dll) + ".dll.bytes");
                Debug.Log("Copy HotUpdate Dll: " + srcPath + " to " + dstPath);

                var srcBytes = File.ReadAllBytes(srcPath);
                var desBytes = Encryption.Encrypt(srcBytes);
                File.WriteAllBytes(dstPath, desBytes);
            }
            Debug.Log("Copy HotUpdate Dlls Success!");
        }

        private static void CopyMetaDataDll()
        {
            List<string> assemblies = GetMetaDataDllList();

            foreach (var dllName in assemblies)
            {
                var srcPath = Path.Combine(WorkStr.MetaDataDLLPath, dllName);
                var dstPath = Path.Combine(WorkStr.MetaDataDestinationPath, Path.GetFileName(dllName) + ".bytes");
                Debug.Log("Copy MetaData Dll: " + srcPath + " to " + dstPath);

                var srcBytes = File.ReadAllBytes(srcPath);
                var desBytes = Encryption.Encrypt(srcBytes);
                File.WriteAllBytes(dstPath, desBytes);
            }
            Debug.Log("Copy MetaData Dlls Success!");
        }

        /// <summary>
        /// 热更代码中可能会调用到AOT中已经被裁剪的函数，需要检查一下
        /// https://hybridclr.doc.code-philosophy.com/docs/basic/codestriping#%E6%A3%80%E6%9F%A5%E7%83%AD%E6%9B%B4%E6%96%B0%E4%BB%A3%E7%A0%81%E4%B8%AD%E6%98%AF%E5%90%A6%E5%BC%95%E7%94%A8%E4%BA%86%E8%A2%AB%E8%A3%81%E5%89%AA%E7%9A%84%E7%B1%BB%E5%9E%8B%E6%88%96%E5%87%BD%E6%95%B0
        /// </summary>
        private static bool CheckAccessMissingMetadata()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string aotDir = WorkStr.BuildDataPath;
            var checker = new MissingMetadataChecker(aotDir, new List<string>());

            string hotUpdateDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
            {
                string dllPath = $"{hotUpdateDir}/{dll}";
                bool notAnyMissing = checker.Check(dllPath);
                if (!notAnyMissing)
                {
                    Debug.LogError($"HotUpdate dll:{dll} is using a stripped method or type in AOT dll!Please rebuild a player!");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 如果是发包，需要拷贝Aot dll到BuildData文件夹下，为后续更新时的代码裁剪检查做准备
        /// </summary>
        private static void CopyAotDllsForStripCheck()
        {
            Directory.CreateDirectory(WorkStr.BuildDataPath);
            var oldDlls = Directory.GetFiles(WorkStr.BuildDataPath, "*.bytes", SearchOption.AllDirectories);
            foreach (var oldDll in oldDlls)
            {
                Debug.Log("Delete Old BuildData AOT Dll: " + oldDll);
                File.Delete(oldDll);
            }
            var currentDlls = Directory.GetFiles(WorkStr.MetaDataDLLPath, "*.dll", SearchOption.AllDirectories);
            foreach (var dll in currentDlls)
            {
                var dstPath = Path.Combine(WorkStr.BuildDataPath, Path.GetFileName(dll) + ".bytes");
                Debug.Log("Copy AOT Dll: " + dll + " to BuildData " + dstPath);
                File.Copy(dll, dstPath);
            }
            Debug.Log("Copy AOT Dlls For Strip Check Success!");
        }

        // 之所以采用读取C#文件的方式是因为如果直接读取代码中的列表会出现打包时更改了AOTGenericReferences.cs但Unity编译未完成导致
        // AOTGenericReferences中PatchedAOTAssemblyList还是代码修改前的数据的问题，是因为Unity还没有reload domain
        // https://docs.unity.cn/2023.2/Documentation/Manual/DomainReloading.html
        private static List<string> GetMetaDataDllList()
        {
            List<string> result = new List<string>();
            if (!File.Exists(WorkStr.AOTGenericReferencesPath))
            {
                Debug.LogError("AOTGenericReferences.cs not found!");
                return result;
            }
            var aotReferencesFileContent = File.ReadAllLines(WorkStr.AOTGenericReferencesPath);
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
                            result.Add(dllName);
                        }
                        i++;
                    }
                    break;
                }
            }
            return result;
        }
    }
}