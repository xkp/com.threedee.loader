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

public class BGBuildScript
{
	public static void Create()
	{
		Console.WriteLine("Creating directory structure, no more is needed");
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

