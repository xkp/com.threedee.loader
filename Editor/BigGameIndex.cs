using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameObjectIndex : ScriptableObject
{
	[System.Serializable]
	private struct Entry
	{
		public string key;
		public GameObject value;
	}

	[SerializeField]
	private List<Entry> _entries = new List<Entry>();

	private Dictionary<string, GameObject> _dict = new Dictionary<string, GameObject>();
	public IReadOnlyDictionary<string, GameObject> Entries => _dict;
	public void Add(GameItem item, GameObject prefab)
	{
		var key = item.Id.ToString();
		if (string.IsNullOrEmpty(key)) 
			return;

		_dict[key] = prefab;
	}

	public GameObject Get(string key)
	{
		_dict.TryGetValue(key, out var prefab);
		return prefab;
	}

	public bool TryGetValue(GameItem item, out GameObject value)
	{
		return _dict.TryGetValue(item.Id.ToString(), out value);
	}

	public void OnBeforeSerialize()
	{
		_entries.Clear();
		foreach (var kvp in _dict)
			_entries.Add(new Entry { key = kvp.Key, value = kvp.Value });
	}

	// Called *after* Unity loads the asset from disk
	public void OnAfterDeserialize()
	{
		_dict = _entries
			.Where(e => !string.IsNullOrEmpty(e.key) && e.value != null)
			.ToDictionary(e => e.key, e => e.value);
	}
}
