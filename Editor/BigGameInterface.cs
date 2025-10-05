using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;


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

	void Init(IEnumerable<IBGModule> modules, BigGame game);
	void ConfigProject();
	void Build();
	void Cleanup();

	bool CreateItem(GameItem item, BigGameItem template, out GameObject go);
	bool UpdateItem(GameItem item, GameObject go);
	void RemoveItem(GameObject go);
}

public interface IBGGameModule
{
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

	public virtual void Init(IEnumerable<IBGModule> modules, BigGame game)
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
	}

	public virtual void ConfigProject()
	{
	}

	public virtual bool CreateItem(GameItem item, BigGameItem template, out GameObject go)
	{
		go = null;
		return false;
	}

	public virtual void Build()
	{
	}

	public virtual void Cleanup()
	{
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

	public virtual bool ImportItem(GameItem item, BigGameItem template)
	{
		return false;
	}

	public virtual bool UpdateItem(GameItem item, GameObject go)
	{
		return false;
	}

	public virtual void RemoveItem(GameObject go)
	{
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

public class BaseGameModule : BaseBGModel, IBGGameModule
{
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
	protected abstract bool BuildCharacter(GameObject instance, CharacterDescriptor descriptor, GameItem item, BigGameItem template);
	
	public override bool CreateItem(GameItem item, BigGameItem template, out GameObject go)
	{
		if (item.Values.TryGetValue("descriptor", out object descriptor))
		{
			var character = (CharacterDescriptor)descriptor;
			var instance = go = InstantiatePrefabFor(character, item, template);

			if (go != null)
			{
				go.transform.position = item.Position;
				go.transform.rotation = item.Rotation;
				go.transform.localScale = item.Scale;

				return BuildCharacter(instance, character, item, template);
			}
		}

		go = null;
		return false;
	}

	private GameObject InstantiatePrefabFor(CharacterDescriptor descriptor, GameItem character, BigGameItem template)
	{
		string gender, role;

		if (template.GetPropertyValue("Gender", out gender))
		{
			var gmodule = _gameModule as IBGModule;
			var templateName = template.unique
				? $"{gmodule.Model.name}_Player_{gender}"
				: $"{gmodule.Model.name}_{descriptor.Role}_{gender}";

			return InstantiateTemplateByName(templateName);
		}

		return null;
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
}


