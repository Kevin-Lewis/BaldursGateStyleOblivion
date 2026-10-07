using System;
using System.Collections.Generic;
using System.Linq;
namespace BaldursGateStyleOblivion.Combat;

public static class CreatureAttacks
{
    public static double ReferenceHit(CombatSettings settings, CreationCatalog creation, int tier, double level)
    {
        var race=creation.Races.FirstOrDefault(r=>r.Name=="Imperial")??creation.Races.First();
        var stats=PlayerBuilds.AtLevel(settings.Gameplay,creation,"Warrior",race.Key,false,null,level).Stats;
        var material=settings.QuickTiers[tier].WeaponMaterial;
        var baseline=settings.Gameplay.WeaponBaselines["Longsword"];
        var weapon=new PhysicalItem("CreatureReference",null,"Tier longsword","Weapon","Longsword",material,baseline.Damage,baseline.Speed,baseline.Reach,baseline.Weight,baseline.Durability,0,0,"",false,false,false,null);
        var fighter=new Fighter { Weapon=weapon.FormKey,Strength=stats["Strength"],WeaponSkill=stats["Blade"],WeaponPower=settings.Gameplay.WeaponPower(tier),Style=settings.Styles.Keys.First() };
        var defender=new Fighter { Weapon=weapon.FormKey,Style=fighter.Style };
        return CombatAnalysis.Run(new() { Player=fighter,Enemy=defender,Seconds=1 },settings,new Dictionary<string,PhysicalItem>{{weapon.FormKey,weapon}},creation.Settings,true).PlayerCleanHit;
    }
    public static double NaturalDamage(CombatSettings settings, CreationCatalog creation, int tier, double level, string build)
    {
        var hit=settings.Gameplay.MatchCreatureAttacksToWarrior?ReferenceHit(settings,creation,tier,level):settings.Gameplay.ActorTiers[tier].NaturalDamage;
        return Math.Clamp(Math.Round(hit*settings.Gameplay.CreatureBuilds[build].Damage,MidpointRounding.AwayFromZero),1,ushort.MaxValue);
    }
    public static object[] References(CombatSettings settings, CreationCatalog creation, IReadOnlyDictionary<int,int?> levels) => Enumerable.Range(0,11).Select(tier=>
    {
        var level=levels.GetValueOrDefault(tier)??levels.GetValueOrDefault(tier-1)??40;
        return (object)new { Tier=tier,Level=level,Material=settings.QuickTiers[tier].WeaponMaterial,WarriorHit=ReferenceHit(settings,creation,tier,level),NaturalAttacks=settings.Gameplay.CreatureBuilds.Keys.ToDictionary(build=>build,build=>NaturalDamage(settings,creation,tier,level,build)) };
    }).ToArray();
}
