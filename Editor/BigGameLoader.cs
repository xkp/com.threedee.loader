using GLTFast;
using Unity.Plastic.Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;

public class BigGameLoader
{
	public static async Task Load(string gameItemPath, string buildFilePath, string modulePath, string assetPath, List<PostProcessNode> preprocess)
	{
		Debug.Log(gameItemPath);
		string jsonContent = File.ReadAllText(gameItemPath);
		JObject jsonObject = JObject.Parse(jsonContent);

		string buildContent = File.ReadAllText(buildFilePath);
		JObject buildFileObject = JObject.Parse(buildContent);

		var game = LoadGame(jsonObject, modulePath, out var modules);
		if (game == null)
		{
			Debug.Log($"could not load: {gameItemPath}");
			return;
		}

		//run the modules
		foreach (var module in modules)
		{
			await module.Init(modules, game);
		}

		foreach (var module in modules)
		{
			await module.ConfigProject();
		}

		foreach (var module in modules)
		{
			if (!preprocess.Any())
				break;

			await module.Preprocess(preprocess);
		}

		if (game.GameItems != null)
		{
			foreach (var item in game.GameItems)
			{
				JObject buildItem = GetBuildItem(buildFileObject, item.BuildId);
				await CreateGameObject(item, buildItem, modules, assetPath);
			}
		}

		foreach (var module in modules)
		{
			await module.Cleanup();
		}
	}

	private static JObject GetBuildItem(JObject buildFileObject, string buildId)
	{
		if (string.IsNullOrEmpty(buildId) || buildFileObject == null)
			return null;

		//TODO: standardize
		var avatarId = buildFileObject.SelectToken("avatar.id")?.Value<string>();
		if (avatarId == buildId)
			return buildFileObject.SelectToken("avatar") as JObject;

		var npcs = buildFileObject.SelectToken("npcs") as JArray;
		if (npcs != null)
		{
			foreach (var obj in npcs.OfType<JObject>())
			{
				string id = (string)obj["id"];
				if (id == buildId)
					return obj;
			}
		}

		return null;
	}

	private static IBGModule GetModuleById(IEnumerable<IBGModule> modules, string moduleId)
	{
		return modules.FirstOrDefault(m => m.Model.id == moduleId);
	}

	private static IEnumerable<IBGModule> BuildModules(BigGame game, string modulePath, List<string> usedModules)
	{
		var result = new List<IBGModule>();

		string[] jsonFiles = Directory.GetFiles(modulePath, "module.bgm", SearchOption.AllDirectories);

		foreach (string filePath in jsonFiles)
		{
			if (filePath.Contains("Assets\\module.bgm"))
				continue;

			string json = File.ReadAllText(filePath);
			try
			{
				JObject jsonObject = JObject.Parse(json);

				var moduleData = LoadModule(jsonObject);
				var used = usedModules.Contains(moduleData.id);
				if (used)
				{
					var module = LoadAndInitializeController(moduleData.controller);
					if (module == null)
					{
						module = new BaseBGModel();
					}

					if (module != null)
					{
						module.Model = moduleData;
						result.Add(module);
						game.Modules.Add(moduleData);
					}
				}
			}
			catch (System.Exception ex)
			{
				Debug.LogError("Failed to parse module.json: " + ex.Message);
			}
		}

		return result;
	}

	private static IBGModule LoadAndInitializeController(string controllerName)
	{
		if (string.IsNullOrEmpty(controllerName))
			return null;

		// Attempt to find the type using the provided controllerName.
		// Make sure the controllerName is fully qualified if it is in a namespace.
		Type controllerType = Type.GetType(controllerName);

		if (controllerType == null)
		{
			Debug.LogError($"Controller type '{controllerName}' not found. " +
						   $"Make sure it is fully qualified and exists in the project.");
			return null;
		}

		// Check if the type implements IBGModule
		if (!typeof(IBGModule).IsAssignableFrom(controllerType))
		{
			Debug.LogError($"The controller '{controllerName}' does not implement IBGModule.");
			return null;
		}

		// Create an instance of the controller.
		// For MonoBehaviour types, you typically need to add them to a GameObject.
		object instance = Activator.CreateInstance(controllerType);
		return instance as IBGModule;
	}

	public static BigGame LoadGame(string gameItemPath, string modulePath, out IEnumerable<IBGModule> modules)
	{
		string jsonContent = File.ReadAllText(gameItemPath);
		JObject jsonObject = JObject.Parse(jsonContent);
		return LoadGame(jsonObject, modulePath, out modules);
	}

	private static string CoreModuleId = "53D8F89C-4EDC-4DEF-B464-015BD1187E95";
	private static BigGame LoadGame(JObject root, string modulePath, out IEnumerable<IBGModule> modules)
	{
		var result = new BigGame();
		result.Name = root["name"]?.ToString();

		var usedModules = new List<string>();

		var modulesNode = root["modules"] as JArray;
		foreach (var moduleNode in modulesNode)
		{
			var moduleName = moduleNode.ToString();
			usedModules.Add(moduleName);
		}

		if (!usedModules.Contains(CoreModuleId))
		{
			usedModules.Add(CoreModuleId);
		}

		modules = BuildModules(result, modulePath, usedModules);

		/*		var userItemTemplatesNodes = root["userItemTemplates"] as JArray;
				foreach (JObject userItemTemplateNode in userItemTemplatesNodes)
				{
					var templateModuleId = userItemTemplateNode["_moduleId"]?.ToString();
					var templateModule = modules.FirstOrDefault(m => m.Model.id == templateModuleId);
					if (templateModule == null)
					{
						Debug.Log($"A user template is referencing an unknown module: {templateModuleId}");
						continue;
					}

					var templateItem = LoadBigGameItem(userItemTemplateNode);
					templateModule.Model.userTemplates.Add(templateItem);
				}
		*/
		/*		var mainCharacterNode = root["character"] as JObject;
				result.Character = LoadCharacter(mainCharacterNode);
		*/
		var itemsNode = root["items"] as JArray;
		foreach (JObject itemNode in itemsNode)
		{
			var gameItem = LoadGameItem(itemNode, result);
			result.GameItems.Add(gameItem);
		}

		return result;
	}

	private static GameItem LoadGameItem(JObject itemNode, BigGame game)
	{
		var result = new GameItem();
		result.Id = itemNode["id"]?.ToString();
		result.Name = itemNode["name"]?.ToString();
		result.ModuleId = itemNode["moduleId"]?.ToString();
		result.TemplateId = itemNode["templateId"]?.ToString();
		result.BuildId = itemNode["buildId"]?.ToString();
		result.Values = LoadValues(itemNode["values"] as JObject);
		result.Position = LoadVector(itemNode["position"] as JArray);
		result.Rotation = LoadQuaternion(itemNode["rotation"] as JArray);
		result.Scale = LoadVector(itemNode["scale"] as JArray);

		Debug.Log($"Adding game item: {result.Id}");
		return result;
	}

	private static Quaternion LoadQuaternion(JArray values)
	{
		if (values == null)
			return new Quaternion();

		return new Quaternion()
		{
			x = (float)values[0],
			y = (float)values[1],
			z = (float)values[2],
			w = (float)values[3]
		};
	}

	private static Vector3 LoadVector(JArray values)
	{
		if (values == null)
			return new Vector3();

		return new Vector3()
		{
			x = (float)values[0],
			y = (float)values[1],
			z = (float)values[2]
		};
	}

	private static Dictionary<string, object> LoadValues(JObject values)
	{
		var result = new Dictionary<string, object>();
		if (values != null)
		{
			foreach (var property in values.Properties())
			{
				string key = property.Name;
				JToken value = property.Value;
				switch (value.Type)
				{
					case JTokenType.String:
						result[key] = (string)value;
						break;
					case JTokenType.Integer:
						result[key] = (int)value;
						break;
					case JTokenType.Float:
						result[key] = (float)value;
						break;
					case JTokenType.Boolean:
						result[key] = (bool)value;
						break;
					case JTokenType.Object:
						result[key] = property.Value;
						break;
					default:
						Debug.Log($"INVALID VALUE TYPE {value.Type}");
						break;
				}
			}
		}

		return result;
	}

	private static BigGameModule LoadModule(JObject moduleNode)
	{
		var result = new BigGameModule();
		result.id = moduleNode["id"].ToString();
		result.name = moduleNode["name"]?.ToString();
		result.itemGroups = LoadItemGroups(moduleNode["itemGroups"] as JArray);
		result.controller = moduleNode["controller"]?.ToString();

		return result;
	}

	private static List<BigGameItemGroup> LoadItemGroups(JArray groups)
	{
		var result = new List<BigGameItemGroup>();
		foreach (var groupNode in groups)
		{
			var itemGroup = LoadItemGroup(groupNode);
			result.Add(itemGroup);
		}

		return result;
	}

	private static BigGameItemGroup LoadItemGroup(JToken groupNode)
	{
		var result = new BigGameItemGroup();
		result.name = groupNode["name"]?.ToString();
		result.icon = groupNode["icon"]?.ToString();
		result.items = LoadGameItemTemplates(groupNode["items"] as JArray);

		return result;
	}

	private static List<BigGameItem> LoadGameItemTemplates(JArray templates)
	{
		var result = new List<BigGameItem>();

		if (templates != null)
		{
			foreach (var templateNode in templates)
			{
				var bgi = LoadBigGameItem(templateNode as JObject);
				result.Add(bgi);
			}
		}

		return result;
	}

	private static BigGameItem LoadBigGameItem(JObject templateNode)
	{
		var result = new BigGameItem();
		result.id = templateNode["id"].ToString();
		result.name = templateNode["name"]?.ToString();
		result.description = templateNode["description"]?.ToString();
		result.icon = templateNode["image"]?.ToString();
		result.icon3d = templateNode["model"]?.ToString();
		result.prefab = templateNode["prefab"]?.ToString();

		if (templateNode.TryGetValue("unique", out JToken token))
			result.unique = (bool)token;

		if (templateNode.TryGetValue("notDraggable", out JToken ndtoken))
			result.notDraggable = (bool)ndtoken;

		if (templateNode.TryGetValue("template", out JToken ttoken))
			result.template = (bool)ttoken;

		result.Properties = LoadProperties(templateNode["properties"] as JArray);
		result.Values = LoadValues(templateNode["values"] as JObject);

		return result;
	}

	private static List<BigGameProperty> LoadProperties(JArray properties)
	{
		var result = new List<BigGameProperty>();
		foreach (var propNode in properties)
		{
			var newProp = new BigGameProperty();
			newProp.name = propNode["name"]?.ToString();
			newProp.data = propNode["data"]?.ToString();
			newProp.type = GetPropType(propNode["type"]?.ToString());

			result.Add(newProp);
		}
		return result;
	}

	/*	private static List<BigGameEnumItem> LoadEnumItems(JArray enumItems)
		{
			var result = new List<BigGameEnumItem>();
			foreach (var enumItem in enumItems)
			{
				var newItem = new BigGameEnumItem();
				newItem.name = enumItem["name"]?.ToString();
				newItem.icon = enumItem["icon"]?.ToString();

				result.Add(newItem);
			}
			return result;
		}
	*/
	private static BigGamePropertyType GetPropType(string typeName)
	{
		switch (typeName)
		{
			case "string":
				return BigGamePropertyType.BGPT_STRING;
			case "int":
				return BigGamePropertyType.BGPT_INT;
			case "float":
				return BigGamePropertyType.BGPT_FLOAT;
			case "bool":
				return BigGamePropertyType.BGPT_BOOL;
			case "enum":
				return BigGamePropertyType.BGPT_ENUM;
			case "gameitem":
				return BigGamePropertyType.BGPT_GAMEITEM;
			case "asset":
				return BigGamePropertyType.BGPT_ASSET;
			case "object":
				return BigGamePropertyType.BGPT_OBJECT;
		}

		throw new InvalidDataException($"Unkown type: {typeName}");
	}

	private static List<string> LoadPackageList(JArray values)
	{
		var result = new List<string>();
		foreach (var v in values)
		{
			result.Add(v.ToString());
		}

		return result;
	}

	public static Dictionary<string, GameObject> GetGameObjectsIndex()
	{
		Dictionary<string, GameObject> guidToObjectMap = new Dictionary<string, GameObject>();

		// Find all game objects in the scene
		GameObject[] allGameObjects = GameObject.FindObjectsOfType<GameObject>();

		// Iterate over each game object
		foreach (GameObject go in allGameObjects)
		{
			// Check if the game object has a name matching a GUID
			if (System.Guid.TryParse(go.name, out System.Guid guid) && go.transform.parent == null)
			{
				// If it's a valid GUID, add it to the dictionary
				guidToObjectMap[guid.ToString().ToUpper()] = go;
			}
		}

		return guidToObjectMap;
	}

	public static async Task Update(string gameItemPath, string modulePath)
	{
		string jsonContent = File.ReadAllText(gameItemPath);
		JObject jsonObject = JObject.Parse(jsonContent);

		var game = LoadGame(jsonObject, modulePath, out var modules);
		if (game == null)
		{
			Debug.Log($"could not load: {gameItemPath}");
			return;
		}

		foreach (var module in modules)
		{
			await module.Init(modules, game);
		}

		var index = GetGameObjectsIndex();
		if (game.GameItems != null)
		{
			var allGameItems = new Dictionary<string, GameItem>();

			//Build update
			var toAdd = new List<GameItem>();
			var toUpdate = new List<Tuple<GameItem, GameObject>>();
			var toRemove = new List<string>();
			foreach (var item in game.GameItems)
			{
				allGameItems[item.Id] = item;

				var existingObject = null as GameObject;
				if (index.TryGetValue(item.Id, out GameObject bgo))
				{
					if (bgo != null)
						existingObject = bgo;

					toUpdate.Add(new Tuple<GameItem, GameObject>(item, existingObject));
				}
				else
				{
					toAdd.Add(item);
				}
			}

			foreach (var gok in index.Keys)
			{
				if (!allGameItems.ContainsKey(gok))
					toRemove.Add(gok);
			}

			//Apply update
			foreach (var tr in toRemove)
			{
				GameObject go = index[tr];
				if (go != null)
				{
					//use case: removing non-prefab objects after the game item is deleted
					string templateId = GetTemplateIdFromObject(go);
					if (!string.IsNullOrEmpty(templateId))
					{
						var module = GetModuleByTemplateId(modules, templateId);
						if (module != null)
						{
							await module.RemoveItem(go);
						}
					}

					GameObject.DestroyImmediate(go);
				}
			}

			foreach (var gi in toAdd)
			{
				var module = GetModuleById(modules, gi.ModuleId);
				if (module != null)
				{
					//TODO: load build items
					await CreateGameObject(gi, null, modules, string.Empty);
				}
			}

			foreach (var goi in toUpdate)
			{
				var module = GetModuleById(modules, goi.Item1.ModuleId);
				if (module != null)
				{
					await module.UpdateItem(goi.Item1, goi.Item2);
					UpdateGameObject(goi.Item1, goi.Item2);
				}
			}
		}
	}

	private static GameObject CreateIndexObject(string id, string templateId)
	{
		GameObject go = new GameObject(id);
		go.name = id;

		GameObject goi = new GameObject(templateId);
		goi.transform.parent = go.transform;

		return go;
	}

	private static string GetTemplateIdFromObject(GameObject go)
	{
		if (go.transform.childCount == 1)
		{
			var child = go.transform.GetChild(0);
			if (System.Guid.TryParse(child.name, out Guid result))
				return child.name;
		}

		return null;
	}

	private static IBGModule GetModuleByTemplateId(IEnumerable<IBGModule> modules, string templateId)
	{
		foreach (var module in modules)
		{
			var template = module.GetTemplateItem(templateId);
			if (template != null)
				return module;
		}

		return null;
	}

	private static void UpdateGameObject(GameItem item, GameObject go)
	{
		if (go != null)
		{
			go.transform.position = item.Position;
			go.transform.rotation = item.Rotation;
			go.transform.localScale = item.Scale;
		}
	}

	private static async Task CreateGameObject(GameItem item, JObject buildItem, IEnumerable<IBGModule> modules, string assetPath)
	{
		var module = GetModuleById(modules, item.ModuleId);
		if (module != null)
		{
			var template = module.GetTemplateItem(item.TemplateId);
			if (template != null)
			{
				//give the module a chance for custom importing
				GameObject go = await module.CreateItem(item, template, buildItem);
				if (go == null)
				{
					//revert to prefab
					var prefabName = Path.GetFileNameWithoutExtension(template.prefab);
					if (!string.IsNullOrEmpty(prefabName))
					{
						string[] guids = AssetDatabase.FindAssets(prefabName + " t:prefab");
						if (guids.Length == 0)
						{
							Debug.LogError($"No prefab found with name '{prefabName}'");
							return;
						}

						// Assume the first found prefab is the one we want.
						string path = AssetDatabase.GUIDToAssetPath(guids[0]);
						GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
						if (prefab == null)
						{
							Debug.LogError($"Failed to load prefab at path: {path}");
							return;
						}

						GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
						UpdateGameObject(item, instance);

						go = instance;
					}
				}

				if (go == null)
				{
					//create a tag game object to represent this game item
					go = CreateIndexObject(item.Id, item.TemplateId);
					go.hideFlags = HideFlags.NotEditable;
				}

				go.name = item.Id; //just in case
			}
		}
		else
		{
			switch (item.ModuleId)
			{
				case "MODULE_USER_ASSET":
					var glbPath = Path.Combine(assetPath, item.TemplateId, "object.glb");
					if (File.Exists(glbPath))
					{
						var unityPath = $"Assets/Big Game/Assets/{item.TemplateId}/object.glb";
						var go = await ImportGlbAsync(glbPath, unityPath);
						go.name = item.Id;
						UpdateGameObject(item, go);
					}
					else
					{
						Debug.Log($"Missing local asset: {glbPath}");
					}
					break;
				default:
					Debug.Log($"Invalid module id: {item.ModuleId}");
					break;
			}
		}
	}

	/// <summary>
	/// Imports a GLB into Assets AND instantiates it into the active scene.
	/// Returns the scene instance.
	/// </summary>
	public static async Task<GameObject> ImportGlbAsync(
		string sourceGlbAbsolutePath,
		string targetAssetPath,
		Transform parent = null,
		Vector3? position = null,
		bool selectAndFrame = true)
	{
		if (!File.Exists(sourceGlbAbsolutePath))
			throw new FileNotFoundException("GLB not found", sourceGlbAbsolutePath);

		// targetAssetPath must be "Assets/....glb"
		if (!targetAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException("targetAssetPath must start with 'Assets/'.", nameof(targetAssetPath));

		// Copy file into Assets
		var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
		var absTarget = Path.Combine(projectRoot, targetAssetPath);

		Directory.CreateDirectory(Path.GetDirectoryName(absTarget)!);
		File.Copy(sourceGlbAbsolutePath, absTarget, true);

		// Import (sync, main thread only)
		AssetDatabase.ImportAsset(targetAssetPath, ImportAssetOptions.ForceUpdate);
		await WaitForImportToFinish(targetAssetPath);

		// Resolve the imported GameObject asset (prefab/model root)
		var assetGo = ResolveImportedGameObject(targetAssetPath);
		if (assetGo == null)
			throw new Exception("GLB imported but no GameObject asset found: " + targetAssetPath);

		// Instantiate into the active scene
		GameObject instance = InstantiateIntoScene(assetGo, parent, position ?? Vector3.zero);

		// Mark scene dirty so it saves
		EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

		if (selectAndFrame)
		{
			Selection.activeGameObject = instance;
			SceneView.lastActiveSceneView?.FrameSelected();
		}

		return instance;
	}

	private static GameObject ResolveImportedGameObject(string glbAssetPath)
	{
		// 1) Sometimes the main asset at the glb path is a GameObject
		var main = AssetDatabase.LoadMainAssetAtPath(glbAssetPath) as GameObject;
		if (main != null) return main;

		// 2) Or it's a sub-asset (common with scripted importers)
		var all = AssetDatabase.LoadAllAssetsAtPath(glbAssetPath);

		// Prefer a prefab-like root: first GameObject that looks like a root (has Transform, name not empty)
		var candidates = all.OfType<GameObject>().ToList();
		if (candidates.Count == 0) return null;

		// Heuristic: prefer the one that has children (often the root) otherwise first
		var withChildren = candidates.FirstOrDefault(go => go.transform.childCount > 0);
		return withChildren != null ? withChildren : candidates[0];
	}

	private static GameObject InstantiateIntoScene(GameObject assetGo, Transform parent, Vector3 position)
	{
		GameObject instance;

		// If this is a prefab asset, instantiate via PrefabUtility to keep prefab connection
		if (PrefabUtility.GetPrefabAssetType(assetGo) != PrefabAssetType.NotAPrefab)
		{
			instance = (GameObject)PrefabUtility.InstantiatePrefab(assetGo);
		}
		else
		{
			// Otherwise just clone the asset object
			instance = UnityEngine.Object.Instantiate(assetGo);
		}

		Undo.RegisterCreatedObjectUndo(instance, "Import GLB");

		if (parent != null)
			instance.transform.SetParent(parent, false);

		instance.transform.position = position;
		return instance;
	}

	private static Task WaitForImportToFinish(string assetPath)
	{
		var tcs = new TaskCompletionSource<bool>();

		void Check()
		{
			if (!AssetDatabase.IsAssetImportWorkerProcess())
			{
				EditorApplication.delayCall -= Check;
				tcs.TrySetResult(true);
			}
		}

		EditorApplication.delayCall += Check;
		return tcs.Task;
	}
}
