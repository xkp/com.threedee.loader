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
using UnityEditor.Rendering;
using UnityEngine;

public class BigGameLoader
{
	public static async Task Load(string gameItemPath, string modulePath)
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

		//run the modules
		foreach (var module in modules)
		{
			await module.Init(modules, game);
		}

		foreach (var module in modules)
		{
			await module.ConfigProject();
		}

		if (game.GameItems != null)
		{
			foreach (var item in game.GameItems)
			{
				await CreateGameObject(item, modules);
			}
		}

		foreach (var module in modules)
		{
			await module.Cleanup();
		}
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

		modules = BuildModules(result, modulePath, usedModules);

		var userItemTemplatesNodes = root["userItemTemplates"] as JArray;
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
					await CreateGameObject(gi, modules);
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

	private static async Task CreateGameObject(GameItem item, IEnumerable<IBGModule> modules)
	{
		var module = GetModuleById(modules, item.ModuleId);
		if (module != null)
		{
			var template = module.GetTemplateItem(item.TemplateId);
			if (template != null)
			{
				//give the module a chance for custom importing
				GameObject go = await module.CreateItem(item, template);
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
	}
}
