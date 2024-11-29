using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Networking;
using System.IO;
using System;


public class StartLoader : MonoBehaviour
{
	public static bool IsInitialized { get; private set; } = false;

	const string CHECK_UPDATE_URL = "https://127-0-0-1.traefik.me/";

	// Game
	const string SCENE_GAME_DLL = "Assets/Game/DLL/Game.dll.bytes";
	const string SCENE_GAME = "Assets/Game/Scenes/Game.unity";

	public AnimProgress AnimProgress;
	public UpdateAlert Alert;

	void Start()
	{
		DontDestroyOnLoad(gameObject);
		AsyncStart().Forget();
	}

	private async UniTask AsyncStart()
	{
		UpdateStatus updateStatus;
		do
		{
			updateStatus = await AsyncUpdate();
			if (updateStatus == UpdateStatus.Failed)
			{
				var result = await Alert.AsyncShow("Update Failed, Retry?", "Retry", "Exit");
				if (result == UpdateAlert.Result.Cancel)
				{
					Application.Quit();
					return;
				}
			}
		}
		while (updateStatus == UpdateStatus.Failed);

		await AsyncInitializeAndEnter();
	}

	async UniTask AsyncReleaseAllAddressables()
	{
		// Force-releasing all operation handles is a workaround, because if any handle is still acquired,
		// Unity refuses to replace these assets with a content update and spits our errors.
		// Since it's highly unlikely that we will have a 100% leak free game at all times (and we actually
		// never had during my tests), most content updates would fail. This hack fixes a ton of content update issues!
		// Workaround for problems:
		// https://discussions.unity.com/t/843869

		var handles = new List<AsyncOperationHandle>();

		var resourceManagerType = Addressables.ResourceManager.GetType();
		var dictionaryMember = resourceManagerType.GetField("m_AssetOperationCache", BindingFlags.NonPublic | BindingFlags.Instance);
		var dictionary = dictionaryMember.GetValue(Addressables.ResourceManager) as IDictionary;

		foreach (var asyncOperationInterface in dictionary.Values)
		{
			if (asyncOperationInterface == null)
				continue;

			var handle = typeof(AsyncOperationHandle).InvokeMember(nameof(AsyncOperationHandle),
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.CreateInstance,
				null, null, new object[] { asyncOperationInterface });

			handles.Add((AsyncOperationHandle)handle);
		}

		// Release all handles
		do
		{
			for (int i = handles.Count - 1; i >= 0; i--)
			{
				var handle = handles[i];
				if (!handle.IsValid())
				{
					handles.RemoveAt(i);
				}
				else
				{
					if (!handle.IsDone)
					{
						Debug.LogWarning($"StartLoader ReleaseAsyncOperationHandles AsyncOperationHandle not completed yet. Releasing anyway!");
					}

					if (handle.IsValid())
					{
						Addressables.ResourceManager.Release(handle);
					}
				}
			}

			await UniTask.Yield();
		}
		while (handles.Count > 0);
	}

	[Serializable]
	public class UpdateInfo
	{
		// latest version
		public string ver;
		// latest version full package download name
		public string down;
		public string size;

		// incremental package versions
		public List<string> vers;
		// incremental package download name match with versions
		public List<string> downs;
		// incremental package sizes match with versions
		public List<string> sizes;
	}

	string Platform
	{
		get
		{
			var platform =
#if UNITY_STANDALONE_WIN
					"StandaloneWindows64";
#elif UNITY_STANDALONE_OSX
            		"StandaloneOSX";
#elif UNITY_IOS
					"iOS";
#elif UNITY_ANDROID
					"Android";
#elif UNITY_WEBGL
            		"WebGL";
#else
					"Unknown";
#endif
			return platform;
		}
	}

	async UniTask<UpdateInfo> CheckUpdate()
	{
		try
		{
			AnimProgress.SetProgressType("Checking Update");
			AnimProgress.ToProgress(0f);
			AnimProgress.AnimToProgress(0.99f, 5f);

			var versionStr = Resources.Load<TextAsset>("Version")?.text.Trim();
			var url = $"{CHECK_UPDATE_URL}/{versionStr}/{Platform}/update_info";
			Debug.Log("CheckUpdate: " + url);
			var request = UnityWebRequest.Post(url, "");
			await request.SendWebRequest();
			if (request.result != UnityWebRequest.Result.Success)
			{
				Debug.LogError("CheckUpdate Error: " + request.error);
				return null;
			}

			var json = request.downloadHandler.text;
			Debug.Log("CheckUpdate: " + json);

			var updateInfo = JsonUtility.FromJson<UpdateInfo>(json);
			AnimProgress.AnimToProgress(1f, 0.1f);
			await UniTask.Delay(100);
			return updateInfo;
		}
		catch (Exception e)
		{
			Debug.LogError("CheckUpdate Error: " + e);
			return null;
		}
	}

	async UniTask<bool> DownloadUpdate(string url, string savePath)
	{
		try
		{
			AnimProgress.SetProgressType("Downloading Update");
			AnimProgress.ToProgress(0f);

			Debug.Log("DownloadUpdate Start Download " + url + " To " + savePath);
			AnimProgress.SetProgressType("Downloading Update");
			var request = UnityWebRequest.Get(url);
			request.downloadHandler = new DownloadHandlerFile(savePath);
			var handler = request.SendWebRequest();

			int progress = 0;
			while (!handler.isDone)
			{
				if (progress != (int)(request.downloadProgress * 100))
				{
					progress = (int)(request.downloadProgress * 100);
					AnimProgress.ToProgress(request.downloadProgress);
				}
				await UniTask.Yield();
			}

			Debug.Log("DownloadUpdate Download Done");

			await UniTask.Yield();
			if (request.result != UnityWebRequest.Result.Success)
			{
				Debug.LogError("DownloadUpdate Download Error: " + request.error);
				return false;
			}
		}
		catch (Exception e)
		{
			Debug.LogError("DownloadUpdate Error: " + e);
			return false;
		}

		return true;
	}

	public async UniTask ExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName, Action<float> progressChanged, int awaitCount = 20)
	{
		using (var archive = System.IO.Compression.ZipFile.OpenRead(sourceArchiveFileName))
		{
			double totalBytes = 0;
			foreach (var entry in archive.Entries)
			{
				totalBytes += entry.Length;
			}
			long currentBytes = 0;
			int awaitCounter = 0;

			foreach (var entry in archive.Entries)
			{
				string fileName = Path.Combine(destinationDirectoryName, entry.FullName);
				Directory.CreateDirectory(Path.GetDirectoryName(fileName));
				using (Stream inputStream = entry.Open())
				using (Stream outputStream = File.OpenWrite(fileName))
				{
					inputStream.CopyTo(outputStream);
				}

				File.SetLastWriteTime(fileName, entry.LastWriteTime.LocalDateTime);
				currentBytes += entry.Length;

				progressChanged?.Invoke((float)(currentBytes / totalBytes));

				awaitCounter++;
				if (awaitCounter >= awaitCount)
				{
					await UniTask.Yield();
					awaitCounter = 0;
				}
			}
		}
	}

	enum UpdateStatus
	{
		Unknown,
		Success,
		Failed,
		NotNeeded,
	}

	async UniTask<UpdateStatus> AsyncUpdate()
	{
		var updateInfo = await CheckUpdate();
		if (updateInfo == null)
		{
			Debug.LogError("CheckUpdate Failed");
			return UpdateStatus.Failed;
		}

		var localVersionPath = $"{Application.persistentDataPath}/AddrsCache/Version.txt";
		var localVersion = File.Exists(localVersionPath) ? File.ReadAllText(localVersionPath) : "";
		if (localVersion == updateInfo.ver)
		{
			Debug.Log("No Need Update");
			return UpdateStatus.NotNeeded;
		}

		var index = updateInfo.vers.IndexOf(localVersion);
		string updateFileName;
		string updateSize;
		if (index >= 0 && index < updateInfo.vers.Count)
		{
			updateFileName = updateInfo.downs[index];
			updateSize = updateInfo.sizes[index];
		}
		else
		{
			updateFileName = updateInfo.down;
			updateSize = updateInfo.size;
		}

		var versionStr = Resources.Load<TextAsset>("Version")?.text.Trim();
		var result = await Alert.AsyncShow($"New Version {versionStr}-{updateInfo.ver} Available, Size To Download {updateSize}", "Update", "Exit");
		if (result == UpdateAlert.Result.Cancel)
		{
			Application.Quit();
			return UpdateStatus.Failed;
		}

		var updateUrl = $"{CHECK_UPDATE_URL}/{versionStr}/{Platform}/{updateFileName}";
		var updateFileDownloadPath = $"{Application.persistentDataPath}/{updateFileName}";
		if (File.Exists(updateFileDownloadPath))
		{
			File.Delete(updateFileDownloadPath);
		}
		var downloadSuccess = await DownloadUpdate(updateUrl, updateFileDownloadPath);
		if (!downloadSuccess)
		{
			Debug.LogError("DownloadUpdate Failed");
			return UpdateStatus.Failed;
		}

		// Extract Zip
		AnimProgress.SetProgressType("Extracting Update");
		AnimProgress.ToProgress(0f);
		await UniTask.Yield();
		await ExtractToDirectory(updateFileDownloadPath, Path.Join(Application.persistentDataPath, "AddrsCache"), (progress) =>
		   {
			   AnimProgress.ToProgress(progress);
		   });
		Debug.Log("Extract Done");

		return UpdateStatus.Success;
	}

	private async UniTask AsyncInitializeAndEnter()
	{
		Debug.Log("Loader.AsyncInitialize Start");

		AnimProgress.SetProgressType("Enter Game");
		AnimProgress.ToProgress(0f);
		AnimProgress.AnimToProgress(0.99f, 5f);

		if (!IsInitialized)
		{
			// use persistentDataPath to store the cache
			CatalogUrl.StringUrl = $"{Application.persistentDataPath}/AddrsCache";
			Debug.Log("Initialize Addressables By Path: " + CatalogUrl.StringUrl);
			await Addressables.InitializeAsync(true);
			IsInitialized = true;
		}
		else
		{
			Debug.Log("Addressables Already Initialized, Release All Addressables");
			await AsyncReleaseAllAddressables();
		}

		// load code

		// Editor环境下，HotUpdate.dll.bytes已经被自动加载，不需要加载，重复加载反而会出问题。
#if !UNITY_EDITOR
		// 加载HotUpdate程序集
		var dllBytes = await Addressables.LoadAssetAsync<TextAsset>(SCENE_GAME_DLL);
		Assembly hotUpdateAss = Assembly.Load(dllBytes.bytes);
#else
		// Editor下无需加载，直接查找获得 Game 程序集
		// Assembly hotUpdateAss = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Game");
#endif

		Debug.Log("Loader.AsyncInitialize Done");

		await Addressables.LoadSceneAsync(SCENE_GAME, UnityEngine.SceneManagement.LoadSceneMode.Single, true);

		AnimProgress.AnimToProgress(1f, 0.1f);

		await UniTask.Delay(500);

		Debug.Log("Loader.AsyncInitialize Enter Game Done");

		Destroy(gameObject);
	}
}

