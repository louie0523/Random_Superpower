using UnityEngine;

[CreateAssetMenu(menuName = "Game/Shop Database")]
public class ShopDatabase : ScriptableObject
{
    [Tooltip("순서가 네트워크 인덱스입니다. 중간 삽입·삭제 금지")]
    public ShopItemData[] Items;

    public int Count => Items?.Length ?? 0;

    public ShopItemData GetByIndex(int i) =>
        (Items != null && i >= 0 && i < Items.Length) ? Items[i] : null;
}