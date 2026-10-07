using System.Text.Json;
using BaldursGateStyleOblivion.Combat;
using BaldursGateStyleOblivion.Core;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Magic;

internal static class MagicAnalysisModule
{
    internal static SpellInfo[] Read(Dictionary<FormKey,IMajorRecordGetter> records,Func<ModKey,bool> includes)
    {
        var effects=records.Values.OfType<IMagicEffectGetter>().Where(e=>!e.IsDeleted&&e.EditorID is not null).GroupBy(e=>e.EditorID!).ToDictionary(g=>g.Key,g=>g.First());
        var users=records.Values.Where(r=>!r.IsDeleted&&r is INpcGetter or ICreatureGetter).SelectMany(r=>(r is INpcGetter npc?npc.Spells:((ICreatureGetter)r).Spells).Select(link=>new{link.FormKey,Actor=r.FormKey.ToString(),Vendor=r is INpcGetter vendor&&vendor.AIData?.BuySellServices.ToString().Contains("Spells")==true})).GroupBy(r=>r.FormKey).ToDictionary(g=>g.Key,g=>g.ToArray());
        return records.Values.OfType<ISpellGetter>().Where(s=>!s.IsDeleted&&s.Data is not null&&includes(s.FormKey.ModKey)).OrderBy(s=>s.FormKey.ToString(),StringComparer.Ordinal).Select(s=>
        {
            var items=s.Effects.Where(e=>e.Data is not null).Select(e=>
            {
                var d=e.Data!;var code=d.MagicEffect.ToString()??"Unknown";var definition=effects.GetValueOrDefault(code);var flags=definition?.Data?.Flags??0;
                return new SpellEffectInfo(code,e.ScriptEffect?.Name??definition?.Name?.ToString()??code,e.ScriptEffect?.Data?.MagicSchool.ToString()??definition?.Data?.MagicSchool.ToString()??"Unknown",MagicAnalysis.Family(code),d.Magnitude,d.Duration,d.Area,d.Type.ToString(),d.ActorValue.ToString(),definition?.Data?.BaseCost,flags.HasFlag(MagicEffect.MagicFlag.NoMagnitude),flags.HasFlag(MagicEffect.MagicFlag.NoDuration),flags.HasFlag(MagicEffect.MagicFlag.NoArea),flags.HasFlag(MagicEffect.MagicFlag.Hostile),e.ScriptEffect?.Data?.Script.FormKeyNullable?.ToString());
            }).ToArray();
            var refs=users.GetValueOrDefault(s.FormKey)??[];
            var requirement=s.Data!.Level.ToString() switch{"Novice"=>0,"Apprentice"=>25,"Journeyman"=>50,"Expert"=>75,"Master"=>100,_=>0};
            return new SpellInfo(s.FormKey.ToString(),s.EditorID,s.Name?.ToString()??s.EditorID??s.FormKey.ToString(),s.FormKey.ModKey.ToString(),s.Data.Type.ToString(),s.Data.Level.ToString(),requirement,s.Data.Flag.HasFlag(Spell.SpellFlag.ManualSpellCost),s.Data.Cost,((uint)s.Data.Flag&255).ToString("X2"),items,refs.Select(r=>r.Actor).Distinct().ToArray(),refs.Where(r=>r.Vendor).Select(r=>r.Actor).Distinct().ToArray(),s.EditorID?.StartsWith("Test",StringComparison.OrdinalIgnoreCase)==true);
        }).ToArray();
    }
    internal static IReadOnlyDictionary<string,double> Settings(Dictionary<FormKey,IMajorRecordGetter> records)
    {
        var settings=records.Values.OfType<IGameSettingFloatGetter>().Where(g=>!g.IsDeleted&&g.EditorID is not null&&g.Data.HasValue).ToDictionary(g=>g.EditorID!,g=>(double)g.Data!.Value);
        foreach(var g in records.Values.OfType<IGameSettingIntGetter>().Where(g=>!g.IsDeleted&&g.EditorID is not null&&g.Data.HasValue))settings[g.EditorID!]=g.Data!.Value;
        return settings;
    }
    public static void Run(IPatcherState<IOblivionMod,IOblivionModGetter> state,PatcherRun run)
    {
        var records=new Dictionary<FormKey,IMajorRecordGetter>();
        foreach(var listing in state.LoadOrder.PriorityOrder.Where(l=>l.Enabled&&l.Mod is not null&&run.IsInputPlugin(l.ModKey)))foreach(var record in listing.Mod!.EnumerateMajorRecords())records.TryAdd(record.FormKey,record);
        foreach(var record in state.PatchMod.EnumerateMajorRecords())records[record.FormKey]=record;
        Write(records,run.Includes,run.ReportPath(".magic-analysis.json"),run.ReportPath(".physical-combat.json"));
        run.Log($"Magic analysis: report only; no spell, magic-effect or actor spell-list changes. Report: {run.ReportPath(".magic-analysis.json")}");
    }
    internal sealed record MageReference(int Level,double Health,double Intelligence,double Willpower,double Magicka,Dictionary<string,double> Skills,int EligibleVendorSpells,double MaximumDirectDamagePerCast);
    private static MageReference[] Progression(SpellAssessment[] spells,IReadOnlyDictionary<string,double> settings,string? physicalReport)
    {
        if(physicalReport is null||!File.Exists(physicalReport))return [];
        using var document=JsonDocument.Parse(File.ReadAllText(physicalReport));
        var root=document.RootElement;
        var creation=root.GetProperty("CharacterCreation").Deserialize<CreationCatalog>(CombatConfiguration.Options)!;
        var config=CombatConfiguration.Load(root.GetProperty("ConfigurationFile").GetString()!);
        var race=creation.Races.FirstOrDefault(r=>r.Name=="Imperial");
        if(race is null)return [];
        return new[]{1,2,5,10,15,20,25,30,35,40}.Select(level=>
        {
            var stats=PlayerBuilds.AtLevel(config.Gameplay,creation,"Mage",race.Key,false,null,level).Stats;
            var skills=new[]{"Alteration","Conjuration","Destruction","Illusion","Mysticism","Restoration"}.ToDictionary(key=>key,key=>stats[key]);
            var eligible=spells.Where(s=>s.Spell.Type=="Spell"&&!s.Spell.Test&&s.Spell.Vendors.Length>0&&skills.GetValueOrDefault(s.School)>=s.Spell.SkillRequirement).ToArray();
            var pool=stats["Intelligence"]*(1+settings.GetValueOrDefault("fPCBaseMagickaMult",1));
            var affordable=eligible.Where(s=>MagicAnalysis.Cast(s,skills.GetValueOrDefault(s.School),pool,0,settings).Cost is {} cost&&cost<=pool);
            return new MageReference(level,stats["Health"],stats["Intelligence"],stats["Willpower"],pool,skills,eligible.Length,affordable.Select(s=>s.Damage).DefaultIfEmpty().Max());
        }).ToArray();
    }
    internal static void Write(Dictionary<FormKey,IMajorRecordGetter> records,Func<ModKey,bool> includes,string path,string? physicalReport=null)
    {
        var settings=Settings(records);var spells=Read(records,includes).Select(s=>MagicAnalysis.Assess(s,settings)).ToArray();MagicAnalysis.FindOutliers(spells);
        var coefficients=settings.Where(p=>p.Key.StartsWith("fMagic")||p.Key.StartsWith("fSpell")||p.Key.Contains("Magicka")||p.Key=="fActorLuckSkillMult").ToDictionary();
        var progression=Progression(spells,settings,physicalReport);
        var report=new { Schema=1,GeneratedUtc=DateTime.UtcNow,AnalysisOnly=true,RecordsModified=0,Assumptions=new[]{"Potential per-target effects at 100% effectiveness, neutral Luck, no resistance, reflection or absorption. Area does not imply a target count.","Estimated costs use winning MGEF base costs and GMSTs; absent area/duration/range coefficients use 0.15/0.1/1.5. Display/runtime rounding requires calibration.","School is the highest estimated-cost effect; mixed-school spells and scripts need engine review. Stored cost is retained alongside calculated cost.","Utility, control, defense and summons have distinct metrics; no universal power score. Numerical outliers are review candidates, not automatic edits."},GameSettings=coefficients,Summary=new{Spells=spells.Length,OrdinarySpells=spells.Count(s=>s.Spell.Type=="Spell"&&!s.Spell.Test),TestRecords=spells.Count(s=>s.Spell.Test),Outliers=spells.Count(s=>s.Outliers.Count>0),VendorReviewCandidates=spells.Count(s=>s.Outliers.Count>0&&s.Spell.Vendors.Length>0)},MageProgression=progression,Spells=spells };
        File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        var lines=new List<string>{"# Magic analysis — first pass","","Analysis only: no spell or magic-effect records changed. Outliers require availability/context review, not automatic normalization.","","## Coverage","",$"{spells.Length} spell records; {report.Summary.OrdinarySpells} ordinary non-test spells; {report.Summary.Outliers} numerical review candidates.","","## Mage progression reference","","Male Imperial Mage, no birthsign/equipment bonuses. Skills follow the existing shared character progression estimates. Spell counts mean skill eligibility among vendor-linked records, not spells automatically learned. Magicka pool is Intelligence × 2, before bonuses; no casting cadence or resistance modeled.","","| Level | Health | Intelligence | Willpower | Destruction | Conjuration | Restoration | Eligible vendor spells |","|---|---|---|---|---|---|---|---|"};
        string Safe(string text)=>text.Replace("|","/").Replace("\n"," ");
        foreach(var row in progression)lines.Add($"| {row.Level} | {row.Health} | {row.Intelligence} | {row.Willpower} | {row.Skills.GetValueOrDefault("Destruction")} | {row.Skills.GetValueOrDefault("Conjuration")} | {row.Skills.GetValueOrDefault("Restoration")} | {row.EligibleVendorSpells} |");
        if(progression.Length==0)lines.Add("\nProgression reference unavailable: generate the physical combat report first.");
        lines.AddRange(["","## Numerical review candidates","","Vendor-linked spells come first; non-vendor quest/NPC/script records remain visible for contextual review. Vendor links are inferred from spell-selling NPC lists, not a guarantee of access.","","| Spell | School / mastery | Base cost estimate | Access | Review reason |","|---|---|---|---|---|"]);
        foreach(var s in spells.Where(s=>s.Outliers.Count>0).OrderByDescending(s=>s.Spell.Vendors.Length>0).ThenBy(s=>s.Spell.Name))lines.Add($"| {Safe(s.Spell.Name)} ({s.Spell.FormKey}) | {s.School} / {s.Spell.Mastery} | {s.BaseCost:0.##} | {(s.Spell.Vendors.Length>0?"Vendor-linked":s.Spell.ActorUsers.Length>0?"Actor-listed":"Unlinked / scripted access possible")} | {Safe(string.Join("; ",s.Outliers))} |");
        lines.AddRange(["","## Contextual review priorities","","These are mechanic-sensitive groups, not confirmed balance errors.","","| Mechanic | Ordinary non-test spells | Why review |","|---|---|---|"]);
        foreach(var family in new[]{"Control","Level-limited control","Weakness","Defense","Summon","Resistance / reflection","Temporary health drain","Scripted"})
        {
            var count=spells.Count(s=>s.Spell.Type=="Spell"&&!s.Spell.Test&&s.Spell.Effects.Any(e=>e.Family==family));
            lines.Add($"| {family} | {count} | Review effect interactions, access and engine exceptions independently of damage. |");
        }
        lines.AddRange(["","## Evaluation limits","","Damage and healing are magnitude × duration, with duration zero treated as an instant one-tick effect. Shield percentages, control seconds and summon durations remain separate. Drain Health, weakness stacking, elemental shields, reflect/absorb, immunity and summons need contextual evaluation. Skill reduces estimated cost; it does not multiply effect magnitude. Custom spellmaking and actor spell availability are not rebalanced in this phase.","","Cost reference: [xOBSE source](https://github.com/llde/xOBSE/blob/master/obse/obse/GameForms.cpp). See docs/MagicFramework.md for progression targets and the calibration checklist."]);
        File.WriteAllLines(Path.ChangeExtension(path,".md"),lines);
    }
}
