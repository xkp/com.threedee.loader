﻿using System;
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
	public BigGameCharacter Character { get; set; }

	public List<GameItem> GameItems { get; set; } = new List<GameItem>();

	public BigGameModule GetModule(string module)
	{
		return Modules.FirstOrDefault(m => m.name == module);
	}

	public BigGameModule GetModule(int id)
	{
		return Modules.FirstOrDefault(m => m.id == id);
	}

	public BigGameItem GetTemplate(GameItem item)
	{
		var module = GetModule(item.ModuleId);
		return module?.GetTemplate(item.TemplateId);
	}
}

public class BigGameCharacter
{
	public int Id { get; set; }
	public string Name { get; set; }
	public string Module { get; set; }
	public object Parameters { get; set; }
	public Vector3 Position { get; set; }
	public Quaternion Rotation { get; set; }
	public Vector3 Scale { get; set; }
}

public class BigGameModule
{
	public int id { get; set; }
	public string name { get; set; }
	public string type { get; set; }
	public string controller { get; set; }
	public List<BigGameItemGroup> itemGroups { get; set; }
	public List<BigGameItem> userTemplates { get; set; } = new List<BigGameItem>();

	public BigGameItem GetTemplate(int id)
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

public class BigGameItem
{
	public int id { get; set; }
	public string name { get; set; }
	public string description { get; set; }
	public string icon { get; set; }
	public string icon3d { get; set; }
	public string prefab { get; set; }
	public bool unique { get; set; }
	public bool notDraggable { get; set; }
	public bool template { get; set; }
	public Dictionary<string, object> Properties { get; set; }
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
	public int Id { get; set; }
	public string Name { get; set; }
	public int ModuleId { get; set; }
	public int TemplateId { get; set; }
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
	void Build();
	void Init(IEnumerable<IBGModule> modules, BigGame game);
	BigGameItem GetTemplateItem(int id);
	bool ImportItem(GameItem item, BigGameItem template);
	void Cleanup();
}

public interface IBGGameModule
{
	//access to character prefabs
	GameObject GetCharacter(string module, int gender, GameItem item, BigGame game);
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
	}

	public virtual void Build()
	{
	}

	public virtual void Cleanup()
	{
	}

	public BigGameItem GetTemplateItem(int id)
	{
		if (Model == null)
			return null;

		foreach (var group in Model.itemGroups)
		{

			var result = group.items.FirstOrDefault(x => x.id == id);
			if (result != null)
				return result;
		}

		return null;
	}

	public virtual bool ImportItem(GameItem item, BigGameItem template)
	{
		return false;
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

	protected void DestroyObjects(params GameObject[] objects)
	{
		foreach (var go in objects)
		{
			if (go == null)
				continue;

			GameObject.DestroyImmediate(go);
		}
	}

}


