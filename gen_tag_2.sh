#!/usr/bin/env bash

# Remove the old resources and create new ones
rm -rf ./unity/Assets/Game/Res
mkdir -p ./unity/Assets/Game/Res
for i in {15..30}; do
    echo "patch 2 Hello, $i! " >./unity/Assets/Game/Res/$i.txt
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
        "Assets/Game/Res/11.txt",
        "Assets/Game/Res/12.txt",
        "Assets/Game/Res/13.txt",
        "Assets/Game/Res/14.txt",
        "Assets/Game/Res/15.txt",
        "Assets/Game/Res/16.txt",
        "Assets/Game/Res/17.txt",
        "Assets/Game/Res/18.txt",
        "Assets/Game/Res/19.txt",
        "Assets/Game/Res/20.txt",
        "Assets/Game/Res/21.txt",
        "Assets/Game/Res/22.txt",
        "Assets/Game/Res/23.txt",
        "Assets/Game/Res/24.txt",
        "Assets/Game/Res/25.txt",
        "Assets/Game/Res/26.txt",
        "Assets/Game/Res/27.txt",
        "Assets/Game/Res/28.txt",
        "Assets/Game/Res/29.txt",
        "Assets/Game/Res/30.txt",
    };

    private void Start()
    {
        Debug.Log("Game started patch 2 !");
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
