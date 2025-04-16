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
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public class BigGameLoader
{
	public static void Load(string gameItemPath, string modulePath)
	{
		Debug.Log(gameItemPath);
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
			module.Init(modules, game);
		}

		foreach (var module in modules)
		{
			module.ConfigProject();
		}

		if (game.GameItems != null)
		{
			foreach (var item in game.GameItems)
			{
				CreateGameObject(item, modules);
			}
		}

		foreach (var module in modules)
		{
			module.Cleanup();
		}
	}

	private static IBGModule GetModuleById(IEnumerable<IBGModule> modules, int moduleId)
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
				var used = usedModules.Contains(moduleData.name);
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

	private static BigGame LoadGame(JObject root, string modulePath, out IEnumerable<IBGModule> modules)
	{
		var result = new BigGame();
		var usedModules = new List<string>();
		var modulesNode = root["modules"] as JArray;

		foreach (var moduleNode in modulesNode)
		{
			var moduleName = moduleNode.ToString();
			usedModules.Add(moduleName);
		}

		modules = BuildModules(result, modulePath, usedModules);

		var userItemTemplatesNodes = root["userItemTemplates"] as JArray;
		foreach (JObject userItemTemplateNode in userItemTemplatesNodes)
		{
			var templateModuleId = (int)userItemTemplateNode["_moduleId"];
			var templateModule = modules.FirstOrDefault(m => m.Model.id == templateModuleId);
			if (templateModule == null)
			{
				Debug.Log($"A user template is referencing an unknown module: {templateModuleId}");
				continue;
			}

			var templateItem = LoadBigGameItem(userItemTemplateNode);
			templateModule.Model.userTemplates.Add(templateItem);
		}

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
		result.Id = (int)itemNode["id"];
		result.Name = itemNode["name"]?.ToString();
		result.ModuleId = (int)itemNode["moduleId"];
		result.TemplateId = (int)itemNode["templateId"];
		result.Values = LoadValues(itemNode["values"] as JObject);
		result.Position = LoadVector(itemNode["position"] as JArray);
		result.Rotation = LoadQuaternion(itemNode["rotation"] as JArray);
		result.Scale = LoadVector(itemNode["scale"] as JArray);

		Debug.Log($"Adding game item: {result.Id}");
		return result;
	}

	private static Quaternion LoadQuaternion(JArray values)
	{
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
				result[key] = value.ToObject<object>();
			}
		}

		return result;
	}

	private static BigGameCharacter LoadCharacter(JObject mainCharacterNode)
	{
		if (mainCharacterNode == null)
			return null;

		var result = new BigGameCharacter();
		return result; //TODO:
	}

	private static BigGameModule LoadModule(JObject moduleNode)
	{
		var result = new BigGameModule();
		result.id = (int)moduleNode["id"];
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
		result.id = (int)templateNode["id"];
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

	private static Dictionary<string, object> LoadProperties(JArray properties)
	{
		var result = new Dictionary<string, object>();
		return result; //TODO:
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

	private static T ModuleInvoke<T>(BigGameModule module, string method, params object[] args)
	{
		return (T)InvokeStaticMethod(module.controller, method, args);
	}

	private static object InvokeStaticMethod(string className, string methodName, object[] parameters)
	{
		// Get the Type of the class
		Type type = Type.GetType(className);

		if (type == null)
		{
			Debug.LogError($"Class {className} not found.");
			return null;
		}

		// Get the MethodInfo of the static method
		MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

		if (method == null)
		{
			Debug.LogError($"Method {methodName} not found in class {className}.");
			return null;
		}

		try
		{
			// Invoke the method with parameters and return the result
			Debug.LogError($"Calling Method {methodName} in class {className} with {parameters.Length} parameters");
			return method.Invoke(null, parameters); // Pass `null` for the instance since it's a static method
		}
		catch (Exception ex)
		{
			Debug.LogError($"Failed Calling Method {methodName} in class {className} with: {ex.Message}");
		}

		return null;
	}

	private static Regex pattern = new Regex("^(gi|go)_\\d+$");
	public static void Update(string gameItemPath, string modulePath)
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
			module.Init(modules, game);
		}

		if (game.GameItems != null)
		{
			var allGameObjects = GameObject
				.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
				.Where(g => pattern.IsMatch(g.name))
				.ToDictionary(g => g.name);

			var allGameItems = new Dictionary<string, GameItem>();

			//Build update
			var toAdd = new List<GameItem>();
			var toUpdate = new List<Tuple<GameItem, GameObject>>();
			var toRemove = new List<GameObject>();
			foreach (var item in game.GameItems)
			{
				var dataKey = $"gi_{item.Id}";
				allGameItems[dataKey] = item;

				var existingObject = null as GameObject;
				if (allGameObjects.TryGetValue(dataKey, out GameObject bgo))
				{
					existingObject = bgo.gameObject;
				}

				if (existingObject != null)
				{
					toUpdate.Add(new Tuple<GameItem, GameObject>(item, existingObject));
				}
				else
				{
					toAdd.Add(item);
				}
			}

			foreach (var gok in allGameObjects)
			{
				if (!allGameItems.ContainsKey(gok.Key))
					toRemove.Add(gok.Value.gameObject);
			}

			//Apply update
			foreach (var go in toRemove)
			{
				GameObject.Destroy(go);
			}

			foreach (var gi in toAdd)
			{
				var module = GetModuleById(modules, gi.ModuleId);
				if (module == null || !module.UpdateItem(gi))
				{
					CreateGameObject(gi, modules);
				}
			}

			foreach (var goi in toUpdate)
			{
				UpdateGameObject(goi.Item1, goi.Item2);
			}
		}
	}

	private static void UpdateGameObject(GameItem item, GameObject go)
	{
		go.transform.position = item.Position; 
		go.transform.rotation = item.Rotation;
		go.transform.localScale = item.Scale;
	}

	private static void CreateGameObject(GameItem item, IEnumerable<IBGModule> modules)
	{
		var module = GetModuleById(modules, item.ModuleId);
		if (module != null)
		{
			var template = module.GetTemplateItem(item.TemplateId);
			if (template != null)
			{
				//give the module a chance for custom importing
				if (!module.CreateItem(item, template))
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
						instance.name = $"gi_{item.Id}";
						UpdateGameObject(item, instance); 
					}
				}
			}
		}
	}
}
