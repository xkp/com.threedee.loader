using UnityEngine;

public enum BigObjectKind
{
	None,
	GameItem,
	Environment
}

public class BigGameObject : MonoBehaviour
{
	[SerializeField]
	public BigObjectKind Kind { get; set; } = BigObjectKind.None;

	[SerializeField]
	public int Id { get; set; } = -1;
}
