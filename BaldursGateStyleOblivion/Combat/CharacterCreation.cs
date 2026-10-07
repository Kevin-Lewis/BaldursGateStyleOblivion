using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace BaldursGateStyleOblivion.Combat;

internal static class CharacterCreation
{
    public static CreationCatalog Read(Dictionary<FormKey,IMajorRecordGetter> records, Dictionary<string,double> settings, IReadOnlyDictionary<FormKey,bool> playable)
    {
        Dictionary<string,double> Attributes(IRaceStatsGetter stats)=>typeof(IRaceStatsGetter).GetProperties().Where(p=>p.PropertyType==typeof(byte)).ToDictionary(p=>p.Name,p=>Convert.ToDouble(p.GetValue(stats)));
        BaldursGateStyleOblivion.Creation.CreationAbility[] Abilities(IEnumerable<FormKey> keys)=>keys.Select(k=>records.GetValueOrDefault(k)).OfType<ISpellGetter>().Where(s=>s.Data is not null).Select(s=>new BaldursGateStyleOblivion.Creation.CreationAbility(s.FormKey.ToString(),s.EditorID??"",s.Name?.ToString()??s.EditorID??"",s.Data!.Type.ToString(),s.Data.Cost,s.Effects.Select((e,i)=>new BaldursGateStyleOblivion.Creation.CreationEffect(i,e.Data?.MagicEffect.ToString()??"",e.Data?.ActorValue.ToString()??"",e.Data?.Magnitude??0,e.Data?.Duration??0,e.Data?.Type.ToString()??"",e.Data?.Area??0,e.ScriptEffect is not null)).ToArray())).ToArray();
        Dictionary<string,double> Bonuses(IEnumerable<FormKey> keys)=>BaldursGateStyleOblivion.Creation.CreationBalance.Bonuses(Abilities(keys));
        var races=records.Values.OfType<IRaceGetter>().Where(r=>!r.IsDeleted && playable.GetValueOrDefault(r.FormKey) && r.RaceStats is not null)
            .Select(r=>new CreationRace(r.FormKey.ToString(),r.Name?.ToString()??r.EditorID!,Attributes(r.RaceStats!.Male),Attributes(r.RaceStats.Female),
                typeof(IRaceDataGetter).GetProperties().Where(p=>p.Name.StartsWith("SkillBoost")).Select(p=>(ISkillBoostGetter)p.GetValue(r.Data!)!).GroupBy(s=>s.Skill.ToString().Replace("Speechraft","Speechcraft")).ToDictionary(g=>g.Key,g=>g.Sum(s=>(double)s.Boost)),Bonuses(r.Spells.Select(s=>s.FormKey)),Abilities(r.Spells.Select(s=>s.FormKey)))).ToArray();
        var classes=records.Values.OfType<IClassGetter>().Where(c=>!c.IsDeleted && c.Data?.Flags.ToString().Contains("Playable")==true)
            .Select(c=>new CreationClass(c.EditorID!,c.Data!.Specialization.ToString(),c.Data.PrimaryAttributes.Select(a=>a.ToString()).ToArray(),c.Data.SecondaryAttributes.Select(a=>a.ToString().Replace("Speechraft","Speechcraft")).ToArray())).ToArray();
        var signs=records.Values.OfType<IBirthsignGetter>().Where(s=>!s.IsDeleted).Select(s=>new CreationSign(s.FormKey.ToString(),s.Name?.ToString()??s.EditorID!,Bonuses(s.Spells.Select(s=>s.FormKey)),Abilities(s.Spells.Select(s=>s.FormKey)))).ToArray();
        return new(races,classes,signs,settings);
    }
    public static string? TrainerFloor(INpcGetter npc,IClassGetter? cls, Dictionary<string,double> stats)
    {
        var ai=npc.AIData;
        var native=ai?.BuySellServices.ToString().Contains("Training")==true;
        var training=cls?.Data?.Training;
        var classTrainer=cls?.Data?.ClassServices.ToString().Contains("Training")==true;
        var skill=native?ai!.Teaches?.ToString():classTrainer?training?.TrainedSkill?.ToString():null;
        var ceiling=native?ai!.MaximumTrainingLevel:classTrainer?training?.MaximumTrainingLevel??0:0;
        if(skill is null || ceiling==0)return null;
        if(skill=="Speechraft")skill="Speechcraft";
        if(!CombatBuilds.Skills.Contains(skill))throw new InvalidDataException($"Unsupported trainer skill: {skill}.");
        stats[skill]=Math.Max(stats.GetValueOrDefault(skill),ceiling);
        return $"{skill} >= {ceiling} ({(native?"NPC":"class")} training ceiling)";
    }
}