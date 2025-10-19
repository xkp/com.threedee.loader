using Microsoft.SqlServer.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public class ThreedeeNode
{
	public string MeshName { get; set; }
	public TransformData Transform { get; set; }
	public List<NodeAttribute> Attributes { get; set; }
	public List<ThreedeeNode> Children { get; set; }
}

public class TransformData
{
	public Vector3 Position { get; set; }
	public Quaternion Rotation { get; set; }
	public Vector3 Scale { get; set; }
}

public class NodeAttribute
{
	public string Name { get; set; }
	public string Value { get; set; }
}

public struct PostProcessNode
{
	public GameObject GameObject { get; set; }
	public IList<NodeAttribute> Attributes { get; set; }
}

public class ThreedeeLoader
{
	public static void Load(string inputFolder, string outputFolder, IList<PostProcessNode> postProcess)
	{
		Console.WriteLine("Loading environment assets...");
		Dictionary<string, GameObject> envAssets = LoadEnvironmentAssets(inputFolder, outputFolder);

		if (envAssets.Count == 0)
		{
			Debug.LogError("No valid FBX files found in the input folder.");
			return;
		}

		Console.WriteLine("Building environment...");
		ThreedeeNode envItems = LoadGameItems(inputFolder);

		// Create the threedee scene
		CreateSceneNode(envItems, envAssets, null, postProcess);
	}

	public static ThreedeeNode LoadScene(string jsonContent)
	{
		var result = new ThreedeeNode();
		try
		{
			// Parse the JSON into a JToken for manual processing
			JObject jsonObject = JObject.Parse(jsonContent);

			// Access the "root" array
			JArray rootArray = jsonObject["root"] as JArray;

			if (rootArray == null)
			{
				Debug.LogError("No 'root' array found in the JSON.");
				return null;
			}

			result.Children = ReadChildren(rootArray);
		}
		catch (Exception ex)
		{
			Debug.LogError("Error reading JSON: " + ex.Message);
			return null;
		}

		return result;
	}

	private static List<ThreedeeNode> ReadChildren(JArray children)
	{
		var result = new List<ThreedeeNode>();

		if (children != null)
		{
			// Process each node in the "root" array
			foreach (JObject child in children)
			{
				ThreedeeNode childNode = CreateNode(child);
				if (childNode != null)
				{
					result.Add(childNode);

					childNode.Children = ReadChildren(child["children"] as JArray);
				}
			}
		}

		return result;
	}

	private static ThreedeeNode CreateNode(JObject child)
	{
		// Extract the "mesh" property
		string mesh = child["mesh"]?.ToString();
		if (string.IsNullOrEmpty(mesh))
		{
			return null;
		}

		// Process the "attrs" array, if it exists
		var attributes = new List<NodeAttribute>();
		var attrsArray = child["attrs"] as JObject;
		if (attrsArray != null)
		{
			foreach (JProperty attr in attrsArray.Properties())
			{
				//read the sheet
				JObject sheet = attr.Value as JObject;
				if (sheet == null)
					continue;

				foreach (var property in sheet.Properties())
				{
					Debug.Log($"found attr: {property.Name} = {property.Value}");
					attributes.Add(new NodeAttribute
					{
						Name = property.Name,
						Value = property.Value.ToString()
					});
				}
			}
		}

		//Parse the transform
		float tx = float.Parse(child["tx"]?.ToString() ?? "0");
		float ty = float.Parse(child["ty"]?.ToString() ?? "0");
		float tz = float.Parse(child["tz"]?.ToString() ?? "0");

		float rx = float.Parse(child["rx"]?.ToString() ?? "0");
		float ry = float.Parse(child["ry"]?.ToString() ?? "0");
		float rz = float.Parse(child["rz"]?.ToString() ?? "0");
		float rw = float.Parse(child["rw"]?.ToString() ?? "1");

		float sx = float.Parse(child["sx"]?.ToString() ?? "1");
		float sy = float.Parse(child["sy"]?.ToString() ?? "1");
		float sz = float.Parse(child["sz"]?.ToString() ?? "1");

		return new ThreedeeNode
		{
			MeshName = mesh,
			Attributes = attributes,
			Transform = new TransformData
			{
				Position = new Vector3(tx, ty, tz),
				Rotation = new Quaternion(rx, ry, rz, rw),
				Scale = new Vector3(sx, sy, sz),
			}
		};
	}

	private static ThreedeeNode LoadNode(JObject jsonObject)
	{
		var result = new ThreedeeNode();
		try
		{
			// Access the "root" array
			JArray rootArray = jsonObject["root"] as JArray;

			if (rootArray == null)
			{
				Debug.LogError("No 'root' array found in the JSON.");
				return null;
			}

			// Process each node in the "root" array
			foreach (JObject rootNode in rootArray)
			{
				// Extract the "mesh" property
				string mesh = rootNode["mesh"]?.ToString();
				Debug.Log($"Root Node Mesh: {mesh}");

				// Process the "children" array, if it exists
				JArray childrenArray = rootNode["children"] as JArray;
				if (childrenArray != null)
				{
					foreach (JObject childNode in childrenArray)
					{
						string childMesh = childNode["mesh"]?.ToString();
						float tx = float.Parse(childNode["tx"]?.ToString() ?? "0");
						float ty = float.Parse(childNode["ty"]?.ToString() ?? "0");
						float tz = float.Parse(childNode["tz"]?.ToString() ?? "0");

						Debug.Log($"  Child Mesh: {childMesh}, Position: ({tx}, {ty}, {tz})");
					}
				}
				else
				{
					Debug.LogWarning("No children found for this node.");
				}

				// Process the "attrs" array, if it exists
				JArray attrsArray = rootNode["attrs"] as JArray;
				if (attrsArray != null)
				{
					foreach (JObject attr in attrsArray)
					{
						Debug.Log("  Attributes:");
						foreach (var property in attr.Properties())
						{
							Debug.Log($"    Name: {property.Name}, Value: {property.Value}");
						}
					}
				}
				else
				{
					Debug.LogWarning("No attributes found for this node.");
				}


			}
		}
		catch (Exception ex)
		{
			Debug.LogError("Error reading JSON: " + ex.Message);
			return null;
		}

		return result;
	}
	private static Dictionary<string, GameObject> LoadEnvironmentAssets(string inputFolder, string outputFolder)
	{
		var result = new Dictionary<string, GameObject>();

		// Load FBX files
		string[] fbxFiles = Directory.GetFiles(inputFolder, "*.fbx");

		string meshFolder = Path.Combine(outputFolder, "Assets", "Big Game", "Meshes");
		Directory.CreateDirectory(meshFolder);
		foreach (string fbxPath in fbxFiles)
		{
			var filename = Path.Combine(meshFolder, Path.GetFileName(fbxPath));
			File.Copy(fbxPath, filename, true);
		}

		foreach (string fbxPath in fbxFiles)
		{
			string assetPath = Path.Combine("Assets", "Big Game", "Meshes", Path.GetFileName(fbxPath));

			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
			ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;

			if (importer != null)
			{
				importer.addCollider = true;
				importer.generateSecondaryUV = true;
				importer.importNormals = ModelImporterNormals.Calculate;
				importer.normalSmoothingAngle = 0;
				importer.preserveHierarchy = true;

				importer.SaveAndReimport();
			}

			GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
			if (modelPrefab == null)
			{
				Debug.LogError("Failed to load model asset at path: " + assetPath);
				continue;
			}

			Console.WriteLine($"adding: {assetPath}");
			//ReplaceMaterials(modelPrefab);


			result[assetPath] = modelPrefab;
		}

		return result;
	}

	private static ThreedeeNode LoadGameItems(string inputFolder)
	{
		// Load JSON file for Threedee Scene
		string[] jsonFiles = Directory.GetFiles(inputFolder, "*.json");
		if (jsonFiles.Length == 0)
		{
			Debug.LogError("No JSON file found in the input folder.");
			return null;
		}

		string jsonContent = File.ReadAllText(jsonFiles[0]);
		return LoadScene(jsonContent);
	}

	private static void ForGameObjectAndChidren(GameObject go, Action<GameObject> visitor)
	{
		visitor(go);
		for (var i = 0; i < go.transform.childCount; i++)
		{
			var child = go.transform.GetChild(i);
			ForGameObjectAndChidren(child.gameObject, visitor);
		}
	}

	private static void CreateSceneNode(ThreedeeNode node, Dictionary<string, GameObject> fbxLibrary, Transform parentTransform, IList<PostProcessNode> postProcess)
	{
		Console.WriteLine($"CreateNode: {node?.MeshName}, meshes: {fbxLibrary.Count}");

		GameObject instance = InstantiateProp(node);
		bool isPrefab = instance != null;
		GameObject prefab = null;
		bool isMesh = !string.IsNullOrEmpty(node.MeshName) && fbxLibrary.TryGetValue(GetMeshPath(node.MeshName), out prefab);

		if (instance == null && node.MeshName != null && isMesh)
		{
			Console.WriteLine($"found prefab to instantiate: {node.MeshName}");
			instance = PrefabUtility.InstantiatePrefab(prefab, parentTransform) as GameObject;

			//set defaults
			ForGameObjectAndChidren(instance, i =>
			{
				i.isStatic = true;

				//lightmaps
				GameObjectUtility.SetStaticEditorFlags(i, StaticEditorFlags.ContributeGI);
				var renderer = instance.GetComponent<MeshRenderer>();
				if (renderer != null)
				{
					renderer.receiveGI = ReceiveGI.Lightmaps;
				}
			});
		}

		if (instance == null && parentTransform == null)
		{
			instance = new GameObject("Environment");
			instance.transform.position = Vector3.zero;
			instance.transform.localScale = new Vector3(-1, 1, 1);
			instance.transform.parent = parentTransform;
		}

		if (instance != null)
		{
			if (node.Transform != null)
			{
				instance.transform.position = node.Transform.Position;
				instance.transform.rotation = node.Transform.Rotation;

				if (!isPrefab) //td: avoid original scale
					instance.transform.localScale = node.Transform.Scale;
			}

			if (parentTransform != null)
			{
				instance.transform.parent = parentTransform;
			}

			if (node.Attributes != null)
			{
				foreach (var attribute in node.Attributes)
				{
					ApplyAttribute(instance, attribute);
				}
			}

			if (node.Children != null)
			{
				var children = new List<ThreedeeNode>();
				foreach (var child in node.Children)
				{
					if (child.Attributes?.Count > 0)
					{
						Debug.Log($"Processing: {child.MeshName}");

						fbxLibrary.TryGetValue(GetMeshPath(child.MeshName), out GameObject attrPrefab);
						if (attrPrefab != null)
						{
							var attrGeom = ProcessAtributeGeometry(child, instance.transform, attrPrefab, postProcess, out bool removeGeometry);
							if (attrGeom != null && removeGeometry)
							{
								GameObject.DestroyImmediate(attrGeom, true);
							}
						}
					}
					else
					{
						children.Add(child);
					}
				}

				foreach (var child in children)
				{
					CreateSceneNode(child, fbxLibrary, instance.transform, postProcess);
				}
			}
		}
	}

	private static GameObject ProcessAtributeGeometry(ThreedeeNode child, Transform parent, GameObject prefab, IList<PostProcessNode> postprocess, out bool removeGeometry)
	{
		removeGeometry = false;

		//find the submesh 
		var t = prefab.transform.Find(Path.GetFileNameWithoutExtension(child.MeshName));
		if (t != null)
		{
			var mesh = t.GetComponent<MeshFilter>()?.sharedMesh;
			if (mesh != null)
			{
				if (isQuadLight(child, out float intensity))
				{
					var lightsObject = new GameObject("QuadLights");
					lightsObject.transform.parent = parent;
					lightsObject.transform.localScale = new Vector3(1, 1, 1);
					lightsObject.transform.rotation = new Quaternion();

					AddQuadLights(mesh, intensity, lightsObject.transform);
					removeGeometry = true;
				}

				if (isSurface(child))
				{
					removeGeometry = false;
					postprocess.Add(new PostProcessNode { GameObject = t.gameObject, Attributes = child.Attributes });
				}
			}

			return t.gameObject;
		}

		return null;
	}

	private static bool isQuadLight(ThreedeeNode child, out float intensity)
	{
		intensity = 10.0f;
		var attr = child.Attributes?.FirstOrDefault(a => a.Name == "translucent"); //TODO: add attributes 
		return attr != null;
	}

	private static bool isSurface(ThreedeeNode child)
	{
		var attr = child.Attributes?.FirstOrDefault(a => a.Name == "surface");
		if (attr != null)
		{
			return true;
		}

		return false;
	}

	public static Vector3 CalculateNormal(Vector3 p1, Vector3 p2, Vector3 p3)
	{
		Vector3 edge1 = p2 - p1;
		Vector3 edge2 = p3 - p1;
		return Vector3.Normalize(Vector3.Cross(edge1, edge2));
	}

	private static Vector3 TransformVertex(Vector3 v, Transform parent)
	{
		if (parent == null)
			return v;
		return parent.TransformPoint(v);
	}

	private static void AddQuadLights(Mesh mesh, float intensity, Transform parent)
	{
		Vector3[] vertices = mesh.vertices;
		int[] triangles = mesh.triangles;

		var used = new HashSet<int>();

		for (int i = 0; i < triangles.Length; i += 3)
		{
			if (used.Contains(i)) continue;

			int i0 = triangles[i];
			int i1 = triangles[i + 1];
			int i2 = triangles[i + 2];

			var v1 = TransformVertex(vertices[i0], parent);
			var v2 = TransformVertex(vertices[i1], parent);
			var v3 = TransformVertex(vertices[i2], parent);

			int[] tri1 = { i0, i1, i2 };

			// Try to find a matching triangle
			for (int j = i + 3; j < triangles.Length; j += 3)
			{
				if (used.Contains(j)) continue;

				int j0 = triangles[j];
				int j1 = triangles[j + 1];
				int j2 = triangles[j + 2];

				int[] tri2 = { j0, j1, j2 };

				// find a shared edge
				for (int t1 = 0; t1 < 3; t1++)
				{
					int e1 = tri1[t1];
					int e2 = tri1[(t1 + 1) % 3];

					bool found = false;
					for (int t2 = 0; t2 < 3; t2++)
					{
						int he1 = tri2[t2];
						int he2 = tri2[(t2 + 1) % 3];

						if (he1 == e2 && he2 == e1)
						{
							//found 
							used.Add(i);
							used.Add(j);

							int e3 = tri1[(t1 + 2) % 3];
							int he3 = tri2[(t2 + 2) % 3];
							var quadVerts = new Vector3[4]
							{
								vertices[e2],
								vertices[e3],
								vertices[e1],
								vertices[he3],
							};


							var n = CalculateNormal(v1, v2, v3);
							CreateAreaLight(quadVerts, parent, n);
							found = true;
							break;
						}
					}

					if (found)
						break;
				}
			}
		}
	}

	private static void CreateAreaLight(Vector3[] quad, Transform parent, Vector3 normal)
	{
		Vector3 center = (quad[0] + quad[1] + quad[2] + quad[3]) / 4f;
		float width = Vector3.Distance(quad[0], quad[1]);
		float height = Vector3.Distance(quad[1], quad[2]);
		Vector3 right = (quad[1] - quad[0]).normalized;

		GameObject lightObj = new GameObject("Quad Area Light");
		lightObj.transform.SetParent(parent);
		lightObj.transform.position = parent.TransformPoint(center);
		lightObj.transform.localScale = new Vector3(1, 1, 1);
		lightObj.transform.rotation = Quaternion.LookRotation(normal, right);

#if UNITY_EDITOR
		var light = lightObj.AddComponent<Light>();
		light.type = LightType.Rectangle;
		light.areaSize = new Vector2(height, width);
		light.intensity = 3f;
		light.color = Color.white;
		light.lightmapBakeType = LightmapBakeType.Baked;
#endif
	}

	private static string GetMeshPath(string mesh)
	{
		return Path.Combine("Assets", "Big Game", "Meshes", mesh);
	}

	private static void ApplyAttribute(GameObject instance, NodeAttribute attribute)
	{
		switch (attribute.Name)
		{
			//td: register handlers
			case "do not collide":
				if (bool.TryParse(attribute.Value, out bool disabled))
				{
					if (disabled)
					{
						ForGameObjectAndChidren(instance, i =>
						{
							var collider = instance.GetComponent<MeshCollider>();
							if (collider != null)
								collider.enabled = false;
						});
					}
				}
				else
				{
					Debug.LogWarning("Unknown attribute value: " + attribute.Value);
				}
				break;
			case "excluded from lightmaps":
				if (bool.TryParse(attribute.Value, out bool enabled))
				{
					if (enabled)
					{
						ForGameObjectAndChidren(instance, i =>
						{
							Debug.Log($"turning lightmaps off for: {i.name}");
							StaticEditorFlags currentFlags = GameObjectUtility.GetStaticEditorFlags(i);
							currentFlags &= ~StaticEditorFlags.ContributeGI;
							GameObjectUtility.SetStaticEditorFlags(i, currentFlags);
						});
					}
				}
				else
				{
					Debug.LogWarning("Unknown attribute value: " + attribute.Value);
				}
				break;
			case "prefab":
				break;
			default:
				Debug.LogWarning("Unknown attribute: " + attribute.Name);
				break;
		}
	}

	private static GameObject InstantiateProp(ThreedeeNode node)
	{
		List<NodeAttribute> attributes = node.Attributes;
		if (attributes != null)
		{
			var attr = attributes.Where(a => a.Name == "prefab").FirstOrDefault();
			if (attr != null)
				return InstantiatePrefab(attr.Value);
		}

		return null;
	}

	public static GameObject InstantiatePrefab(string prefabName)
	{
		string[] prefabGuids = AssetDatabase.FindAssets(prefabName + " t:Prefab");

		if (prefabGuids.Length == 0)
		{
			Debug.LogError("Prefab not found: " + prefabName);
			return null;
		}

		string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[0]);
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

		if (prefab == null)
		{
			Debug.LogError("Failed to load prefab at path: " + prefabPath);
			return null;
		}

		return PrefabUtility.InstantiatePrefab(prefab) as GameObject;
	}

	public static Material FindAssetByExactName(string assetName, string folder)
	{
		string assetPath = $"{folder}/{assetName}.mat".ToLower();
		string[] guids = AssetDatabase.FindAssets(assetName + " t:Material");

		foreach (string guid in guids)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid).ToLower();

			if (path == assetPath)
			{
				Debug.LogError("AssetDatabase.LoadAssetAtPath: " + path);
				return AssetDatabase.LoadAssetAtPath<Material>(path);
			}
		}

		return null; // No perfect match
	}

	public static void ReplaceMaterials(GameObject meshObject)
	{
		if (meshObject == null)
		{
			Debug.LogError("Mesh object is null.");
			return;
		}

		Renderer[] renderers = meshObject.GetComponentsInChildren<Renderer>();

		foreach (Renderer renderer in renderers)
		{
			Material[] materials = renderer.sharedMaterials;

			for (int i = 0; i < materials.Length; i++)
			{
				Material originalMaterial = materials[i];
				if (originalMaterial == null || string.IsNullOrEmpty(originalMaterial.name))
				{
					Debug.LogWarning("Renderer has a null material, skipping.");
					continue;
				}

				Debug.Log($"Material to be replaced: {originalMaterial?.name}");

				Material newMaterial = FindAssetByExactName(originalMaterial.name, "Assets/Big Game/Materials");
				if (newMaterial != null)
				{
					Debug.Log($"Replacing material {originalMaterial.name} with {newMaterial.name}.");
					materials[i] = newMaterial;
				}
				else
				{
					Debug.Log($"Material {originalMaterial.name} not found.");
				}
			}

			renderer.sharedMaterials = materials;
		}

		EditorUtility.SetDirty(meshObject);
		Debug.Log("Material replacement completed for: " + meshObject.name);
	}
}
