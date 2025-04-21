using System;
using System.Collections.Generic;
using UnityEngine;

public class GameObjectIndex : ScriptableObject
{
	public Dictionary<string, GameObject> indexedObjects;

	public void Add(GameItem item, GameObject go)
	{
		indexedObjects[item.Id.ToString()] = go;
	}

	public bool TryGetValue(GameItem item, out GameObject go)
	{
		return indexedObjects.TryGetValue(item.Id.ToString(), out go);
	}
}
