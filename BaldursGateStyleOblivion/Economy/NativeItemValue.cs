using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Records;

namespace BaldursGateStyleOblivion.Economy;

// Base record values exclude these engine additions. Keep unresolved costs explicit.
public static class NativeItemValue
{
    public static NativePrice? Read(IMajorRecordGetter item, IEnchantmentGetter? enchantment, IReadOnlyDictionary<string, IMagicEffectGetter> definitions, IReadOnlyDictionary<string, double>? settings = null)
    {
        if (enchantment is null) return null;
        double Setting(string id, double fallback) => settings?.GetValueOrDefault(id, fallback) ?? fallback;
        NativePrice Unknown(string reason) => new(0, false, false, reason);
        if (enchantment.Data is null) return Unknown("Missing enchantment data.");
        if (item is IArmorGetter or IClothingGetter)
        {
            double addition = 0;
            foreach (var effect in enchantment.Effects)
            {
                var data = effect.Data;
                if (data is null || effect.ScriptEffect is not null) return Unknown("Scripted or missing effect; inspect in game.");
                var definition = definitions.GetValueOrDefault(data.MagicEffect.ToString() ?? "");
                if (definition?.Data?.SubData is null || definition.FormKey.ModKey.FileName.String != "Oblivion.esm")
                    return Unknown("Custom effect requires verification before changing its price.");
                // Oblivion.exe 1.2.0416: inventory valuation at 0x488E50.
                var magnitude = definition.Data.Flags.HasFlag(MagicEffect.MagicFlag.NoMagnitude)
                    ? Math.Truncate(Setting("fMagicCEEnchantMagOffset", 5)) : data.Magnitude;
                addition += magnitude * definition.Data.SubData.ConstantEffectBarterFactor;
            }
            return new(addition, true);
        }
        if (!enchantment.Data.Flags.HasFlag(Enchantment.Flag.ManualEnchantCost))
            return Unknown("Auto-calculated enchantment cost needs engine verification.");
        var cost = enchantment.Data.EnchantCost;
        return item switch
        {
            IWeaponGetter weapon => new(Setting("fEnchantmentPointsMult", .4) * (weapon.EnchantmentPoints ?? 0) + Setting("fEnchantmentEffectPointsMult", .4) * cost, false),
            IAmmunitionGetter ammo => new(Setting("fEnchantmentPointsMult", .4) * (ammo.EnchantmentPoints ?? 0) + Setting("fEnchantmentEffectPointsMult", .4) * cost, false),
            IBookGetter => new(cost * Setting("fScrollCostMult", .5), false, ZeroBaseSuppresses: false),
            _ => Unknown("Unsupported enchanted item type.")
        };
    }
}
