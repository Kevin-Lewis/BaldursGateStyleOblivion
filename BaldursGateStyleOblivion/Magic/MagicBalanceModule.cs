using System.Text.Json;
using BaldursGateStyleOblivion.Combat;
using BaldursGateStyleOblivion.Core;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Magic;

internal static class MagicBalanceModule
{
    internal static string PathFor(PatcherRun run)
    {
        var path=string.IsNullOrWhiteSpace(run.Settings.MagicConfigurationFile)?Path.Combine(run.DataDirectory,"magic.json"):Path.GetFullPath(run.Settings.MagicConfigurationFile,run.DataDirectory);
        if(!File.Exists(path)&&string.IsNullOrWhiteSpace(run.Settings.MagicConfigurationFile))path=Path.Combine(AppContext.BaseDirectory,"magic.json");
        return path;
    }
    internal static Spell Create(IOblivionMod patch,ISpellGetter template,KitSpell spell)
    {
        // Keep learned representative spells stable across configuration and kit changes.
        var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(spell.Key));
        var id=0xF00000u|(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(hash)&0xFFFFFu);
        var key=new FormKey(patch.ModKey,id);
        if(patch.Spells.ContainsKey(key))throw new InvalidDataException($"Generated spell ID collision for {spell.Key}.");
        var target=new Spell(key,OblivionRelease.Oblivion){Data=new SpellData{Type=template.Data!.Type}};
        patch.Spells.Add(target);
        target.EditorID="BGSOMagic_"+spell.Key.Replace(" ","");target.Name=$"{spell.Rank} {spell.Slot}";
        target.Data!.Flag=Spell.SpellFlag.ManualSpellCost;target.Data.Cost=(uint)Math.Round(spell.BaseCost);
        target.Data.Level=spell.RequiredSkill switch{>=100=>Spell.SpellLevel.Master,>=75=>Spell.SpellLevel.Expert,>=50=>Spell.SpellLevel.Journeyman,>=25=>Spell.SpellLevel.Apprentice,_=>Spell.SpellLevel.Novice};
        var effect=template.Effects.First(e=>e.Data?.MagicEffect.ToString()==spell.Code&&e.ScriptEffect is null).DeepCopy();
        effect.Data!.Magnitude=(uint)Math.Round(spell.Magnitude);effect.Data.Duration=(uint)Math.Round(spell.Duration);effect.Data.Area=0;
        effect.Data.Type=Enum.Parse<Effect.EffectType>(spell.Range);
        target.Effects.Clear();target.Effects.Add(effect);return target;
    }
    internal static bool Replaceable(FormKey key,Dictionary<FormKey,IMajorRecordGetter> records,HashSet<FormKey>? path=null)
    {
        path??=[];if(!path.Add(key)||path.Count>32)return false;
        if(records.GetValueOrDefault(key) is ISpellGetter spell)
            return spell.Data?.Type.ToString()=="Spell"&&spell.Effects.Count>0&&spell.Effects.All(e=>e.ScriptEffect is null&&e.Data is {} data&&MagicAnalysis.Family(data.MagicEffect.ToString()??"") is "Damage" or "Healing" or "Defense" or "Summon" or "Control" or "Bound equipment");
        if(records.GetValueOrDefault(key) is ILeveledSpellGetter list)
            return list.Entries is {Count:>0}&&list.Entries.All(e=>Replaceable(e.Reference.FormKey,records,new(path)));
        return false;
    }
    public static Dictionary<FormKey,string[]> Run(IPatcherState<IOblivionMod,IOblivionModGetter> state,PatcherRun run)
    {
        var configPath=PathFor(run);var settings=MagicConfiguration.Load(configPath);
        var write=settings.Enabled&&run.Settings.EnableMagicBalance&&!run.Settings.ReportOnly;
        var records=new Dictionary<FormKey,IMajorRecordGetter>();
        foreach(var listing in state.LoadOrder.PriorityOrder.Where(l=>l.Enabled&&l.Mod is not null&&run.IsInputPlugin(l.ModKey)))
            foreach(var record in listing.Mod!.EnumerateMajorRecords())records.TryAdd(record.FormKey,record);
        foreach(var record in state.PatchMod.EnumerateMajorRecords())records[record.FormKey]=record;
        var gameSettings=MagicAnalysisModule.Settings(records).ToDictionary();var variants=CasterKits.Spells(settings);
        var templates=new Dictionary<string,ISpellGetter>();
        foreach(var code in variants.Select(s=>s.Code).Distinct())
        {
            var template=records.Values.OfType<ISpellGetter>().Where(s=>!s.IsDeleted&&s.Data?.Type.ToString()=="Spell"&&s.FormKey.ModKey.ToString()=="Oblivion.esm"&&s.Effects.Any(e=>e.ScriptEffect is null&&e.Data?.MagicEffect.ToString()==code)).OrderBy(s=>s.FormKey.ID).FirstOrDefault();
            if(template is null)throw new InvalidDataException($"No native spell template for {code}.");
            templates[code]=template;
        }
        var generated=new Dictionary<string,FormKey>();var changes=new Dictionary<FormKey,string[]>();var plans=new List<object>();var skipped=new List<object>();
        var engineChanges=new List<object>();
        foreach(var pair in settings.GameSettings)
        {
            var source=records.Values.OfType<IGameSettingGetter>().FirstOrDefault(g=>g.EditorID==pair.Key&&!g.IsDeleted);
            if(source is not null&&source is not IGameSettingFloatGetter)throw new InvalidDataException("Incorrect native type for "+pair.Key);
            var before=(source as IGameSettingFloatGetter)?.Data;
            engineChanges.Add(new{EditorID=pair.Key,Before=before,After=pair.Value});
            gameSettings[pair.Key]=pair.Value;
            if(!write||before.HasValue&&Math.Abs(before.Value-pair.Value)<.000001)continue;
            var target=source is null?state.PatchMod.GameSettings.AddNewFloat():(GameSettingFloat)state.PatchMod.GameSettings.GetOrAddAsOverride(source);
            target.EditorID=pair.Key;target.Data=(float)pair.Value;records[target.FormKey]=target;changes[target.FormKey]=["Data"];
        }
        FormKey SpellKey(KitSpell spell)
        {
            if(generated.TryGetValue(spell.Key,out var key))return key;
            var target=Create(state.PatchMod,templates[spell.Code],spell);records[target.FormKey]=target;generated[spell.Key]=target.FormKey;changes[target.FormKey]=["New representative combat spell"];return target.FormKey;
        }
        var allowed=new Dictionary<string,JsonElement>();
        if(run.Settings.EnablePhysicalCombatBalance&&File.Exists(run.ReportPath(".combat-gameplay.json")))
        {
            using var physical=JsonDocument.Parse(File.ReadAllText(run.ReportPath(".combat-gameplay.json")));
            foreach(var row in physical.RootElement.GetProperty("Plans").EnumerateArray().Where(p=>p.GetProperty("Kind").GetString()=="NPC"))allowed[row.GetProperty("FormKey").GetString()!]=row.Clone();
        }
        foreach(var npc in records.Values.OfType<INpcGetter>().OrderBy(n=>n.FormKey.ToString(),StringComparer.Ordinal).ToArray())
        {
            var key=npc.FormKey.ToString();var vendor=npc.AIData?.BuySellServices.ToString().Contains("Spells")==true;
            if(npc.IsDeleted||!run.Includes(npc.FormKey.ModKey)||npc.EditorID?.StartsWith("Test",StringComparison.OrdinalIgnoreCase)==true||npc.Stats is null||npc.Configuration is null||!allowed.TryGetValue(key,out var planned))continue;
            var build=planned.GetProperty("Build").GetString()!;
            settings.ActorOverrides.TryGetValue(key,out var individual);
            var classId=(records.GetValueOrDefault(npc.Class.FormKey) as IClassGetter)?.EditorID??"";
            var profile=CasterKits.Profile(settings,build,classId,key);
            if(profile is null||individual?.Preserve==true)continue;
            if(vendor&&!settings.AddToSpellVendors){skipped.Add(new{FormKey=key,Reason="Spell vendor additions disabled"});continue;}
            var stats=planned.GetProperty("Stats").Deserialize<Dictionary<string,double>>()!;
            var pool=Math.Round(individual?.Magicka??stats["Intelligence"]*settings.NPCMagickaPerIntelligence);
            var tier=planned.GetProperty("Tier").GetInt32();
            var kit=CasterKits.Select(settings,profile,stats,pool,tier,gameSettings,!vendor&&individual?.AllowRareSpells==true,variants);
            var removed=!vendor&&settings.ReplaceOrdinaryCombatSpells?npc.Spells.Select(s=>s.FormKey).Where(s=>Replaceable(s,records)).ToArray():[];
            plans.Add(new{FormKey=key,npc.EditorID,Name=npc.Name?.ToString(),Profile=profile,Build=build,Tier=tier,Level=npc.Configuration.LevelOffset,MagickaPool=pool,Vendor=vendor,Kit=kit,Removed=removed.Select(k=>k.ToString()).ToArray(),Retained=npc.Spells.Select(s=>s.FormKey.ToString()).Except(removed.Select(k=>k.ToString())).ToArray(),Notes="Base magicka from stored configuration; racial/runtime bonuses and actual AI choice require in-game verification."});
            if(!write||kit.Length==0)continue;
            var target=state.PatchMod.Npcs.GetOrAddAsOverride(npc);
            target.Configuration!.BaseSpellPoints=(ushort)pool;
            if(!vendor)foreach(var link in target.Spells.Where(link=>removed.Contains(link.FormKey)).ToArray())target.Spells.Remove(link);
            foreach(var entry in kit){var spellKey=SpellKey(entry.Spell);if(!target.Spells.Any(s=>s.FormKey==spellKey))target.Spells.Add(spellKey);}
            records[target.FormKey]=target;changes[target.FormKey]=["Spells","Configuration.BaseSpellPoints"];
        }
        run.WriteReport(".magic-gameplay.json",new{Schema=1,Applied=write,ConfigurationFile=configPath,Settings=settings,EngineSettings=engineChanges,Variants=variants,GeneratedSpells=generated.ToDictionary(p=>p.Key,p=>p.Value.ToString()),Plans=plans,Skipped=skipped,Notes=new[]{"Original spell/MGEF records, racial abilities, scripted effects and non-combat spell lists retained. Vendors receive additions only.","Kits are capability inventories, not guaranteed AI sequences. Summon DPS, allied healing and weakness combinations are not assumed.","Exceptional spells require an explicit AllowRareSpells actor override and the configured access tier."}},MagicConfiguration.Options);
        run.Log($"Magic gameplay: {plans.Count} caster/vendor plans, {generated.Count} new spells, {changes.Count(k=>records.GetValueOrDefault(k.Key) is INpcGetter)} NPC writes; Applied={write}.");
        return changes;
    }
}