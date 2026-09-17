using Fusion;

public class PlayerEnchant : NetworkBehaviour
{
    public const int MaxEnchants = 8;

    [Networked, Capacity(PlayerEquipment.WeaponSlots * MaxEnchants)]
    public NetworkArray<int> SlotEnchants { get; }

    [Networked, Capacity(PlayerEquipment.WeaponSlots)]
    public NetworkArray<int> EnchantedWeaponIds { get; }

    [Networked] public int Stones { get; set; }
}