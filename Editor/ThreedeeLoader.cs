using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Assets.Editor
{
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

	public class ThreedeeLoader
	{
		public static void Load(string inputFolder, string outputFolder) 
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
			CreateSceneNode(envItems, envAssets, null);
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

					importer.SaveAndReimport();
				}

				GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
				if (modelPrefab == null)
				{
					Debug.LogError("Failed to load model asset at path: " + assetPath);
					continue;
				}

				Console.WriteLine($"adding: {assetPath}");
				ReplaceMaterials(modelPrefab);


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

		private static void CreateSceneNode(ThreedeeNode node, Dictionary<string, GameObject> fbxLibrary, Transform parentTransform)
		{
			Console.WriteLine($"CreateNode: {node?.MeshName}, meshes: {fbxLibrary.Count}");

			GameObject instance = InstantiateProp(node.Attributes);
			bool isPrefab = instance != null;
			if (instance == null && node.MeshName != null && fbxLibrary.TryGetValue(GetMeshPath(node.MeshName), out GameObject prefab))
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
					foreach (var child in node.Children)
					{
						CreateSceneNode(child, fbxLibrary, instance.transform);
					}
				}
			}
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

		private static GameObject InstantiateProp(List<NodeAttribute> attributes)
		{
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
					if (originalMaterial == null)
					{
						Debug.LogWarning("Renderer has a null material, skipping.");
						continue;
					}

					Debug.Log($"Material to be replaced: {originalMaterial?.name}");

					string[] materialGuids = AssetDatabase.FindAssets(originalMaterial.name + " t:Material", new string[] { "Assets/Big Game/Materials" });

					Debug.Log($"Found: {materialGuids?.Length}");

					if (materialGuids.Length > 0)
					{
						string materialPath = AssetDatabase.GUIDToAssetPath(materialGuids[0]);
						Material newMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

						if (newMaterial != null)
						{
							Debug.Log($"Replacing material {originalMaterial.name} with {newMaterial.name}.");
							materials[i] = newMaterial;
						}
						else
						{
							Debug.Log($"Material {originalMaterial.name} found, but could not be loaded at path: {materialPath}.");
						}
					}
					else
					{
						Debug.Log($"No matching material found for {originalMaterial.name}.");
					}
				}

				renderer.sharedMaterials = materials;
			}

			Debug.Log("Material replacement completed for: " + meshObject.name);
		}
	}
}

