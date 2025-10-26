using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;


public class BigGame
{
	public string Name { get; set; }
	public string MainModule { get; set; }
	public List<BigGameModule> Modules { get; set; } = new List<BigGameModule>();

	public List<GameItem> GameItems { get; set; } = new List<GameItem>();

	public BigGameModule GetModule(string id)
	{
		return Modules.FirstOrDefault(m => m.id == id);
	}

	public BigGameItem GetTemplate(GameItem item)
	{
		var module = GetModule(item.ModuleId);
		return module?.GetTemplate(item.TemplateId);
	}
}

public class BigGameModule
{
	public string id { get; set; }
	public string name { get; set; }
	public string type { get; set; }
	public string controller { get; set; }
	public List<string> dependencies { get; set; }
	public List<BigGameItemGroup> itemGroups { get; set; }
	public List<BigGameItem> userTemplates { get; set; } = new List<BigGameItem>();

	public BigGameItem GetTemplate(string id)
	{
		foreach (var group in itemGroups)
		{
			var result = group.items.FirstOrDefault(i => i.id == id);
			if (result != null)
				return result;
		}

		foreach (var userTemplate in userTemplates)
		{
			if (userTemplate.id == id)
				return userTemplate;
		}

		return null;
	}
}

public class BigGameItemGroup
{
	public string name { get; internal set; }
	public string icon { get; internal set; }
	public List<BigGameItem> items { get; set; }
}

public enum BigGamePropertyType
{
	BGPT_STRING,
	BGPT_INT,
	BGPT_FLOAT,
	BGPT_BOOL,
	BGPT_ENUM,
	BGPT_GAMEITEM,
	BGPT_ASSET,
	BGPT_PREFAB,
	BGPT_OBJECT,
}

public class BigGameProperty
{
	public string name { get; set; }
	public BigGamePropertyType type { get; set; }
	public string data { get; set; }
}

public class BigGameItem
{
	public string id { get; set; }
	public string name { get; set; }
	public string description { get; set; }
	public string icon { get; set; }
	public string icon3d { get; set; }
	public string prefab { get; set; }
	public bool unique { get; set; }
	public bool notDraggable { get; set; }
	public bool template { get; set; }
	public List<BigGameProperty> Properties { get; set; }
	public Dictionary<string, object> Values { get; set; }

	public bool GetPropertyValue<T>(string key, out T value)
	{
		if (Values != null && Values.TryGetValue(key, out object v))
		{
			value = (T)v;
			return true;
		}

		value = default(T);
		return false;
	}
}

public class GameItem
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string ModuleId { get; set; }
	public string TemplateId { get; set; }
	public Dictionary<string, object> Values { get; set; }
	public Vector3 Position { get; set; }
	public Quaternion Rotation { get; set; }
	public Vector3 Scale { get; set; }

	public bool GetPropertyValue<T>(string key, out T value)
	{
		if (Values != null && Values.TryGetValue(key, out object v))
		{
			value = (T)v;
			return true;
		}

		value = default(T);
		return false;
	}
}


//modules
public interface IBGModule
{
	BigGameModule Model { get; set; }
	BigGameItem GetTemplateItem(string id);

	Task Init(IEnumerable<IBGModule> modules, BigGame game);
	Task ConfigProject();
	Task Build();
	Task Cleanup();

	Task<GameObject> CreateItem(GameItem item, BigGameItem template);
	Task<bool> UpdateItem(GameItem item, GameObject go);
	Task RemoveItem(GameObject go);
	Task Preprocess(IList<PostProcessNode> preprocess);
}

public interface IBGGameModule
{
	T GetBuildValue<T>(string key);
	string GetAlias();
	void AddScene(string name, bool starter = false);
}

public interface IBGCharacterModule
{
	void BuildCharacter(GameObject go, JObject config);
}

public class BaseBGModel : IBGModule
{
	public BigGameModule Model { get; set; }

	public virtual Task Init(IEnumerable<IBGModule> modules, BigGame game)
	{
		_game = game;
		_modules = modules;
		_gameModule = modules.FirstOrDefault(m =>
		{
			var g = m as IBGGameModule;
			if (g != null)
				return true;
			return false;
		}) as IBGGameModule;

		return Task.CompletedTask;
	}

	public virtual Task ConfigProject()
	{
		return Task.CompletedTask;
	}

	public virtual Task<GameObject> CreateItem(GameItem item, BigGameItem template)
	{
		return Task.FromResult(null as GameObject);
	}

	public virtual Task Build()
	{
		return Task.CompletedTask;
	}

	public virtual Task Cleanup()
	{
		return Task.CompletedTask;
	}

	public BigGameItem GetTemplateItem(string id)
	{
		if (Model == null)
			return null;

		foreach (var group in Model.itemGroups)
		{
			var result = group.items.FirstOrDefault(x => x.id == id);
			if (result != null)
				return result;
		}

		if (Model.userTemplates != null)
		{
			foreach (var ut in Model.userTemplates)
			{
				if (ut.id == id)
					return ut;
			}
		}

		return null;
	}

	public virtual Task<bool> ImportItem(GameItem item, BigGameItem template)
	{
		return Task.FromResult(false);
	}

	public virtual Task<bool> UpdateItem(GameItem item, GameObject go)
	{
		return Task.FromResult(false);
	}

	public virtual Task RemoveItem(GameObject go)
	{
		return Task.CompletedTask;
	}

	public GameObject GetPrefab(string prefabName)
	{
		string[] prefabGuids = AssetDatabase.FindAssets(prefabName);

		if (prefabGuids.Length == 0)
		{
			Debug.LogError("Prefab not found: " + prefabName);
			return null;
		}

		string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[0]);
		return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
	}

	public GameObject GetInstance(string instanceName)
	{
		return GameObject.Find(instanceName);
	}

	protected void HideObjects(params GameObject[] objects)
	{
		foreach (var go in objects)
		{
			if (go == null)
				continue;

			go.SetActive(false);
		}
	}

	protected void DestroyObjects(params GameObject[] objects)
	{
		foreach (var go in objects)
		{
			if (go == null)
				continue;

			GameObject.DestroyImmediate(go);
		}
	}

	public virtual Task Preprocess(IList<PostProcessNode> preprocess)
	{
		return Task.CompletedTask;
	}

	protected BigGame _game;
	protected IEnumerable<IBGModule> _modules;
	protected IBGGameModule _gameModule;
}

public class CharacterDescriptor
{
	public string Name { get; set; }
	public string Gender { get; set; }
	public string Role { get; set; }
	public object Data { get; set; }
}

public class AvatarOptions
{
	public bool IsCustomAvatar { get; set; }
	public string ModuleId { get; set; }
	public object Data { get; set; }
}

public class BaseGameModule : BaseBGModel, IBGGameModule
{
	public T GetBuildValue<T>(string key)
	{
		var buildItem = _game.GameItems.FirstOrDefault(gi => string.IsNullOrWhiteSpace(gi.ModuleId) && gi.Id == "Build");
		if (buildItem != null && buildItem.Values.TryGetValue(key, out object value))
		{
			if (value is JObject)
			{
				return (value as JObject).ToObject<T>();
			}

			return (T)value;
		}

		return default(T);
	}

	private List<string> _scenes = new List<string>() { "MainScene" };
	public void AddScene(string name, bool starter)
	{
		if (starter)
		{
			_scenes.Insert(0, name);
		}
		else
		{
			_scenes.Add(name);
		}
	}

	public virtual string GetAlias()
	{
		return Model.name;
	}

	public override Task Cleanup()
	{
		AddScenes();
		return base.Cleanup();
	}

	private void AddScenes()
	{
		var currentScenes = new List<EditorBuildSettingsScene>();
		foreach (var scene in _scenes)
		{
			var guid = AssetDatabase.FindAssets($"t:Scene {scene}").FirstOrDefault();
			if (!string.IsNullOrEmpty(guid))
			{
				string scenePath = AssetDatabase.GUIDToAssetPath(guid);
				// Check if the scene is already in the list
				bool alreadyAdded = currentScenes.Any(s => s.path == scenePath);
				if (!alreadyAdded)
				{
					EditorBuildSettingsScene newScene = new EditorBuildSettingsScene(scenePath, true);
					currentScenes.Add(newScene);
					Debug.Log("Added scene: " + scenePath);
				}
				else
				{
					Debug.Log("Scene already in Build Settings: " + scenePath);
				}
			}
		}

		EditorBuildSettings.scenes = currentScenes.ToArray();
	}
}

public abstract class BaseCharacterModule : BaseBGModel
{
	protected abstract Task<bool> BuildCharacter(GameObject instance, CharacterDescriptor descriptor, GameItem item, BigGameItem template);

	protected virtual CharacterDescriptor GetDescriptor(JObject data)
	{
		return data?.ToObject<CharacterDescriptor>();
	}

	public override async Task<GameObject> CreateItem(GameItem item, BigGameItem template)
	{
		if (item.Values.TryGetValue("descriptor", out object descriptor))
		{
			var character = GetDescriptor(descriptor as JObject);
			var instance = InstantiatePrefabFor(character, item, template);

			if (instance != null)
			{
				instance.transform.position = item.Position;
				instance.transform.rotation = item.Rotation;
				instance.transform.localScale = item.Scale;

				if (!await BuildCharacter(instance, character, item, template))
					return null;

				return instance;
			}
		}

		var go = new GameObject(item.Id);
		go.transform.position = item.Position;
		go.transform.rotation = item.Rotation;
		go.transform.localScale = item.Scale;

		if (!await BuildCharacter(go, null, item, template))
			return null;

		return go;
	}

	private GameObject InstantiatePrefabFor(CharacterDescriptor descriptor, GameItem character, BigGameItem template)
	{
		var templateName = template.unique
			? $"{_gameModule.GetAlias()}_Avatar_{descriptor.Gender}"
			: $"{_gameModule.GetAlias()}_{descriptor.Role}_{descriptor.Gender}";

		return InstantiateTemplateByName(templateName);
	}

	public static GameObject InstantiateTemplateByName(
			string prefabName,
			Transform parent = null,
			Vector3? worldPosition = null,
			Quaternion? worldRotation = null,
			bool selectAfterCreate = true)
	{
		if (string.IsNullOrWhiteSpace(prefabName))
			throw new ArgumentException("prefabName is null or empty.", nameof(prefabName));

		var path = FindPrefabPathByName(prefabName);
		if (string.IsNullOrEmpty(path))
			throw new InvalidOperationException($"No prefab found named '{prefabName}' under Assets/.");

		var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
		if (!prefab)
			throw new InvalidOperationException($"LoadAssetAtPath failed for '{path}'.");

		GameObject instance = parent
			? PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject
			: PrefabUtility.InstantiatePrefab(prefab) as GameObject;

		if (!instance)
			throw new Exception("PrefabUtility.InstantiatePrefab returned null.");

		Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {prefab.name}");

		instance.transform.position = worldPosition ?? Vector3.zero;
		instance.transform.rotation = worldRotation ?? Quaternion.identity;
		instance.name = prefab.name; // keep clean name (no (Clone))

		if (selectAfterCreate)
			Selection.activeObject = instance;

		return instance;
	}

	public static string FindPrefabPathByName(string prefabName)
	{
		var guids = AssetDatabase.FindAssets($"t:prefab {prefabName}");
		if (guids == null || guids.Length == 0) return null;

		string fallbackPath = null;

		foreach (var guid in guids)
		{
			var path = AssetDatabase.GUIDToAssetPath(guid);
			var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			if (!go) continue;

			if (go.name.Equals(prefabName, StringComparison.Ordinal))
				return path; // exact match

			fallbackPath ??= path;
		}

		return fallbackPath;
	}

	public static GameObject FindPrefabByName(string prefabName)
	{
		var guids = AssetDatabase.FindAssets($"t:prefab {prefabName}");
		if (guids == null || guids.Length == 0) return null;

		foreach (var guid in guids)
		{
			var path = AssetDatabase.GUIDToAssetPath(guid);
			var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			if (!go) continue;

			if (go.name.Equals(prefabName, StringComparison.Ordinal))
				return go; // exact match
		}

		return null;
	}
}


