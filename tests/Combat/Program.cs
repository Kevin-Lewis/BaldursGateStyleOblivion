using System.Text.Json;
using BaldursGateStyleOblivion.Combat;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;

void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < .0001, $"{message}: {actual} != {expected}");
void Reject(Action action, string message) { try { action(); } catch (Exception error) when (error is ArgumentException or JsonException) { return; } throw new Exception(message); }
if(args.Length==3)
{
    using var report=JsonDocument.Parse(File.ReadAllText(args[0]));
    using var gameplay=JsonDocument.Parse(File.ReadAllText(args[1]));
    using var candidate=OblivionMod.CreateFromBinaryOverlay(args[2],OblivionRelease.Oblivion);
    var config=CombatConfiguration.Load("BaldursGateStyleOblivion/combat.json");
    if(!report.RootElement.GetProperty("Applied").GetBoolean())
    {
        Check(!gameplay.RootElement.GetProperty("Applied").GetBoolean(),"Gameplay writes enabled in report-only run");
        Check(new FileInfo(args[2]).Length==38,"Report-only candidate contains records");
        Check(gameplay.RootElement.GetProperty("Plans").GetArrayLength()>3000,"Report-only gameplay analysis missing");
        Console.WriteLine("Gameplay report-only checks passed: analysis retained, empty native plugin.");return;
    }
    foreach(var plan in report.RootElement.GetProperty("Plans").EnumerateArray())
    {
        var before=plan.GetProperty("Before").Deserialize<PhysicalItem>(CombatConfiguration.Options)!;
        var after=plan.GetProperty("After").Deserialize<PhysicalItem>(CombatConfiguration.Options)!;
        Check(!before.Protected || before.PreservationReason=="Enchanted item: enchantment balance pending","Artifact/script physical changes leaked");
        var key=FormKey.Factory(after.FormKey);
        var actual=after.Kind=="Weapon"?PhysicalCombatModule.ReadItem(candidate.Weapons[key],[])!:PhysicalCombatModule.ReadItem(candidate.Armors[key],[])!;
        Near(actual.Damage,after.Damage,"Native equipment damage");Near(actual.Armor,after.Armor,"Native equipment armor");Near(actual.Weight,after.Weight,"Native equipment weight");
        Check(actual.Value==before.Value&&actual.Enchanted==before.Enchanted,"Price/enchantment changed unexpectedly");
        Check(actual.Heavy==before.Heavy,"Native heavy/light classification changed unexpectedly");
    }
    var catalogItems=report.RootElement.GetProperty("Items").Deserialize<PhysicalItem[]>(CombatConfiguration.Options)!;
    Check(catalogItems.Single(i=>i.EditorID=="IronCuirass").Heavy,"Native iron armor incorrectly classified as light");
    Check(!catalogItems.Single(i=>i.EditorID=="GlassCuirass").Heavy,"Native glass armor incorrectly classified as heavy");
    var npcCount=0;var creatureCount=0;
    foreach(var plan in gameplay.RootElement.GetProperty("Plans").EnumerateArray())
    {
        var kind=plan.GetProperty("Kind").GetString();
        if(kind=="NPC")
        {
            var actor=candidate.Npcs[FormKey.Factory(plan.GetProperty("FormKey").GetString()!)];
            Check(!actor.Configuration!.Flags.HasFlag(Npc.NpcFlag.AutoCalcStats)&&!actor.Configuration.Flags.HasFlag(Npc.NpcFlag.PCLevelOffset),"NPC scaling/autocalc retained");
            foreach(var stat in plan.GetProperty("Stats").EnumerateObject())
            {
                var property=typeof(INpcDataGetter).GetProperty(stat.Name);
                if(property is not null)Near(Convert.ToDouble(property.GetValue(actor.Stats)),stat.Value.GetDouble(),"Native NPC "+stat.Name);
            }
            Near(actor.Configuration.Fatigue,plan.GetProperty("Stats").GetProperty("Fatigue").GetDouble(),"Native explicit fatigue");
            Check(candidate.CombatStyles.ContainsKey(actor.CombatStyle.FormKey),"NPC assigned style missing");npcCount++;
        }
        else if(kind=="Creature")
        {
            var actor=candidate.Creatures[FormKey.Factory(plan.GetProperty("FormKey").GetString()!)];
            Near(actor.Data!.Health,plan.GetProperty("Health").GetDouble(),"Creature health");Near(actor.Data.AttackDamage,plan.GetProperty("AttackDamage").GetDouble(),"Creature natural damage");
            Check(!actor.Configuration!.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset),"Creature autocalc retained");creatureCount++;
        }
        else if(kind=="GameSetting")
        {
            var setting=candidate.GameSettings.OfType<IGameSettingFloatGetter>().Single(g=>g.EditorID==plan.GetProperty("EditorID").GetString());
            Near(setting.Data!.Value,plan.GetProperty("After").GetDouble(),"Native engine setting");
        }
    }
    foreach(var skipped in gameplay.RootElement.GetProperty("Skipped").EnumerateArray())
    {
        var key=FormKey.Factory(skipped.GetProperty("FormKey").GetString()!);
        if(candidate.Npcs.TryGetValue(key,out var actor))Check(actor.CombatStyle.FormKey.ModKey!=candidate.ModKey,"Safeguarded actor received combat style");
    }
    foreach(var actor in candidate.Npcs.Where(n=>n.CombatStyle.FormKey.ModKey==candidate.ModKey))
    {
        foreach(var entry in actor.Items)Check(!entry.Item.IsNull,"Invalid generated inventory link");
        Check(actor.FormKey!=FormKey.Factory("000007:Oblivion.esm"),"Player base actor was edited");
    }
    Check(!candidate.Weapons.Any(w=>w.EditorID?.StartsWith("BGSOCombatTier")==true && w.Data?.Type.ToString()=="Staff"),"Staff must not receive physical tier variants");
    Check(npcCount>2000&&creatureCount>700,"Insufficient combat coverage");
    Console.WriteLine($"Combined native candidate checks passed: {npcCount} NPCs, {creatureCount} creatures, equipment and engine settings.");return;
}
if (args.Length == 2)
{
    using var report = JsonDocument.Parse(File.ReadAllText(args[0]));
    using var candidate = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    var plans = report.RootElement.GetProperty("Plans").EnumerateArray().ToArray();
    var applied = report.RootElement.GetProperty("Applied").GetBoolean();
    Check(plans.Length > 0, "Expected equipment proposals");
    Check(candidate.Npcs.Count == 0 && candidate.Creatures.Count == 0 && candidate.CombatStyles.Count == 0 && candidate.GameSettings.Count == 0, "Equipment experiment changed actors, AI, or global settings");
    if (!applied) Check(candidate.Weapons.Count == 0 && candidate.Armors.Count == 0, "Report-only run wrote equipment");
    else
    {
        Check(candidate.Weapons.Count + candidate.Armors.Count == plans.Length, "Native candidate count differs from plans");
        foreach (var plan in plans)
        {
            var before = plan.GetProperty("Before").Deserialize<PhysicalItem>(CombatConfiguration.Options)!;
            var after = plan.GetProperty("After").Deserialize<PhysicalItem>(CombatConfiguration.Options)!;
            Check(!before.Protected, "Protected equipment leaked into normalization");
            var key = FormKey.Factory(after.FormKey);
            var record = after.Kind == "Weapon" ? PhysicalCombatModule.ReadItem(candidate.Weapons[key], [])! : PhysicalCombatModule.ReadItem(candidate.Armors[key], [])!;
            Near(record.Damage, after.Damage, "Applied native damage"); Near(record.Armor, after.Armor, "Applied native armor");
            Near(record.Weight, after.Weight, "Applied native weight"); Near(record.Speed, after.Speed, "Applied native speed");
            Near(record.Reach, after.Reach, "Applied native reach"); Check(record.Durability == after.Durability && record.Value == before.Value, "Native durability/value mismatch");
        }
    }
    Console.WriteLine($"Native candidate validation passed: {plans.Length} proposals; applied={applied}.");
    return;
}
var settings = CombatConfiguration.Load("BaldursGateStyleOblivion/combat.json");
settings.Gameplay = new(); foreach(var entry in settings.Materials.Values){entry.Damage=entry.Armor=entry.Speed=entry.Reach=entry.Weight=entry.Durability=1;}
var sword = new PhysicalItem("000001:Test.esp", "WeapIronLongsword", "Iron sword", "Weapon", "Longsword", "Iron", 20, 1, 1, 10, 100, 0, 25, "", false, false, false, null);
var cuirass = new PhysicalItem("000002:Test.esp", "ArmorIronCuirass", "Iron cuirass", "Armor", "Heavy Armor", "Iron", 0, 0, 0, 20, 200, 20, 50, "UpperBody", true, false, false, null);
var shield = cuirass with { FormKey = "000003:Test.esp", Kind = "Shield", Class = "Heavy Shield", Slots = "Shield" };
var items = new[] { sword, cuirass, shield }.ToDictionary(i => i.FormKey);
var constants = new Dictionary<string, double> { ["fDamageWeaponMult"] = 1, ["fDamageStrengthBase"] = 1, ["fDamageStrengthMult"] = 0,
    ["fDamageSkillBase"] = 1, ["fDamageSkillMult"] = 0, ["fDamageWeaponConditionBase"] = 1, ["fDamageWeaponConditionMult"] = 0,
    ["fArmorRatingBase"] = 1, ["fArmorRatingMax"] = 1, ["fBlockSkillBase"] = .5, ["fBlockSkillMult"] = 0,
    ["fBlockAmountWeaponMult"] = .5, ["fBlockMax"] = .75, ["fFatigueBase"] = 1, ["fFatigueMult"] = .5 };
var scenario = new CombatScenario { Player = new() { Weapon = sword.FormKey }, Enemy = new() { Weapon = sword.FormKey }, Seconds = 60, UseNativeResources = false };
settings.Styles["Aggressive Fighter"] = new() { AttacksPerSecond = 1, ContactRate = 1, BlockUptime = 0, PowerAttackShare = 0 };
settings.AttackFatigueBase = settings.AttackFatigueWeight = settings.BlockFatigueBase = settings.BlockFatigueWeight = 0;
Near(PhysicalBalance.Propose(sword, settings).Damage, 20, "Neutral modifiers preserve source");
var baseline = CombatAnalysis.Run(scenario, settings, items, constants, false);
Near(baseline.PlayerCleanHit, 20, "Known damage formula"); Near(baseline.EnemyTimeToDefeat!.Value, 7, "Known equal duel duration");
Check(baseline.PlayerTimeToDefeat == baseline.EnemyTimeToDefeat, "Simultaneous defeat must be retained");
scenario.Enemy.Armor = [cuirass.FormKey]; var armored = CombatAnalysis.Run(scenario, settings, items, constants, false);
Near(armored.PlayerCleanHit, 16, "Armor mitigation");
constants["fArmorRatingBase"] = 0; scenario.Enemy.HeavyArmorSkill = 100; scenario.Enemy.LightArmorSkill = 0;
Near(CombatAnalysis.Run(scenario, settings, items, constants, false).PlayerCleanHit, 16, "Heavy pieces use heavy armor skill");
scenario.Enemy.HeavyArmorSkill = 0;
Near(CombatAnalysis.Run(scenario, settings, items, constants, false).PlayerCleanHit, 20, "Light skill cannot substitute for heavy skill");
constants["fArmorRatingBase"] = 1; scenario.Enemy.HeavyArmorSkill = scenario.Enemy.LightArmorSkill = 50;
scenario.Player.Armor = [shield.FormKey]; settings.Styles["Aggressive Fighter"].BlockUptime = 1;
var blocking = CombatAnalysis.Run(scenario, settings, items, constants, false); Near(blocking.PlayerBlockReduction, .5, "Shield blocking"); Near(blocking.EnemyBlockReduction, .25, "Weapon blocking");
settings.Materials["Iron"].Damage = 2;
var increased = CombatAnalysis.Run(scenario, settings, items, constants, true); Near(increased.PlayerCleanHit, armored.PlayerCleanHit * 2, "Material modifiers applied");
var protectedSword = sword with { Protected = true, PreservationReason = "Artifact" }; Near(PhysicalBalance.Propose(protectedSword, settings).Damage, 20, "Artifact protection");
settings.ItemOverrides[sword.FormKey] = new() { NormalizeProtected = true, Modifiers = new() { Damage = 1.5 } };
Near(PhysicalBalance.Propose(protectedSword, settings).Damage, 60, "Explicit artifact exception");
settings.ItemOverrides[sword.FormKey].Preserve = true; Near(PhysicalBalance.Propose(sword, settings).Damage, 20, "Preserve takes precedence");
// A profile reassignment must not invent native resistance bypass or change handedness.
scenario.Player.Armor = []; scenario.Enemy.Armor = []; scenario.Enemy.ResistNormalWeapons = 100;
settings.ItemOverrides.Clear(); settings.ItemOverrides[sword.FormKey] = new() { Material = "Silver" };
Near(CombatAnalysis.Run(scenario, settings, items, constants, true).PlayerCleanHit, 0, "Material profile cannot bypass native resistance");
items[sword.FormKey] = sword with { IgnoresNormalWeaponResistance = true };
Check(CombatAnalysis.Run(scenario, settings, items, constants, true).PlayerCleanHit > 0, "Native resistance bypass flag ignored");
items[sword.FormKey] = sword; scenario.Enemy.ResistNormalWeapons = 0; settings.ItemOverrides.Clear();
scenario.Player.Armor = []; scenario.Enemy.Armor = []; settings.Styles["Aggressive Fighter"].BlockUptime = 0;
scenario.Player.Health = 10000; scenario.Enemy.Health = 1; scenario.EnemyCount = 3;
var group = CombatAnalysis.Run(scenario, settings, items, constants, false);
Check(group.EnemyHealthRemaining == 0 && group.Timeline[^1].EnemiesAlive == 0, "Group aggregate health must end at zero");
Check(group.Timeline.All(p => p.EnemyHealth >= 0 && p.PlayerHealth >= 0), "Timeline health cannot be negative");
scenario.Player.Armor = [cuirass.FormKey, cuirass.FormKey]; Reject(() => CombatAnalysis.Validate(scenario, settings, items), "Duplicate equipment accepted");
scenario.Player.Armor = [cuirass.FormKey, shield.FormKey]; items[shield.FormKey] = shield with { Slots = "UpperBody" };
Reject(() => CombatAnalysis.Validate(scenario, settings, items), "Overlapping armor accepted"); items[shield.FormKey] = shield;
items[sword.FormKey] = sword with { Class = "Claymore" }; Reject(() => CombatAnalysis.Validate(scenario, settings, items), "Two-handed shield loadout accepted"); items[sword.FormKey] = sword;
scenario.Player.Armor = []; scenario.EnemyCount = 1; scenario.Enemy.Health = 10000;
scenario.Player.StartingFatiguePercent = 0; var tired = CombatAnalysis.Run(scenario, settings, items, constants, false);
Check(tired.PlayerHealthRemaining < scenario.Player.Health, "Low-fatigue scenario should receive damage");
settings.AttackFatigueBase = 50; scenario.Player.StartingFatiguePercent = 100; scenario.Player.FatigueRegen = 0;
var fatigue = CombatAnalysis.Run(scenario, settings, items, constants, false); Check(fatigue.PlayerFatigueRemaining == 0, "Fatigue exhaustion");
scenario.Seconds = double.NaN; Reject(() => CombatAnalysis.Validate(scenario, settings, items), "Nonfinite duration accepted"); scenario.Seconds = 60;
Reject(() => CombatConfiguration.Parse("{\"Styles\":null}"), "Null styles accepted");
Reject(() => CombatConfiguration.Parse("{\"Unknown\":true}"), "Unknown configuration property accepted");
Check(settings.OffenseTarget.At(8) - settings.OffenseTarget.At(7) > settings.OffenseTarget.At(4) - settings.OffenseTarget.At(3), "Rare-tier curve must steepen");

// Write/re-read native fields to verify preview rounding and preserve unrelated data.
var source = new OblivionMod(ModKey.FromNameAndExtension("Test.esp"), OblivionRelease.Oblivion);
var nativeWeapon = source.Weapons.AddNew(); nativeWeapon.EditorID = "WeapIronLongsword";
nativeWeapon.Data = new() { Damage = 20, Speed = 1, Reach = 1, Weight = 10, Health = 100, Value = 25 };
var nativeArmor = source.Armors.AddNew(); nativeArmor.EditorID = "ArmorIronCuirass";
nativeArmor.Data = new() { ArmorValue = 20, Weight = 20, Health = 200, Value = 50 };
nativeArmor.ClothingFlags = new() { BipedFlags = BipedFlag.UpperBody, GeneralFlags = (EquipmentFlag)0x80 };
Check(PhysicalCombatModule.ReadItem(nativeArmor,[])!.Heavy,"Native heavy-armor bit must use the serialized flag byte");
var readWeapon = PhysicalCombatModule.ReadItem(nativeWeapon, [])!; var readArmor = PhysicalCombatModule.ReadItem(nativeArmor, [])!;
settings.ItemOverrides.Clear(); settings.ItemOverrides[sword.FormKey.ToLowerInvariant()] = new() { Preserve = true };
CombatConfiguration.Validate(settings); Check(settings.ItemOverrides.ContainsKey(sword.FormKey), "FormKey override matching must ignore case");
settings.ItemOverrides.Clear(); settings.Materials["Iron"].Armor = 1.234;
var weaponProposal = PhysicalBalance.Propose(readWeapon, settings); var armorProposal = PhysicalBalance.Propose(readArmor, settings);
PhysicalCombatModule.Apply(nativeWeapon, weaponProposal); PhysicalCombatModule.Apply(nativeArmor, armorProposal);
Directory.CreateDirectory("artifacts/combat-review/fixture"); var path = "artifacts/combat-review/fixture/Test.esp"; source.WriteToBinary(path);
using var loaded = OblivionMod.CreateFromBinaryOverlay(path, OblivionRelease.Oblivion);
Near(loaded.Weapons.Single().Data!.Damage, weaponProposal.Damage, "Native weapon preview parity");
Near(loaded.Armors.Single().Data!.ArmorValue, armorProposal.Armor, "Native armor preview parity");
Check(PhysicalCombatModule.ReadItem(loaded.Armors.Single(),[])!.Heavy,"Native heavy classification lost in roundtrip");
Check(loaded.Weapons.Single().Data!.Value == 25 && loaded.Armors.Single().Data!.Value == 50, "Economy values changed");
var gameplaySettings=CombatConfiguration.Load("BaldursGateStyleOblivion/combat.json");
var master=CombatBuilds.AtLevel(gameplaySettings.Gameplay,"Knight",5,20,new BaldursGateStyleOblivion.Classification.ClassificationSettings().LevelMapping);
Near(master["Blade"],100,"Tier 5 specialty mastery");Near(master["Block"],100,"Defensive mastery");
var offensive=CombatBuilds.AtLevel(gameplaySettings.Gameplay,"Barbarian",5,20,new BaldursGateStyleOblivion.Classification.ClassificationSettings().LevelMapping);
Near(master["Health"],offensive["Health"],"Same-tier humanoid health starts neutral");
Near(master["Endurance"],offensive["Endurance"],"No forced defensive health versus offensive Endurance tradeoff");
var interpolated=CombatBuilds.AtLevel(gameplaySettings.Gameplay,"Knight",3,12,new BaldursGateStyleOblivion.Classification.ClassificationSettings().LevelMapping);
Near(interpolated["Blade"],73,"Shared level interpolation");
var normalized=PhysicalBalance.Propose(sword,gameplaySettings);Near(normalized.Damage,12,"Absolute weapon baseline");
Near(normalized.Weight,12,"Absolute weapon weight");
var nativeStats=new Npc(FormKey.Factory("000010:Test.esp"),OblivionRelease.Oblivion){Stats=new NpcData(),Configuration=new NpcConfiguration()};
GameplayCombatModule.Stats(nativeStats,master);Near(nativeStats.Stats!.Health,master["Health"],"Native explicit health");Near(nativeStats.Configuration!.Fatigue,master["Fatigue"],"Native fatigue reserves");
Check(!nativeStats.Configuration!.Flags.HasFlag(Npc.NpcFlag.AutoCalcStats),"Native auto calculation should be disabled");
Console.WriteLine("Combat formulas, normalization, shared builds, tier-5 mastery and native stat checks passed.");
using(var vanilla=OblivionMod.CreateFromBinaryOverlay("F:/SteamLibrary/steamapps/common/Oblivion/Data/Oblivion.esm",OblivionRelease.Oblivion))
{
    var records=vanilla.EnumerateMajorRecords().ToDictionary(r=>r.FormKey,r=>(Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter)r);
    var gs=records.Values.OfType<IGameSettingFloatGetter>().Where(g=>g.EditorID is not null&&g.Data.HasValue).ToDictionary(g=>g.EditorID!,g=>(double)g.Data!.Value);
    foreach(var setting in records.Values.OfType<IGameSettingIntGetter>().Where(g=>g.EditorID is not null&&g.Data.HasValue))gs[setting.EditorID!]=setting.Data!.Value;
    var creation=CharacterCreation.Read(records,gs,PlayableRaces.Read("F:/SteamLibrary/steamapps/common/Oblivion/Data/Oblivion.esm",vanilla));
    Check(creation.Races.Length==10,"Native playable race filter");
    var imperial=creation.Races.Single(r=>r.Name=="Imperial");
    var player=PlayerBuilds.AtLevel(gameplaySettings.Gameplay,creation,"Warrior",imperial.Key,false,null,1).Stats;
    Near(player["Strength"],45,"Native Imperial Warrior starting Strength");Near(player["Blade"],35,"Native Imperial Warrior starting Blade");Near(player["Block"],30,"Native Warrior starting Block");
    var playerMaster=PlayerBuilds.AtLevel(gameplaySettings.Gameplay,creation,"Warrior",imperial.Key,false,null,20).Stats;
    Near(playerMaster["Blade"],100,"Player estimated major skill mastery at level 20");
    var sign=creation.Birthsigns.Single(s=>s.Name=="The Warrior");
    var signed=PlayerBuilds.AtLevel(gameplaySettings.Gameplay,creation,"Warrior",imperial.Key,false,sign.Key,1).Stats;
    Near(signed["Strength"],55,"Warrior birthsign passive attribute bonus");
    var trained=0;
    foreach(var npc in vanilla.Npcs)
    {
        var stats=CombatBuilds.AtLevel(gameplaySettings.Gameplay,"Civilian",0,1,new BaldursGateStyleOblivion.Classification.ClassificationSettings().LevelMapping);
        if(CharacterCreation.TrainerFloor(npc,records.GetValueOrDefault(npc.Class.FormKey) as IClassGetter,stats) is not null)
        {
            var ai=npc.AIData!;var skill=ai.Teaches!.Value.ToString().Replace("Speechraft","Speechcraft");
            var written=new Npc(FormKey.Factory("000011:Test.esp"),OblivionRelease.Oblivion){Stats=new NpcData(),Configuration=new NpcConfiguration()};GameplayCombatModule.Stats(written,stats);
            Near(Convert.ToDouble(typeof(NpcData).GetProperty(skill)!.GetValue(written.Stats)),stats[skill],"Native trainer skill write");
            Check(stats[skill]>=ai.MaximumTrainingLevel,"Trainer floor below service cap");Near(stats["Health"],65,"Trainer floor must not boost health");trained++;
        }
    }
    Check(trained>=100,"Native trainer coverage missing");
    Console.WriteLine($"Native creation and trainer checks passed: {creation.Races.Length} races, {creation.Classes.Length} playable classes, {trained} trainers.");
}Near(new CombatHitFactors(12,3,.725,.415,1,1,.97,1,1,1).Damage,10.506555,"Measured iron sword calibration");
Near(new CombatHitFactors(31,3,.725,.415,1,1,.97,1,1,1).Damage,27.14193375,"Measured Daedric sword calibration");
Near(new CombatHitFactors(26,3,.775,.595,1,1,1,1,1,1).Damage,35.96775,"Measured warhammer calibration");
Near(35.96775*(1-.3*.5),30.5725875,"Measured weapon blocking calibration");