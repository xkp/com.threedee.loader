using UnityEditor;
using UnityEngine;

public class MaterialRemapper : AssetPostprocessor
{
	static readonly string MaterialLibraryPath = "Assets/Big Game/Materials/"; // Path where your real materials live

	Material OnAssignMaterialModel(Material importedMaterial, Renderer renderer)
	{
		string materialName = importedMaterial.name;
		string searchPath = MaterialLibraryPath + materialName + ".mat";

		Debug.Log($"Loading material: '{materialName}'");

		Material officialMaterial = AssetDatabase.LoadAssetAtPath<Material>(searchPath);

		if (officialMaterial != null)
		{
			Debug.Log($"Replacing material '{materialName}' with official material '{officialMaterial.name}'");
			return officialMaterial;
		}
		else
		{
			Debug.LogWarning($"No matching official material found for '{materialName}'. Using imported one.");
			return importedMaterial; // fallback
		}
	}
}
