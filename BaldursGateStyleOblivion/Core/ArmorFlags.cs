using Mutagen.Bethesda.Oblivion;

namespace BaldursGateStyleOblivion.Core;

internal static class ArmorFlags
{
    // Mutagen 0.54.4 exposes and serializes this field as the BMDT flag byte,
    // but EquipmentFlag defines bit positions in the full BMDT word.
    public static bool IsHeavy(IArmorGetter armor) => armor.ClothingFlags is not null
        && ((int)armor.ClothingFlags.GeneralFlags & ((int)EquipmentFlag.HeavyArmor >> 16)) != 0;
}