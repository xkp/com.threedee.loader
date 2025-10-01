using System.Linq;
using UnityEngine;

public static class QuadFitter
{
	/// <summary>
	/// Creates a PrimitiveType.Quad and fits it to 4 points in WORLD space.
	/// Assumes the 4 points form a planar, rectangle-like quad (no shear).
	/// Returns the created quad and outputs width/height.
	/// </summary>
	public static GameObject CreateQuadFromPoints(
		Vector3[] points,
		out float width,
		out float height,
		string name = "QuadFromPoints",
		Material material = null)
	{
		width = height = 0f;
		if (points == null || points.Length != 4)
		{
			Debug.LogError("CreateQuadFromPoints: need exactly 4 points.");
			return null;
		}

		// --- 1) Get a robust ordering of the 4 points (CCW) on their plane ---
		// Find a stable plane normal (handles slight non-coplanarity).
		Vector3 n = Vector3.zero;
		n += Vector3.Cross(points[1] - points[0], points[2] - points[0]);
		n += Vector3.Cross(points[2] - points[0], points[3] - points[0]);
		if (n.sqrMagnitude < 1e-10f)
		{
			Debug.LogError("CreateQuadFromPoints: degenerate points (nearly colinear).");
			return null;
		}
		n.Normalize();

		// Build a temporary 2D basis on the plane to sort the corners CCW.
		Vector3 tmpRight = Vector3.ProjectOnPlane(Vector3.right, n);
		if (tmpRight.sqrMagnitude < 1e-6f) tmpRight = Vector3.ProjectOnPlane(Vector3.up, n);
		tmpRight.Normalize();
		Vector3 tmpUp = Vector3.Cross(n, tmpRight).normalized;

		Vector2[] uv2D = new Vector2[4];
		for (int i = 0; i < 4; i++)
			uv2D[i] = new Vector2(Vector3.Dot(points[i], tmpRight), Vector3.Dot(points[i], tmpUp));

		Vector2 centroid = (uv2D[0] + uv2D[1] + uv2D[2] + uv2D[3]) * 0.25f;
		int[] order = Enumerable.Range(0, 4)
			.OrderBy(i => Mathf.Atan2(uv2D[i].y - centroid.y, uv2D[i].x - centroid.x))
			.ToArray();

		Vector3 p0 = points[order[0]];
		Vector3 p1 = points[order[1]];
		Vector3 p2 = points[order[2]];
		Vector3 p3 = points[order[3]];

		// --- 2) Derive center, orthonormal width/height directions, and sizes ---
		Vector3 center = (p0 + p1 + p2 + p3) * 0.25f;

		// Raw edge directions around p0
		Vector3 e01 = p1 - p0; // width-ish
		Vector3 e03 = p3 - p0; // height-ish

		// Orthonormal frame on the quad:
		Vector3 normal = Vector3.Normalize(Vector3.Cross(e01, e03));  // forward (+Z of the quad)
																	  // Ensure consistent orientation with our earlier n
		if (Vector3.Dot(normal, n) < 0f) normal = -normal;

		// Make width dir as the normalized projection of e01 onto the plane
		Vector3 widthDir = Vector3.ProjectOnPlane(e01, normal).normalized;
		if (widthDir.sqrMagnitude < 1e-8f) widthDir = tmpRight; // fallback

		// Height dir is orthogonal to width & on plane
		Vector3 heightDir = Vector3.Cross(normal, widthDir).normalized;

		// Project opposite edges onto the axes to estimate side lengths
		float w1 = Mathf.Abs(Vector3.Dot((p1 - p0), widthDir));
		float w2 = Mathf.Abs(Vector3.Dot((p2 - p3), widthDir));
		float h1 = Mathf.Abs(Vector3.Dot((p3 - p0), heightDir));
		float h2 = Mathf.Abs(Vector3.Dot((p2 - p1), heightDir));

		width = 0.5f * (w1 + w2);
		height = 0.5f * (h1 + h2);

		if (width < 1e-6f || height < 1e-6f)
		{
			Debug.LogError("CreateQuadFromPoints: computed near-zero width/height.");
			return null;
		}

		// --- 3) Create Unity's built-in Quad and fit transform ---
		GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
		quad.name = name;

		// Unity's Quad lies in the XY plane with forward = +Z.
		// So set rotation to make +Z = normal and +Y = heightDir (then +X = widthDir)
		Quaternion rot = Quaternion.LookRotation(normal, heightDir);

		quad.transform.SetPositionAndRotation(center, rot);
		quad.transform.localScale = new Vector3(width, height, 1f);

		if (material != null)
		{
			var mr = quad.GetComponent<MeshRenderer>();
			mr.sharedMaterial = material;
		}

		return quad;
	}
}
