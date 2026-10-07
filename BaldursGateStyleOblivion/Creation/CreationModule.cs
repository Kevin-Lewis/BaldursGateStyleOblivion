using System.Text.Json;
using BaldursGateStyleOblivion.Combat;
using BaldursGateStyleOblivion.Core;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;
namespace BaldursGateStyleOblivion.Creation;
internal static class CreationModule
{
    public static string PathFor(PatcherRun run)=>string.IsNullOrWhiteSpace(run.Settings.CreationConfigurationFile)?File.Exists(Path.Combine(run.DataDirectory,"creation.json"))?Path.Combine(run.DataDirectory,"creation.json"):Path.Combine(AppContext.BaseDirectory,"creation.json"):Path.GetFullPath(run.Settings.CreationConfigurationFile,run.DataDirectory);
    public static Dictionary<FormKey,string[]> Run(IPatcherState<IOblivionMod,IOblivionModGetter> state,PatcherRun run)
    {
        var settings=CreationBalance.Load(PathFor(run));var records=new Dictionary<FormKey,IMajorRecordGetter>();var playable=new Dictionary<FormKey,bool>();
        foreach(var listing in state.LoadOrder.PriorityOrder.Where(l=>l.Enabled&&l.Mod is not null&&run.IsInputPlugin(l.ModKey)))
        {
            foreach(var record in listing.Mod!.EnumerateMajorRecords())records.TryAdd(record.FormKey,record);
            foreach(var pair in PlayableRaces.Read(Path.Combine(state.DataFolderPath.ToString(),listing.ModKey.ToString()),listing.Mod))playable.TryAdd(pair.Key,pair.Value);
        }
        foreach(var record in state.PatchMod.EnumerateMajorRecords())records[record.FormKey]=record;
        var engine=new Dictionary<string,double>();foreach(var r in records.Values.OfType<IGameSettingIntGetter>().Where(r=>r.EditorID is not null&&r.Data.HasValue))engine[r.EditorID!]=r.Data!.Value;foreach(var r in records.Values.OfType<IGameSettingFloatGetter>().Where(r=>r.EditorID is not null&&r.Data.HasValue))engine[r.EditorID!]=r.Data!.Value;
        var before=CharacterCreation.Read(records,engine,playable);var after=CreationBalance.Apply(before,settings);var changes=new Dictionary<FormKey,string[]>();var write=settings.Enabled&&!run.Settings.ReportOnly;
        if(write)
        {
            foreach(var pair in settings.Spells)
            {
                var key=FormKey.Factory(pair.Key);var source=(ISpellGetter)records[key];if(pair.Value.Effects.Count==0)continue;var target=state.PatchMod.Spells.GetOrAddAsOverride(source);
                foreach(var edit in pair.Value.Effects){var data=target.Effects[edit.Key].Data!;if(edit.Value.Magnitude.HasValue)data.Magnitude=(uint)edit.Value.Magnitude.Value;if(edit.Value.Duration.HasValue)data.Duration=(uint)edit.Value.Duration.Value;}
                changes[key]=["Effects.Magnitude","Effects.Duration"];
            }
            var effects=records.Values.OfType<IMagicEffectGetter>().Where(e=>e.EditorID is not null).ToDictionary(e=>e.EditorID!);
            string Describe(CreationEffect effect)
            {
                effects.TryGetValue(effect.Code,out var definition);var text=definition?.Name?.ToString()??effect.Code;var flags=definition?.Data?.Flags??0;
                if(effect.Code is "FOAT" or "DRAT" or "FOHE" or "FOFA" or "FOSP")text+=" ("+effect.ActorValue+")";
                if(!flags.HasFlag(MagicEffect.MagicFlag.NoMagnitude))text+=" "+effect.Magnitude+(effect.Code.StartsWith("RS")||effect.Code.StartsWith("WK")||effect.Code is "SABS" or "REDG" or "RFLC"?"%":" points");
                if(effect.Duration>0&&!flags.HasFlag(MagicEffect.MagicFlag.NoDuration))text+=" for "+effect.Duration+" seconds";
                return text+" on "+effect.Range.ToLowerInvariant();
            }
            foreach(var sign in after.Birthsigns.Where(s=>(s.Abilities??[]).Any(a=>settings.Spells.ContainsKey(a.Key))))
            {
                var source=(IBirthsignGetter)records[FormKey.Factory(sign.Key)];var target=state.PatchMod.Birthsigns.GetOrAddAsOverride(source);
                target.Description=string.Join(" ",(sign.Abilities??[]).Select(a=>a.Name+" ("+(a.Type=="Ability"?"permanent":a.Type=="Power"?"once per day":"lesser power")+"): "+string.Join("; ",a.Effects.Select(Describe))+"."));changes[source.FormKey]=["Description"];
            }
            foreach(var pair in settings.Races)
            {
                var key=FormKey.Factory(pair.Key);var source=(IRaceGetter)records[key];var edit=pair.Value;if(edit.Male.Count+edit.Female.Count+edit.Skills.Count==0)continue;
                var target=state.PatchMod.Races.GetOrAddAsOverride(source);
                foreach(var p in edit.Male)target.RaceStats!.Male.GetType().GetProperty(p.Key)!.SetValue(target.RaceStats.Male,(byte)p.Value);
                foreach(var p in edit.Female)target.RaceStats!.Female.GetType().GetProperty(p.Key)!.SetValue(target.RaceStats.Female,(byte)p.Value);
                foreach(var p in edit.Skills)
                {
                    var boost=target.Data!.GetType().GetProperties().Where(p=>p.Name.StartsWith("SkillBoost")).Select(p=>(SkillBoost)p.GetValue(target.Data)!).FirstOrDefault(b=>b.Skill.ToString().Replace("Speechraft","Speechcraft")==p.Key);
                    if(boost is null)throw new ArgumentException("Edit an existing racial skill bonus rather than adding a new skill: "+p.Key);boost.Boost=(sbyte)p.Value;
                }
                changes[key]=["RaceStats","Data.SkillBoosts"];
            }
            foreach(var pair in settings.Classes)
            {
                var source=records.Values.OfType<IClassGetter>().Single(c=>c.EditorID==pair.Key);var edit=pair.Value;if(edit.Attributes is null&&edit.Skills is null&&edit.Specialization is null)continue;var target=state.PatchMod.Classes.GetOrAddAsOverride(source);
                if(edit.Specialization is not null)target.Data!.Specialization=Enum.Parse<Class.SpecializationFlag>(edit.Specialization);
                if(edit.Attributes is not null)for(var i=0;i<2;i++)target.Data!.PrimaryAttributes[i]=Enum.Parse<ActorValue>(edit.Attributes[i]);
                if(edit.Skills is not null)for(var i=0;i<7;i++)target.Data!.SecondaryAttributes[i]=Enum.Parse<ActorValue>(edit.Skills[i]=="Speechcraft"?"Speechraft":edit.Skills[i]);
                changes[source.FormKey]=["Data.Specialization","Data.PrimaryAttributes","Data.SecondaryAttributes"];
            }
        }
        var warnings=after.Classes.Where(c=>c.Skills.Length!=7||c.Skills.Distinct().Count()!=7||c.Attributes.Length!=2||c.Attributes.Distinct().Count()!=2).Select(c=>c.Name+": invalid native class layout.").ToArray();
        run.WriteReport(".character-creation.json",new{Schema=1,Applied=write,ConfigurationFile=PathFor(run),Baseline=before,Proposed=after,Warnings=warnings,Notes=new[]{"Race and class starts are retained unless explicitly overridden. Native powers retain effect codes, ranges, costs, scripts and daily/lesser-power activation types.","Racial ability spell edits affect their existing users, including NPCs. Do not add these runtime abilities to stored NPC stats again.","Comparison assumes typical progression and passive abilities. Daily powers, absorption-triggered magicka recovery and equipped effects require in-game testing."}},CreationBalance.Options);
        run.Log($"Character creation: {before.Races.Length} races, {before.Classes.Length} classes, {before.Birthsigns.Length} birthsigns; {changes.Count} writes.");return changes;
    }
}