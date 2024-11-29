
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


