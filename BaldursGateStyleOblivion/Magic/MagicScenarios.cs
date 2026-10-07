using BaldursGateStyleOblivion.Combat;

namespace BaldursGateStyleOblivion.Magic;

public sealed class MagicScenario
{
    public string Profile { get; set; } = "Destruction specialist";
    public string Race { get; set; } = "";
    public bool Female { get; set; }
    public string? Birthsign { get; set; }
    public int Tier { get; set; } = 3;
    public double Level { get; set; } = 10;
    public int OpponentTier { get; set; } = 3;
    public double OpponentLevel { get; set; } = 10;
    public string? AttackSlot { get; set; }
    public bool AllowRareSpells { get; set; }
    public bool UseShield { get; set; } = true;
    public bool UseHealing { get; set; } = true;
    public double ArmorRating { get; set; }
    public double SpellEffectiveness { get; set; } = 100;
    public double Resistance { get; set; }
    public double SpellContactRate { get; set; } = .75;
    public double MeleeContactRate { get; set; } = .65;
    public double? MagickaOverride { get; set; }
    public double? RegenerationOverride { get; set; }
}
public sealed record MagicPoint(double Seconds,double CasterHealth,double WarriorHealth,double Magicka);
public sealed record MagicScenarioResult(Dictionary<string,double> Stats,KitEntry[] Kit,double MagickaPool,double MagickaRegen,double WarriorHealth,double WarriorCleanHit,double? SpellDamage,double? SpellCost,double? PoolDamage,MagicPoint[] Timeline,string[] Notes,double? PoolRecoverySeconds,double? AttackBurstCasts,double? SustainedDamageCeiling);
public static class MagicScenarios
{
    public static MagicScenarioResult Run(MagicSettings settings,CombatSettings combat,CreationCatalog creation,IReadOnlyDictionary<string,double> gameSettings,MagicScenario scenario)
    {
        MagicConfiguration.Validate(settings);
        var effective=gameSettings.ToDictionary();foreach(var pair in settings.GameSettings)effective[pair.Key]=pair.Value;gameSettings=effective;
        void Range(double value,double min,double max,string name)=>CombatConfiguration.Range(value,min,max,name);
        Range(scenario.Tier,0,10,"Caster tier");Range(scenario.OpponentTier,0,10,"Opponent tier");Range(scenario.Level,1,100,"Caster level");Range(scenario.OpponentLevel,1,100,"Opponent level");
        Range(scenario.ArmorRating,0,85,"Armor rating");Range(scenario.SpellEffectiveness,0,100,"Spell effectiveness");Range(scenario.Resistance,0,100,"Spell resistance");Range(scenario.SpellContactRate,0,1,"Spell contact rate");Range(scenario.MeleeContactRate,0,1,"Melee contact rate");
        if(scenario.MagickaOverride.HasValue)Range(scenario.MagickaOverride.Value,0,10000,"Magicka pool");if(scenario.RegenerationOverride.HasValue)Range(scenario.RegenerationOverride.Value,0,1000,"Magicka regeneration");
        var stats=CasterKits.Build(combat,creation,settings,scenario.Profile,scenario.Race,scenario.Female,scenario.Birthsign,scenario.Level).Stats;
        var race=creation.Races.Single(r=>r.Key==scenario.Race);var sign=creation.Birthsigns.SingleOrDefault(s=>s.Key==scenario.Birthsign);
        var pool=scenario.MagickaOverride??stats["Intelligence"]*(1+gameSettings.GetValueOrDefault("fPCBaseMagickaMult",1))+race.Bonuses.GetValueOrDefault("Magicka")+(sign?.Bonuses.GetValueOrDefault("Magicka")??0);
        var stunted=race.Bonuses.GetValueOrDefault("StuntedMagicka")>0||(sign?.Bonuses.GetValueOrDefault("StuntedMagicka")??0)>0;
        var regen=scenario.RegenerationOverride??(stunted?0:pool*(gameSettings.GetValueOrDefault("fMagickaReturnBase",.75)+stats["Willpower"]*gameSettings.GetValueOrDefault("fMagickaReturnMult",.02))*.01);
        var kit=CasterKits.Select(settings,scenario.Profile,stats,pool,scenario.AllowRareSpells?10:scenario.Tier,gameSettings,scenario.AllowRareSpells);
        var attack=kit.Where(s=>s.Spell.Metric=="Damage"&&(scenario.AttackSlot is null||s.Spell.Slot==scenario.AttackSlot)).OrderByDescending(s=>s.Spell.Magnitude).FirstOrDefault();
        var healing=scenario.UseHealing?kit.FirstOrDefault(s=>s.Spell.Metric=="Healing"):null;
        var shield=scenario.UseShield?kit.FirstOrDefault(s=>s.Spell.Metric=="Shield"):null;
        var imperial=creation.Races.FirstOrDefault(r=>r.Name=="Imperial")??creation.Races.First();
        var warrior=PlayerBuilds.AtLevel(combat.Gameplay,creation,"Warrior",imperial.Key,false,null,scenario.OpponentLevel).Stats;
        var hit=CreatureAttacks.ReferenceHit(combat,creation,scenario.OpponentTier,scenario.OpponentLevel);
        var damage=attack?.Spell.Magnitude*scenario.SpellEffectiveness*.01*(1-scenario.Resistance*.01);
        var health=stats["Health"];var enemy=warrior["Health"];var magicka=pool;var nextCast=0d;var shieldEnd=0d;var timeline=new List<MagicPoint>{new(0,health,enemy,magicka)};
        for(var step=1;step<=600&&health>0&&enemy>0;step++)
        {
            var time=step*.1;
            magicka=Math.Min(pool,magicka+regen*.1);
            if(time>=nextCast)
            {
                KitEntry? action=shield is not null&&time>=shieldEnd&&magicka>=shield.EstimatedCost?shield:healing is not null&&health<stats["Health"]*.5&&magicka>=healing.EstimatedCost?healing:attack is not null&&magicka>=attack.EstimatedCost?attack:null;
                if(action is not null)
                {
                    magicka-=action.EstimatedCost;nextCast=time+settings.CastInterval;
                    if(action==shield)shieldEnd=time+action.Spell.Duration;
                    else if(action==healing)health=Math.Min(stats["Health"],health+action.Spell.Magnitude*scenario.SpellEffectiveness*.01);
                    else enemy=Math.Max(0,enemy-damage!.Value*scenario.SpellContactRate);
                }
            }
            var armor=Math.Min(gameSettings.GetValueOrDefault("fMaxArmorRating",85),scenario.ArmorRating+(time<shieldEnd?(shield?.Spell.Magnitude??0)*scenario.SpellEffectiveness*.01:0));
            if(enemy>0)health=Math.Max(0,health-hit*(1-armor*.01)*scenario.MeleeContactRate*.1);
            if(step%10==0||health==0||enemy==0)timeline.Add(new(Math.Round(time,1),health,enemy,magicka));
        }
        return new(stats,kit,pool,regen,warrior["Health"],hit,damage,attack?.EstimatedCost,attack is null?null:Math.Floor(pool/attack.EstimatedCost)*damage,timeline.ToArray(),
            ["Scenario assumption: chosen attack, shield upkeep, then self-healing below 50% health. This is not a prediction of NPC AI priorities.","One expected warrior clean-hit opportunity per second, full fatigue, tier longsword. Contact rates represent misses/positioning; blocking and power attacks are excluded.","Single target, neutral Luck. Control, invisibility, summons, weapon attacks by the caster, absorption, reflection and weakness sequences are not simulated.","Shield magnitude is added to entered equipped armor rating and capped. Magic delivery, cost rounding and NPC runtime magicka require calibration; use observed pool/regen overrides."],regen>0?pool/regen:null,attack?.CastsFromPool,attack is not null?Math.Min(1/settings.CastInterval,regen/attack.EstimatedCost)*damage*scenario.SpellContactRate:null);
    }
}