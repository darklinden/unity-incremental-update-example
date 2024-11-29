#!/usr/bin/env bash

# Remove the old resources and create new ones
rm -rf ./unity/Assets/Game/Res
mkdir -p ./unity/Assets/Game/Res
for i in {1..10}; do
    echo "patch 0 Hello, $i! " >./unity/Assets/Game/Res/$i.txt
done

if [ ! -f ./unity/Assets/Game/DLL/Game.dll.bytes ]; then
    touch ./unity/Assets/Game/DLL/Game.dll.bytes
fi

echo '
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class Game : MonoBehaviour
{
    static readonly string[] ResAssets = new string[]
    {
        "Assets/Game/Res/1.txt",
        "Assets/Game/Res/2.txt",
        "Assets/Game/Res/3.txt",
        "Assets/Game/Res/4.txt",
        "Assets/Game/Res/5.txt",
        "Assets/Game/Res/6.txt",
        "Assets/Game/Res/7.txt",
        "Assets/Game/Res/8.txt",
        "Assets/Game/Res/9.txt",
        "Assets/Game/Res/10.txt",
    };

    private void Start()
    {
        Debug.Log("Game started patch 0 !");
        AsyncLoadRes().Forget();
    }

    private async UniTask AsyncLoadRes()
    {
        foreach (var asset in ResAssets)
        {
            Debug.Log("Loading " + asset);
            var textAsset = await Addressables.LoadAssetAsync<TextAsset>(asset);
            Debug.Log(textAsset.text);
        }
    }
}

' >./unity/Assets/Game/Scripts/Game.cs
