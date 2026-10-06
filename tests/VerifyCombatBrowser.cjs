const fs = require('node:fs');
const path = require('node:path');
const {spawn} = require('node:child_process');
const net = require('node:net');
const {chromium} = require(process.env.PLAYWRIGHT_MODULE || 'C:/Users/kevle/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const check = (ok, message) => { if (!ok) throw Error(message); };
(async () => {
  const directory = fs.mkdtempSync(path.resolve('artifacts/combat-browser-'));
  for (const name of fs.readdirSync('BaldursGateStyleOblivion').filter(name => name.endsWith('.json')))
    fs.copyFileSync(path.join('BaldursGateStyleOblivion', name), path.join(directory, name));
  const probe = net.createServer();
  await new Promise(resolve => probe.listen(0, '127.0.0.1', resolve));
  const port = probe.address().port;
  await new Promise(resolve => probe.close(resolve));
  const server = spawn('dotnet', [process.env.COMBAT_EDITOR_DLL || 'tools/ActorResearch/bin/Debug/net10.0/ActorResearch.dll', 'edit', '--no-open', '--port', String(port), '--config', path.join(directory, 'actor-classification.json')]);
  let log = '';
  server.stdout.on('data', value => log += value); server.stderr.on('data', value => log += value);
  let browser;
  try {
    for (let i = 0; i < 60; i++) {
      try { if ((await fetch(`http://127.0.0.1:${port}/combat`)).ok) break; } catch {}
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    browser = await chromium.launch({executablePath: process.env.EDGE_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', headless: true});
    const page = await browser.newPage({viewport: {width: 1500, height: 1000}});
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto(`http://127.0.0.1:${port}/combat`);
    await page.waitForFunction(() => document.getElementById('metrics').children.length > 0);
    check(await page.evaluate(() => scenario.Player.Tier === 2 && scenario.Enemy.Tier === 4), 'Default quick tier 2 versus tier 4 matchup');
    check(await page.locator('#customBuilds').getAttribute('open') === null, 'Detailed inputs should start collapsed');
    await page.locator('#quickPlayerTier').selectOption('0');
    await page.waitForFunction(()=>scenario.Player.Tier===0&&scenario.Player.Strength===45&&scenario.Player.WeaponSkill===35);
    check(await page.evaluate(()=>scenario.Player.BlockSkill===30),'Native Warrior starting Block');
    check((await page.locator('#quickSkillCalcs').innerText()).includes('loaded race'),'Player stat provenance');
    await page.locator('#quickPlayerBirthsign').selectOption({label:'The Warrior'});
    await page.waitForFunction(()=>scenario.Player.Strength===55);
    await page.locator('#quickPlayerBirthsign').selectOption('');
    await page.locator('#quickPlayerTier').selectOption('2');
    await page.waitForFunction(()=>scenario.Player.Tier===2&&scenario.Player.Strength===55);
    check(await page.evaluate(() => !scenario.Player.UseTierTargets && !scenario.Enemy.UseTierTargets && result.Proposed.PlayerHitFactors.BenchmarkMultiplier === 1), 'Quick matchup must not invent tier damage');
    check(await page.locator('#quickPlayerTier option:checked').innerText() === 'Tier 2 · level 5', 'Player tier level reference');
    check(await page.locator('#quickEnemyTier option:checked').innerText() === 'Tier 4 · level 15', 'Enemy tier level reference');
    const originalStats=await page.evaluate(()=>({health:scenario.Player.Health,skill:scenario.Player.WeaponSkill,strength:scenario.Player.Strength}));
    await page.locator('#quickPlayerArmorMaterial').selectOption('None');
    await page.waitForFunction(()=>scenario.Player.Armor.length===0&&result?.Proposed.PlayerArmor===0);
    check((await page.locator('#quickBuildSummary').innerText()).includes('no armor'),'No-armor summary missing');
    const unarmoredHit=await page.evaluate(()=>result.Proposed.EnemyCleanHit);
    await page.locator('#quickPlayerArmorMaterial').selectOption('Iron');
    await page.waitForFunction(()=>scenario.Player.Armor.length===6&&result?.Proposed.PlayerArmor>0);
    const iron=await page.evaluate(()=>({armor:result.Proposed.PlayerArmor,hit:result.Proposed.EnemyCleanHit}));
    check(iron.hit<unarmoredHit,'Iron armor must reduce incoming damage');
    await page.locator('#quickPlayerArmorMaterial').selectOption('Steel');
    await page.waitForFunction(()=>scenario.Player.Armor.every(key=>data.Catalog.Items.find(item=>item.FormKey===key).Material==='Steel')&&result?.Proposed.PlayerArmor>0);
    await page.evaluate(async()=>{await preview();});
    check(await page.evaluate(()=>result.Proposed.PlayerArmor)>iron.armor,'Steel should provide more protection than iron at the same skills');
    check(await page.locator('#quickPlayerShieldMaterial option').first().innerText()==='Preset default · Steel','Default shield label must track the armor override');
    check(JSON.stringify(await page.evaluate(()=>({health:scenario.Player.Health,skill:scenario.Player.WeaponSkill,strength:scenario.Player.Strength})))===JSON.stringify(originalStats),'Gear override changed character stats');
    await page.locator('#quickPlayerArmorMaterial').selectOption('None');
    await page.locator('#quickPlayerShieldMaterial').selectOption('Iron');
    await page.waitForFunction(()=>scenario.Player.Armor.length===1&&data.Catalog.Items.find(i=>i.FormKey===scenario.Player.Armor[0]).Kind==='Shield');
    await page.locator('#quickPlayerShieldMaterial').selectOption('None');
    await page.waitForFunction(()=>scenario.Player.Armor.length===0&&result?.Proposed.PlayerArmor===0);
    await page.locator('#quickPlayerWeaponMaterial').selectOption('Steel');
    await page.waitForFunction(()=>data.Catalog.Items.find(i=>i.FormKey===scenario.Player.Weapon).Material==='Steel'&&scenario.Player.WeaponPower===1);
    for(const [kind,value] of [['Weapon','Tier default'],['Armor','Tier default'],['Shield','Preset default']])await page.locator('#quickPlayer'+kind+'Material').selectOption(value);
    await page.waitForFunction(()=>scenario.Player.Armor.length===6&&data.Catalog.Items.find(i=>i.FormKey===scenario.Player.Weapon).Material==='Iron');
    await page.locator('[data-tab=targets]').click();
    check(await page.locator('#curveRows tr').nth(4).locator('td').nth(1).innerText() === '15', 'Curve table level reference');
    check(await page.locator('#curveRows tr').nth(10).locator('td').nth(1).innerText() === 'Individual / apex', 'Apex does not invent a universal level');
    await page.locator('[data-tab=scenarios]').click();
    await page.locator('#quickPlayerTier').selectOption('1');
    await page.locator('#quickEnemyTier').selectOption('6');
    await page.waitForFunction(() => result && scenario.Player.Tier === 1 && scenario.Enemy.Tier === 6 && document.getElementById('quickSummary').textContent.includes('Tier 1 player vs tier 6'));
    check(await page.evaluate(() => scenario.Player.Armor.length > 0 && scenario.Enemy.Weapon !== scenario.Player.Weapon), 'Quick tiers should supply equipment');
    await page.locator('#quickPlayerTier').selectOption('2');
    await page.locator('#quickEnemyTier').selectOption('4');
    await page.waitForFunction(() => result && scenario.Player.Tier === 2 && scenario.Enemy.Tier === 4);
    check(await page.evaluate(()=>data.Catalog.Items.find(i=>i.EditorID==='IronCuirass').Heavy && !data.Catalog.Items.find(i=>i.EditorID==='GlassCuirass').Heavy),'Native heavy/light armor classification');
    await page.locator('#quickPlayerLevel').fill('12');
    await page.locator('#quickPlayerClass').selectOption('Knight');
    await page.waitForFunction(() => scenario.Player.Tier === 3 && scenario.Player.BlockSkill === 71 && scenario.Player.WeaponSkill === 73 && result);
    check(await page.evaluate(() => scenario.Player.Strength > 50), 'Intermediate levels should interpolate stats');
    check((await page.locator('#quickSkillCalcs').innerText()).includes('73'), 'Visible interpolated class skill calculation');
    for(const className of ['Warrior','Knight','Barbarian','Rogue','Scout','Spellsword','Battlemage']){
      await page.locator('#quickPlayerClass').selectOption(className);
      await page.waitForFunction(name => scenario.Player.Name.includes(name),className);
      await page.locator('#analyze').click();
      await page.waitForFunction(() => result && document.getElementById('metrics').children.length > 0);
    }
    for(const equipment of ['Sword & shield','Two-handed / heavy armor','Two-handed / light armor','Sword / light armor','Dagger / light armor','Mace & shield','Warhammer / heavy armor','Axe & shield']){
      await page.locator('#quickPlayerEquipment').selectOption(equipment);
      await page.locator('#analyze').click();
      await page.waitForFunction(() => result && document.getElementById('metrics').children.length > 0);
      check(await page.evaluate(() => scenario.Player.Armor.length > 0), 'Preset should include armor');
    }
    await page.locator('#quickPlayerClass').selectOption('Warrior');
    await page.locator('#quickPlayerEquipment').selectOption('Class default');
    await page.locator('#quickPlayerLevel').fill('5');
    await page.waitForFunction(() => scenario.Player.Tier === 2 && result);
    check(await page.locator('.editor-nav a[aria-current=page]').innerText() === 'Combat workbench', 'Navigation selection');
    await page.locator('[data-tab=gameplay]').click();
    check((await page.locator('#actorTiers').innerText()).includes('Specialty'), 'Playable actor budgets missing');
    const engineRow=page.locator('#engineSettings tr').filter({hasText:'fDamageWeaponMult'});
    await engineRow.locator('input').fill('2');
    await page.waitForFunction(()=>result.Proposed.PlayerHitFactors.WeaponMultiplier===2);
    await engineRow.locator('input').fill('3');
    await page.locator('[data-tab=scenarios]').click();
    await page.locator('#quickPlayerTier').selectOption('5');
    await page.locator('#quickPlayerClass').selectOption('Knight');
    await page.waitForFunction(()=>scenario.Player.WeaponSkill===100&&scenario.Player.BlockSkill===100);
    await page.locator('#quickPlayerTier').selectOption('2');
    await page.locator('#quickPlayerClass').selectOption('Warrior');
    await page.waitForFunction(()=>scenario.Player.WeaponSkill===49);
    await page.locator('[data-tab=equipment]').click();
    await page.locator('#materials tbody tr').filter({hasText: 'Iron'}).locator('input').first().fill('1.5');
    await page.waitForFunction(() => result?.Proposed.PlayerCleanHit > result?.Current.PlayerCleanHit);
    const classRow = page.locator('#weaponClasses tbody tr').filter({hasText: 'Longsword'});
    await classRow.locator('input').first().fill('1.25');
    await page.waitForFunction(() => result?.Items.find(i => i.Before.FormKey === '000C0C:Oblivion.esm').After.Damage === 23);
    await page.locator('#save').click();
    await page.waitForFunction(() => document.getElementById('status').textContent.includes('profiles saved'));
    check(JSON.parse(fs.readFileSync(path.join(directory, 'combat.json'), 'utf8')).WeaponClasses.Longsword.Damage === 1.25, 'Class modifier save');
    await page.locator('[data-tab=scenarios]').click();
    await page.locator('#preset').selectOption('exhausted');
    await page.waitForFunction(() => scenario.Player.Tier === 3 && scenario.Player.StartingFatiguePercent === 20 && Math.abs(result?.Proposed.Timeline[0].PlayerFatigue - scenario.Player.MaxFatigue * .2) < .0001);
    await page.locator('#preset').selectOption('boss');
    await page.waitForFunction(() => scenario.Enemy.Tier === 7 && scenario.Enemy.Style === 'Boss' && result?.Proposed.Timeline[0].EnemyHealth === scenario.Enemy.Health && result?.Proposed.EnemyCleanHit > result?.Current.EnemyCleanHit);
    check(await page.evaluate(()=>scenario.Enemy.WeaponPower===settings.Gameplay.RareWeaponGrowth),'Tier default rare variant missing');
    await page.locator('#quickEnemyWeaponMaterial').selectOption('Iron');
    await page.locator('#quickEnemyArmorMaterial').selectOption('Steel');
    await page.waitForFunction(()=>scenario.Enemy.WeaponPower===1&&data.Catalog.Items.find(i=>i.FormKey===scenario.Enemy.Weapon).Material==='Iron');
    const downloadEvent = page.waitForEvent('download'); await page.locator('#export').click();
    const download = await downloadEvent, experiment = path.join(directory, 'experiment.json'); await download.saveAs(experiment);
    await page.locator('#preset').selectOption('equal');
    await page.waitForFunction(() => scenario.Enemy.Tier === 3);
    await page.locator('#import').setInputFiles(experiment);
    await page.waitForFunction(() => scenario.Enemy.Tier === 7 && document.getElementById('status').textContent.includes('unsaved'));
    check(await page.evaluate(() => !scenario.Enemy.UseTierTargets), 'Imported scenario preserves disabled tier benchmarks');
    check(await page.locator('#quickEnemyWeaponMaterial').inputValue()==='Iron'&&await page.locator('#quickEnemyArmorMaterial').inputValue()==='Steel','Material overrides lost during experiment import');

    // Real overlapping slots must invalidate a preview and clear stale metrics.
    const keys = await page.evaluate(() => {
      const armor = data.Catalog.Items.filter(i => i.Kind === 'Armor' && i.Slots && !i.Protected);
      for (const first of armor) { const second = armor.find(i => i.FormKey !== first.FormKey && i.Slots === first.Slots); if (second) return [first.FormKey, second.FormKey]; }
    });
    check(keys?.length === 2, 'No overlap fixture in native catalog');
    await page.locator('#customBuilds > summary').click();
    while (await page.locator('#fighters .card').first().locator('.armor-list button').count())
      await page.locator('#fighters .card').first().locator('.armor-list button').last().click();
    for (const key of keys) await page.locator('#fighters .card').first().locator('select').last().selectOption(key);
    await page.waitForFunction(() => document.getElementById('status').textContent.includes('Overlapping armor slot'));
    check(await page.locator('#metrics tr').count() === 0, 'Invalid scenario retained stale result metrics');
    await page.locator('#fighters .card').first().locator('.armor-list button').last().click();
    await page.waitForFunction(() => document.getElementById('metrics').children.length > 0);
    await page.locator('[data-tab=styles]').click();
    check((await page.locator('#styleDetail').innerText()).includes('PowerAttack'), 'Native style reference missing');
    await page.locator('[data-tab=targets]').click(); await page.screenshot({path: path.join(directory, 'targets.png'), fullPage: true});
    await page.setViewportSize({width: 390, height: 844});
    await page.locator('[data-tab=scenarios]').click();
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2), 'Mobile page overflows horizontally');
    await page.screenshot({path: path.join(directory, 'mobile.png'), fullPage: true});
    check(errors.length === 0, 'Browser errors: ' + errors.join('; '));
    console.log('Combat browser controls, live comparisons, saved class modifiers, presets, import/export, slot validation, native styles, and responsive layout passed.');
  } finally {
    if (browser) await browser.close(); server.kill();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
