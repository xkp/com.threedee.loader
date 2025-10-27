using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System;
using System.Linq;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

public class BGBuildScript
{
	internal static class Directories
	{
		public static string inputFolder { get; set; }
		public static string outputFolder { get; set; }
		public static string gameItemPath { get; set; }
		public static string modulePath { get; set; }
	}

	static void BindDirectories()
	{
		string[] args = System.Environment.GetCommandLineArgs();

		var i = 0;
		while (i < args.Length)
		{
			var arg = args[i];
			if (arg == "-inputFolder")
				Directories.inputFolder = args[i + 1];
			else if (arg == "-outputFolder")
				Directories.outputFolder = args[i + 1];
			else if (arg == "-itemFile")
				Directories.gameItemPath = args[i + 1];
			else if (arg == "-moduleFolder")
				Directories.modulePath = args[i + 1];

			i++;
		}
	}

	[Serializable]
	class BuildStepData
	{
		public List<string> dependencies; //for install step
	}

	[Serializable]
	class BuildStep
	{
		public string name { get; set; }
		public List<string> errors = new List<string>();
		public BuildStepData data { get; set; } = new BuildStepData();
	}

	static class BuildState
	{
		public static List<BuildStep> steps = new List<BuildStep>();

		public static void AddError(string stepName, string message)
		{
			var step = steps.FirstOrDefault(s => s.name == stepName);
			if (step == null)
			{
				step = new BuildStep
				{
					name = stepName,
					errors = new List<string> { message }
				};
				steps.Add(step);
			}
			else
				step.errors.Add(message);
		}

		public static BuildStep GetStep(string stepName)
		{
			return steps.Find(s => s.name == stepName);
		}

		public static BuildStep Add(string installStep)
		{
			var step = new BuildStep { name = installStep };
			steps.Add(step);
			return step;
		}
	}

	static void BindState()
	{
		if (Directory.Exists(Directories.inputFolder))
		{
			var stateFile = $"{Directories.inputFolder}\\Assets\\Big Game\\build.json";
			if (File.Exists(stateFile))
			{
				string json = File.ReadAllText(stateFile);
				BuildState.steps = JsonUtility.FromJson<List<BuildStep>>(json);
			}
		}
		else
		{
			BuildState.steps.Clear();
		}
	}

	private static void SaveState()
	{
		if (Directory.Exists(Directories.inputFolder))
		{
			var stateFile = $"{Directories.inputFolder}\\Assets\\Big Game\\build.json";
			string json = JsonUtility.ToJson(BuildState.steps);
			File.WriteAllText(stateFile, json);
		}
	}

	static string CreateStep = "create";
	static string InstallStep = "install";

	public static void Create()
	{
		Debug.Log($"[threedee] calling Create");

		BindDirectories();
		BindState();

		try
		{
			if (BuildState.steps.Any())
			{
				Console.WriteLine($"Create has already been ran for this game");
				return;
			}

			if (!File.Exists(Directories.gameItemPath))
			{
				BuildState.AddError(CreateStep, $"Missing item file {Directories.gameItemPath ?? string.Empty}");
				return;
			}

			//build dependencies
			Debug.Log($"[threedee] searching for dependencies...");

			var modules = LoadGameModules(Directories.gameItemPath, Directories.modulePath);
			var packageDependencies = new List<string>();
			var assetDependencies = new List<string>();
			foreach (var module in modules)
			{
				if (module.dependencies != null)
				{
					Debug.Log($"[threedee] found {module.dependencies.Count} dependencies on module {module.name}");
					foreach (var dependency in module.dependencies)
					{
						if (isAssetDependency(dependency))
							assetDependencies.Add(dependency);
						else if (IsPackageDependency(dependency))
							packageDependencies.Add(dependency);
						else
							assetDependencies.Add(dependency);
					}
				}
			}

			Debug.Log($"[threedee] installing dependencies...");
			InstallUPM(packageDependencies.ToArray());
			AddPackagesToStep(assetDependencies);
		}
		finally
		{
			SaveState();
		}
	}

	private static bool isAssetDependency(string dependency)
	{
		return dependency.Contains('|');
	}

	private static void AddPackagesToStep(List<string> assetDependencies)
	{
		var step = BuildState.GetStep(CreateStep);
		if (step == null)
		{
			step = BuildState.Add(CreateStep);
		}

		step.data.dependencies = assetDependencies;
		if (assetDependencies?.Count > 0)
		{
			step.errors.Add("Depedendencies need to be installed");
		}
	}

	private const int DefaultPerPackageTimeoutSec = 600; // 10 minutes per package
	private static readonly List<(string id, AddRequest request, DateTime started)> _active = new();
	private static readonly List<(string id, string error)> _failed = new();
	private static readonly List<string> _succeeded = new();
	private static int _timeoutPerPkgSec = DefaultPerPackageTimeoutSec;
	private static bool _started = false;
	private static void InstallUPM(string[] packages)
	{
		try
		{
			if (_started) return;
			_started = true;

			if (packages.Length == 0)
			{
				Debug.LogError("[BatchAddUpmPackages] No packages specified. Use -packagesFile or -packages.");
				return;
			}

			Debug.Log($"[BatchAddUpmPackages] Installing {packages.Length} package(s) (timeout {_timeoutPerPkgSec}s each) …");

			foreach (var id in packages)
				QueueAdd(id);

			// Poll until all requests complete
			EditorApplication.update += Tick;
		}
		catch (Exception ex)
		{
			Debug.LogError($"[BatchAddUpmPackages] Failed to start: {ex}");
			EditorApplication.Exit(1);
		}
	}

	private static void QueueAdd(string id)
	{
		try
		{
			// Unity will ignore duplicates already in manifest.json
			var req = Client.Add(id);
			_active.Add((id, req, DateTime.UtcNow));
			Debug.Log($"[BatchAddUpmPackages] Add queued: {id}");
		}
		catch (Exception ex)
		{
			_failed.Add((id, $"Exception while queuing add: {ex.Message}"));
			Debug.LogError($"[BatchAddUpmPackages] Queue failed: {id}\n{ex}");
		}
	}

	private static void Tick()
	{
		// Check each active request
		for (int i = _active.Count - 1; i >= 0; i--)
		{
			var (id, req, started) = _active[i];

			// Timeout?
			if ((DateTime.UtcNow - started).TotalSeconds > _timeoutPerPkgSec)
			{
				_failed.Add((id, "Timeout"));
				Debug.LogError($"[BatchAddUpmPackages] TIMEOUT installing: {id}");
				_active.RemoveAt(i);
				continue;
			}

			switch (req.Status)
			{
				case StatusCode.InProgress:
					// still working
					break;

				case StatusCode.Success:
					_succeeded.Add(id);
					Debug.Log($"[BatchAddUpmPackages] Installed: {id}");
					_active.RemoveAt(i);
					break;

				case StatusCode.Failure:
					var message = req.Error == null ? "Unknown error" : $"{req.Error.message} (code {req.Error.errorCode})";
					_failed.Add((id, message));
					Debug.LogError($"[BatchAddUpmPackages] FAILED: {id} – {message}");
					_active.RemoveAt(i);
					break;
			}
		}

		// Done?
		if (_active.Count == 0)
		{
			EditorApplication.update -= Tick;

			// Force refresh to import any newly added assets/asmdefs
			try
			{
				AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
				AssetDatabase.SaveAssets();
			}
			catch { /* headless safe */ }

			// Summary + exit code
			if (_failed.Count == 0)
			{
				Debug.Log($"[BatchAddUpmPackages] All packages installed successfully ({_succeeded.Count}).");
				foreach (var id in _succeeded) Debug.Log($"  ✔ {id}");
				EditorApplication.Exit(0);
			}
			else
			{
				Debug.LogError($"[BatchAddUpmPackages] Completed with failures. Success: {_succeeded.Count}, Failed: {_failed.Count}");
				foreach (var id in _succeeded) Debug.Log($"  ✔ {id}");
				foreach (var (id, err) in _failed) Debug.LogError($"  ✖ {id} – {err}");
				EditorApplication.Exit(1);
			}
		}
	}
	private static bool IsPackageDependency(string dependency)
	{
		return !string.IsNullOrEmpty(dependency) && dependency.Split(".").Length > 2;
	}

	[Serializable]
	class ImportGame
	{
		public List<string> modules;
	}

	[Serializable]
	class ImportModule
	{
		public string id;
		public string name;
		public List<string> dependencies;
	}

	private static List<ImportModule> LoadGameModules(string gameItemPath, string modulePath)
	{
		var result = new List<ImportModule>();
		var jsonContent = File.ReadAllText(gameItemPath);
		var import = JsonUtility.FromJson<ImportGame>(jsonContent);
		if (import != null)
		{
			foreach (var moduleId in import.modules)
			{
				var bgmFile = Path.Combine(modulePath, moduleId, "module.bgm");
				if (File.Exists(bgmFile))
				{
					var bgmFileContents = File.ReadAllText(bgmFile);
					var module = JsonUtility.FromJson<ImportModule>(bgmFileContents);
					result.Add(module);
				}
			}
		}

		return result;
	}

	public static async void CreateGame()
	{
		Console.WriteLine("Creating folder structure...");
		string inputFolder;
		string outputFolder;
		string gameItemPath;
		string modulePath;
		if (!Scaffold(out inputFolder, out outputFolder, out gameItemPath, out modulePath))
			return;

		Console.WriteLine($"gameItemPath = {gameItemPath}");
		Console.WriteLine($"modulePath = {modulePath}");

		Console.WriteLine("Starting scene generation...");
		Scene scene = OpenDefaultScene();
		if (!scene.IsValid())
		{
			Console.WriteLine("There must be at least one valid scene.");
			return;
		}

		Console.WriteLine("Loading environment assets...");
		var postProcess = new List<PostProcessNode>();
		ThreedeeLoader.Load(inputFolder, outputFolder, postProcess);

		await BigGameLoader.Load(gameItemPath, modulePath, postProcess);

		//save before generating light maps
		EditorSceneManager.SaveScene(scene);

		Console.WriteLine("Generating lightmaps...");
		GenerateLightmaps();

		UpdateNavMeshes();

		Console.WriteLine("Finishing...");
		EditorSceneManager.SaveScene(scene);

		Debug.Log("Threedee scene generation completed. Scene saved to: " + scene.path);

		// Trigger a build
		PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
		//PlayerSettings.WebGL.template = "BigGame";

		string buildPath = Path.Combine(outputFolder, "Build");
		BuildPipeline.BuildPlayer(new BuildPlayerOptions
		{
			scenes = EditorBuildSettings.scenes.Select(s => s.path).ToArray(),
			//scenes = new[] { scene.path }, //todo: added scenes
			locationPathName = buildPath,
			target = BuildTarget.WebGL, // Adjust target as necessary
			options = BuildOptions.None
		});

		Debug.Log("Build completed. Build located at: " + buildPath);
	}

	private static void UpdateNavMeshes()
	{
		//build nav meshes
		NavMeshSurface[] surfaces = GameObject.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);

		// Iterate over each surface and rebuild its nav mesh
		foreach (NavMeshSurface surface in surfaces)
		{
			if (surface != null)
			{
				surface.BuildNavMesh();
			}
		}
	}

	public static async void UpdateGame()
	{
		string inputFolder;
		string outputFolder;
		string gameItemPath;
		string modulePath;
		if (!Scaffold(out inputFolder, out outputFolder, out gameItemPath, out modulePath))
			return;

		Scene scene = OpenDefaultScene();
		if (!scene.IsValid())
		{
			Console.WriteLine("There must be at least one valid scene.");
			return;
		}

		Console.WriteLine("Loading environment assets...");
		await BigGameLoader.Update(gameItemPath, modulePath);

		UpdateNavMeshes();

		//save before generating light maps
		EditorSceneManager.SaveScene(scene);

		// Trigger a build
		PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
		//PlayerSettings.WebGL.template = "BigGame";

		string buildPath = Path.Combine(outputFolder, "Build");

		BuildPipeline.BuildPlayer(new BuildPlayerOptions
		{
			scenes = EditorBuildSettings.scenes.Select(s => s.path).ToArray(),
			//scenes = new[] { scene.path }, //todo: added scenes
			locationPathName = buildPath,
			target = BuildTarget.WebGL, // Adjust target as necessary
			options = BuildOptions.None
		});

		Debug.Log("Build completed. Build located at: " + buildPath);
	}

	private static void GenerateLightmaps()
	{
		// Create and configure lighting settings
		LightingSettings lightingSettings = new LightingSettings();
		lightingSettings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU; // Use Progressive GPU lightmapper
		lightingSettings.maxBounces = 2; // Multiplier for indirect light intensity
		lightingSettings.indirectResolution = 2.0f; // Texels per unit for indirect light
		lightingSettings.lightmapResolution = 40.0f; // Texels per unit for baked lightmaps
		lightingSettings.lightmapPadding = 4; // Padding between UV islands
		lightingSettings.filteringMode = LightingSettings.FilterMode.Auto; // Use automatic filtering
		lightingSettings.ao = true; // Enable Ambient Occlusion
		lightingSettings.aoMaxDistance = 2.0f; // Maximum distance for AO calculations
		lightingSettings.lightmapCompression = LightmapCompression.NormalQuality;

		// Assign the lighting settings to the scene
		Lightmapping.lightingSettings = lightingSettings;

		Debug.Log("Lightmap settings configured. Starting bake...");

		// Start the baking process
		if (Lightmapping.Bake())
		{
			//Lightmapping.Bake(); //bake a second time, not sure why
			Debug.Log("Lightmap baking started successfully.");
		}
		else
		{
			Debug.LogError("Lightmap baking failed to start. Check your scene setup.");
		}

	}

	private static Scene OpenDefaultScene()
	{
		Scene scene = EditorSceneManager.OpenScene("Assets/Big Game/MainScene.unity", OpenSceneMode.Single);
		return scene;
	}

	private static bool Scaffold(out string inputFolder, out string outputFolder, out string gameItemPath, out string modulePath)
	{
		inputFolder = string.Empty;
		outputFolder = string.Empty;
		gameItemPath = string.Empty;
		modulePath = string.Empty;

		string[] args = System.Environment.GetCommandLineArgs();

		var i = 0;
		while (i < args.Length)
		{
			var arg = args[i];
			if (arg == "-inputFolder")
				inputFolder = args[i + 1];
			else if (arg == "-outputFolder")
				outputFolder = args[i + 1];
			else if (arg == "-itemFile")
				gameItemPath = args[i + 1];
			else if (arg == "-moduleFolder")
				modulePath = args[i + 1];

			i++;
		}

		if (!Directory.Exists(inputFolder))
		{
			Console.WriteLine("Input folder does not exist: " + inputFolder);
			return false;
		}

		return !string.IsNullOrEmpty(inputFolder) && !string.IsNullOrEmpty(outputFolder);
	}


	private static void DirectoryCopy(string sourceDirName, string destDirName)
	{
		DirectoryInfo dir = new DirectoryInfo(sourceDirName);
		if (!dir.Exists)
		{
			throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourceDirName);
		}

		DirectoryInfo[] dirs = dir.GetDirectories();
		Directory.CreateDirectory(destDirName);

		FileInfo[] files = dir.GetFiles();
		foreach (FileInfo file in files)
		{
			string tempPath = Path.Combine(destDirName, file.Name);
			file.CopyTo(tempPath, false);
		}

		foreach (DirectoryInfo subdir in dirs)
		{
			string tempPath = Path.Combine(destDirName, subdir.Name);
			DirectoryCopy(subdir.FullName, tempPath);
		}
	}

	private static void MarkObjectAndChildrenStatic(GameObject obj)
	{
		obj.isStatic = true;

		foreach (Transform child in obj.transform)
		{
			MarkObjectAndChildrenStatic(child.gameObject);
		}
	}
}

