import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';

const root = path.resolve(import.meta.dirname, '..');
const html = fs.readFileSync(path.join(root, 'index.html'), 'utf8');
const scripts = [...html.matchAll(/<script src="([^"]+)"/g)].map(match => match[1]);

class ClassList {
  values = new Set();
  add(...items) { items.forEach(item => this.values.add(item)); }
  remove(...items) { items.forEach(item => this.values.delete(item)); }
  toggle(item, force) {
    const next = force === undefined ? !this.values.has(item) : force;
    next ? this.values.add(item) : this.values.delete(item);
    return next;
  }
  contains(item) { return this.values.has(item); }
}

const gradient = { addColorStop() {} };
const canvasContext = new Proxy({}, {
  get(target, key) {
    if (key === 'createLinearGradient' || key === 'createRadialGradient') return () => gradient;
    if (!(key in target)) target[key] = () => {};
    return target[key];
  },
  set(target, key, value) { target[key] = value; return true; },
});

class FakeElement {
  constructor(tag = 'div') {
    this.tagName = tag.toUpperCase();
    this.classList = new ClassList();
    this.style = {};
    this.children = [];
    this.clientWidth = 238;
    this.clientHeight = 136;
    this.value = '';
    this.files = [];
    this.textContent = '';
    this.innerHTML = '';
    this.listeners = new Map();
  }
  addEventListener(type, listener) {
    const listeners = this.listeners.get(type) || [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }
  dispatchEvent(event) {
    event.target ??= this;
    event.currentTarget = this;
    event.preventDefault ??= () => {};
    for (const listener of this.listeners.get(event.type) || []) listener(event);
    return true;
  }
  appendChild(child) { this.children.push(child); child.parent = this; return child; }
  prepend(child) { this.children.unshift(child); child.parent = this; }
  remove() { if (this.parent) this.parent.children = this.parent.children.filter(child => child !== this); }
  get lastElementChild() { return this.children.at(-1) || null; }
  getContext() { return canvasContext; }
  getBoundingClientRect() { return { left: 0, top: 0, width: this.clientWidth, height: this.clientHeight, right: this.clientWidth, bottom: this.clientHeight }; }
  setPointerCapture() {}
  click() {}
}

const elements = new Map();
const getElement = selector => {
  if (!elements.has(selector)) {
    const element = new FakeElement(selector.includes('canvas') || selector === '#world' || selector === '#minimap' ? 'canvas' : 'div');
    if (selector === '#game') element.classList.add('hidden');
    if (selector.includes('Overlay') || selector === '#tooltip' || selector === '#buildHint' || selector === '#continueBtn') element.classList.add('hidden');
    elements.set(selector, element);
  }
  return elements.get(selector);
};

const saved = new Map();
const localStorage = {
  getItem(key) { return saved.has(key) ? saved.get(key) : null; },
  setItem(key, value) { saved.set(key, String(value)); },
  removeItem(key) { saved.delete(key); },
};

const document = {
  body: new FakeElement('body'),
  hidden: false,
  querySelector: getElement,
  querySelectorAll() { return []; },
  createElement(tag) { return new FakeElement(tag); },
  addEventListener() {},
};

const context = vm.createContext({
  console,
  document,
  localStorage,
  innerWidth: 1440,
  innerHeight: 900,
  devicePixelRatio: 1,
  performance: { now: () => 1000 },
  matchMedia: () => ({ matches: false }),
  addEventListener() {},
  requestAnimationFrame: () => 1,
  cancelAnimationFrame() {},
  setTimeout: () => 1,
  clearTimeout() {},
  setInterval: () => 1,
  clearInterval() {},
  Blob,
  URL,
  Date,
  Math,
  JSON,
  Uint8Array,
  Float32Array,
  Int32Array,
  Set,
  Map,
  Intl,
});
context.window = context;

for (const relative of scripts) {
  const source = fs.readFileSync(path.join(root, relative), 'utf8');
  vm.runInContext(source, context, { filename: relative });
}

const result = vm.runInContext(`(() => {
  newGame();
  const initial = { entities: game.entities.length, nodes: game.nodes.length, sites: game.sites.length, popCap: game.player.popCap, players: game.players.length };
  const worldCanvas=document.querySelector('#world'),openingCamera={x:game.camera.x,y:game.camera.y,zoom:game.camera.zoom};
  const openingAnchor={x:mouse.x,y:mouse.y,inside:mouse.inside,down:mouse.down,pan:mouse.pan};
  for(let i=0;i<120;i++)updateCamera(STEP);
  const stationaryBeforeEntry=Math.abs(game.camera.x-openingCamera.x)<1e-9&&Math.abs(game.camera.y-openingCamera.y)<1e-9;
  worldCanvas.dispatchEvent({type:'pointerenter',clientX:viewW-1,clientY:100,pointerId:1,pointerType:'mouse',button:0,timeStamp:1000});
  const beforeEdgeScroll={x:game.camera.x,y:game.camera.y};updateCamera(STEP);
  const edgeScrollAfterEntry=game.camera.x>beforeEdgeScroll.x;
  worldCanvas.dispatchEvent({type:'pointerleave',clientX:viewW+1,clientY:100,pointerId:1,pointerType:'mouse',button:0,timeStamp:1010});
  centerCamera(game.spawn[0].x,game.spawn[0].y);resetCameraInputAnchor();
  const beforeKeyboard={x:game.camera.x,y:game.camera.y};keys.add('d');updateCamera(STEP);keys.clear();
  const keyboardPan=game.camera.x>beforeKeyboard.x;
  centerCamera(game.spawn[0].x,game.spawn[0].y);resetCameraInputAnchor();
  const beforeRightDrag={x:game.camera.x,y:game.camera.y};
  worldCanvas.dispatchEvent({type:'pointerdown',clientX:viewW/2,clientY:viewH/2,pointerId:2,pointerType:'mouse',button:2,timeStamp:1100});
  worldCanvas.dispatchEvent({type:'pointermove',clientX:viewW/2-48,clientY:viewH/2,pointerId:2,pointerType:'mouse',button:2,timeStamp:1120});
  worldCanvas.dispatchEvent({type:'pointerup',clientX:viewW/2-48,clientY:viewH/2,pointerId:2,pointerType:'mouse',button:2,timeStamp:1140});
  const rightDragPan=game.camera.x>beforeRightDrag.x&&!mouse.down&&!mouse.pan;
  const openingCameraInput={
    anchorCentered:Math.abs(openingAnchor.x-viewW*.5)<1e-9&&Math.abs(openingAnchor.y-viewH*.5)<1e-9,
    initiallyOutside:!openingAnchor.inside&&!openingAnchor.down&&!openingAnchor.pan,
    stationaryBeforeEntry,
    edgeScrollAfterEntry,
    keyboardPan,
    rightDragPan,
  };
  centerCamera(game.spawn[0].x,game.spawn[0].y);resetCameraInputAnchor();
  const civKeys = Object.keys(CIVS);
  const canonicalUnique = {britons:'longbowman',byzantines:'cataphract',celts:'woadRaider',chinese:'chuKoNu',franks:'throwingAxeman',goths:'huskarl',japanese:'samurai',mongols:'mangudai',persians:'warElephant',saracens:'mameluke',teutons:'teutonicKnight',turks:'janissary',vikings:'berserk'};
  const roster = {
    keys: civKeys,
    uniqueUnits: new Set(Object.values(CIVS).map(c => c.unique)).size,
    dataDriven: Object.values(CIVS).every(c => c.mods && c.powerMods && Array.isArray(c.pros) && c.pros.length && Array.isArray(c.cons) && c.cons.length && UNIT[c.unique]),
    canonicalUnique: Object.entries(canonicalUnique).every(([civ,type]) => CIVS[civ]?.unique===type),
    castleAgeUnique: Object.values(CIVS).every(c => UNIT[c.unique]?.age===3 && UNIT[c.unique]?.trainAt==='castle'),
  };
  const progressionData = {
    ages:[...AGES],
    tiers:Object.fromEntries(['town','house','mill','lumber','farm','barracks','range','stable','blacksmith','tower','wall','workshop','castle','wonder'].map(type=>[type,BUILD[type]?.age??null])),
    buildOrder:[...BUILD_ORDER],
  };
  const projectionSamples = [[0,0],[420,1635],[WORLD_W/2,WORLD_H/2],[WORLD_W,WORLD_H]].map(([x,y]) => {
    const s = worldToScreen(x,y), w = screenToWorld(s.x,s.y);
    return Math.hypot(w.x-x,w.y-y);
  });
  const axisOrigin = worldToScreen(WORLD_W/2,WORLD_H/2);
  const axisX = worldToScreen(WORLD_W/2+100,WORLD_H/2);
  const axisY = worldToScreen(WORLD_W/2,WORLD_H/2+100);
  const projection = {
    id: PROJECTION_ID,
    roundTripError: Math.max(...projectionSamples),
    xCrossTalk: Math.abs(axisX.y-axisOrigin.y),
    yCrossTalk: Math.abs(axisY.x-axisOrigin.x),
    xDirection: axisX.x-axisOrigin.x,
    yDirection: axisY.y-axisOrigin.y,
  };
  const worker = game.entities.find(entity => entity.faction === 0 && entity.type === 'villager');
  const tree = nearestResource(worker, 'wood');
  const woodBefore = game.player.res.wood;
  setGather(worker, tree);
  for (let i = 0; i < 450; i++) updateGame(STEP);
  const gatheredWood = game.player.res.wood - woodBefore;

  let buildPoint = null;
  for (let y = 1200; y < 1800 && !buildPoint; y += 60) for (let x = 240; x < 900; x += 60) {
    if (validBuildAt('house', x, y)) { buildPoint = { x, y }; break; }
  }
  if (!buildPoint) throw new Error('找不到測試建築位置');
  const house = createBuilding('house', 0, buildPoint.x, buildPoint.y, 0);
  setBuild(worker, house);
  for (let i = 0; i < 600 && house.construction < 1; i++) updateGame(STEP);

  const town = game.entities.find(entity => entity.faction === 0 && entity.type === 'town');
  game.player.res.food += 500;
  const popBeforeTraining = game.player.pop;
  if (!queueUnit(town, 'villager', true)) throw new Error('無法加入訓練佇列');
  for (let i = 0; i < 480; i++) updateGame(STEP);
  const popAfterTraining = game.player.pop;

  const spear = createUnit('spear', 0, 760, 1360);
  const cavalry = createUnit('cavalry', 1, 790, 1360);
  setAttack(spear, cavalry);
  for (let i = 0; i < 600 && !cavalry.dead; i++) {
    updateUnit(spear, STEP);
    updateProjectiles(STEP);
  }

  if (!persistGame(true)) throw new Error('瀏覽器保存失敗');
  const raw = localStorage.getItem(SAVE_KEY);
  const snapshot = JSON.parse(raw);
  validateSnapshot(snapshot);
  restoreSnapshot(snapshot, false);
  const restoredPaused = game.paused;
  const restoredCamera={x:game.camera.x,y:game.camera.y};
  const restoredAnchor={x:mouse.x,y:mouse.y,inside:mouse.inside,down:mouse.down,pan:mouse.pan};
  game.paused=false;for(let i=0;i<120;i++)updateCamera(STEP);game.paused=restoredPaused;
  const restoredCameraInput={
    anchorCentered:Math.abs(restoredAnchor.x-viewW*.5)<1e-9&&Math.abs(restoredAnchor.y-viewH*.5)<1e-9,
    initiallyOutside:!restoredAnchor.inside&&!restoredAnchor.down&&!restoredAnchor.pan,
    stationaryBeforeEntry:Math.abs(game.camera.x-restoredCamera.x)<1e-9&&Math.abs(game.camera.y-restoredCamera.y)<1e-9,
  };
  const restoredState = { entities: game.entities.length, time: Math.round(game.time), resources: {...game.player.res}, fogCells: game.fog.length };

  const legacy = JSON.parse(raw);
  legacy.v = 3;
  legacy.gameVersion = '3.0.0';
  legacy.chosenCiv = 'jade';
  legacy.projection = 'isometric-v1';
  legacy.game.camera.projection = 'isometric-v1';
  legacy.game.players[0].civ = 'jade';
  for (const entity of legacy.game.entities) if (entity.faction === 0) entity.civ = 'jade';
  const legacyUnit = legacy.game.entities.find(entity => entity.faction === 0 && entity.kind === 'unit');
  legacyUnit.type = 'repeater';
  const legacyUnitId = legacyUnit.id;
  restoreSnapshot(legacy, false);
  const migratedEntity = entityById(legacyUnitId);
  const migration = {
    civ: game.player.civ,
    chosenCiv,
    projection: game.camera.projection,
    unit: migratedEntity?.type,
    allCivsValid: game.players.every(player => !!CIVS[player.civ]) && game.entities.every(entity => !!CIVS[entity.civ]),
    nextSaveVersion: makeSnapshot().v,
  };

  const returnOfRome = JSON.parse(raw);
  returnOfRome.v = 4;
  returnOfRome.chosenCiv = 'assyrians';
  returnOfRome.game.players[0].civ = 'assyrians';
  returnOfRome.game.camera.projection = 'topdown-v1';
  for (const entity of returnOfRome.game.entities) if (entity.faction === 0) entity.civ = 'assyrians';
  const returnOfRomeUnit = returnOfRome.game.entities.find(entity => entity.faction === 0 && entity.kind === 'unit');
  returnOfRomeUnit.type = 'assyrianChariot';
  const returnOfRomeUnitId = returnOfRomeUnit.id;
  restoreSnapshot(returnOfRome, false);
  const priorRosterMigration = {
    civ:game.player.civ,
    chosenCiv,
    unit:entityById(returnOfRomeUnitId)?.type,
    allTypesValid:game.entities.every(entity=>entity.kind!=='unit'||!!UNIT[entity.type]),
    nextSaveVersion:makeSnapshot().v,
  };

  playerCount = 4;
  newGame();
  const multi = { players: game.players.length, spawns: game.spawn.length, factions: new Set(game.entities.map(e=>e.faction)).size, civs: new Set(game.players.map(p=>p.civ)).size };
  difficulty = '休閒'; playerCount = 2; tutorialRequested = true; newGame();
  tutorialEvent('camera'); updateTutorial(.3);
  const tutorial = { active: game.tutorial.active, lessons: TUTORIAL_STEPS.length, step: game.tutorial.step };

  const p=game.player,at=(type,n)=>createBuilding(type,0,game.spawn[0].x+300+n*7,game.spawn[0].y-250-n*9,1);
  const progression={
    darkInitiallyBlocked:!ageRequirement(p).met,
    farmNeedsMill:missingBuildPrerequisite('farm')==='mill',
    rangeNeedsBarracks:missingBuildPrerequisite('range')==='barracks',
    workshopNeedsBlacksmith:missingBuildPrerequisite('workshop')==='blacksmith',
  };
  at('mill',1);progression.farmUnlocked=missingBuildPrerequisite('farm')===null;progression.darkAfterMillStillBlocked=!ageRequirement(p).met;at('lumber',2);progression.feudalReady=ageRequirement(p).met;
  p.age=2;progression.castleInitiallyBlocked=!ageRequirement(p).met;at('barracks',3);progression.rangeAndStableUnlocked=missingBuildPrerequisite('range')===null&&missingBuildPrerequisite('stable')===null;at('blacksmith',4);progression.castleBuildingsUnlocked=missingBuildPrerequisite('workshop')===null&&missingBuildPrerequisite('castle')===null;progression.castleStillNeedsMilitary=!ageRequirement(p).met;at('range',5);progression.castleReady=ageRequirement(p).met;
  p.age=3;progression.imperialInitiallyBlocked=!ageRequirement(p).met;const castle=at('castle',6);progression.imperialReady=ageRequirement(p).met;
  for(const key of Object.keys(p.res))p.res[key]+=5000;
  const uniqueType=CIVS[p.civ].unique;progression.uniqueTraining={type:uniqueType,queued:queueUnit(castle,uniqueType,true),queueType:castle.queue[0]?.type,age:UNIT[uniqueType].age,trainAt:UNIT[uniqueType].trainAt};
  const animation = ['villager','spear','archer','cavalry','catapult','warElephant'].map((type,index) => {
    const unit = {id:9000+index,type,path:[],anim:0,order:{type:'idle'}};
    const poses = Array.from({length:41},(_,i)=>unitIdlePose(unit,10+i*.1,false));
    const reduced = Array.from({length:41},(_,i)=>unitIdlePose(unit,10+i*.1,true));
    const range = (samples,key) => Math.max(...samples.map(p=>p[key]))-Math.min(...samples.map(p=>p[key]));
    const ranges = {bob:range(poses,'bob'),breath:range(poses,'breath'),headTurn:range(poses,'headTurn'),arm:range(poses,'arm'),tail:range(poses,'tail'),crew:range(poses,'crew'),reducedBob:range(reduced,'bob')};
    return {type,ranges,finite:poses.every(p=>['bob','breath','sway','headTurn','arm','foot','tail','crew','banner','gear','lunge'].every(key=>Number.isFinite(p[key])))};
  });
  const idleAnimated = animation.every(({ranges,finite}) => finite && ranges.bob>1 && ranges.breath>.04 && ranges.headTurn>.18 && ranges.arm>.3 && ranges.reducedBob>.25)
    && animation.find(a=>a.type==='cavalry').ranges.tail>.5
    && animation.find(a=>a.type==='catapult').ranges.crew>.35;

  const anchor={x:game.spawn[0].x+70,y:game.spawn[0].y-70};
  const tower=createBuilding('tower',0,anchor.x,anchor.y,1),back=createUnit('spear',0,anchor.x,anchor.y),front=createUnit('archer',0,anchor.x,anchor.y);
  updateFog(true);
  const click=worldToScreen(anchor.x,anchor.y),unitHits=friendlyUnitHitsAtScreen(click.x,click.y).map(unit=>unit.id),buildingHits=friendlyBuildingHitsAtScreen(click.x,click.y).map(building=>building.id);
  resetSelectionCycle();selectAt(click.x,click.y,false,false,1000);const firstPick=[...game.selected][0];selectAt(click.x,click.y,false,false,1200);const secondPick=[...game.selected][0];
  game.camera.zoom=.62;clampCamera();const lowZoomClick=worldToScreen(anchor.x,anchor.y),lowZoomHits=friendlyUnitHitsAtScreen(lowZoomClick.x+18,lowZoomClick.y).map(unit=>unit.id);resetSelectionCycle();selectAt(lowZoomClick.x+18,lowZoomClick.y,false,false,2200);const lowZoomPick=[...game.selected][0];
  document.documentElement={requestFullscreen(){document.fullscreenElement=this}};document.exitFullscreen=function(){document.fullscreenElement=null};toggleFullscreen();const fullscreenEntered=fullscreenActive();toggleFullscreen();const fullscreenExited=!fullscreenActive();
  const selection={
    unitFirst:unitHits.length===2&&unitHits.includes(back.id)&&unitHits.includes(front.id)&&buildingHits.includes(tower.id),
    cycled:firstPick!==secondPick&&[back.id,front.id].includes(firstPick)&&[back.id,front.id].includes(secondPick),
    lowZoomReach:lowZoomHits.length>0&&lowZoomHits.includes(lowZoomPick)&&entityById(lowZoomPick)?.kind==='unit',
    lowZoomHits,
    lowZoomPick,
    minimumHitRadius:Math.min(unitScreenHitRadius(back),unitScreenHitRadius(front)),
  };
  const visibility={idleAnimated,animation,selection,fullscreenEntered,fullscreenExited};
  const effectsFallback={api:typeof EmpireFX==='object'&&typeof EmpireFX.setEnabled==='function'&&typeof EmpireFX.resize==='function',available:!!EmpireFX?.available};
  const generatedArtFallback={
    api:typeof GeneratedArt==='object'&&['getUnitSprite','getBuildingSprite','getEnvironmentSprite','getEffectSprite','preload','atlasState'].every(key=>typeof GeneratedArt[key]==='function'),
    units:Object.keys(GeneratedArt?.unitMapping||{}).length,
    buildings:Object.keys(GeneratedArt?.buildingMapping||{}).length,
    environment:Object.keys(GeneratedArt?.environmentMapping||{}).length,
    effects:Object.keys(GeneratedArt?.effectMapping||{}).length,
    noImageSafe:GeneratedArt?.preload()===false&&GeneratedArt?.getUnitSprite('villager')===null&&GeneratedArt?.getBuildingSprite('town')===null,
  };
  return {
    initial,
    openingCameraInput,
    restoredCameraInput,
    afterSimulation: restoredState,
    economy: { gatheredWood: Math.round(gatheredWood * 10) / 10, houseComplete: house.construction === 1, popBeforeTraining, popAfterTraining },
    combat: { cavalryDefeated: cavalry.dead },
    saveVersion: snapshot.v,
    migration,
    priorRosterMigration,
    saveBytes: raw.length,
    restoredPaused,
    fogCells: restoredState.fogCells,
    projection,
    roster,
    progressionData,
    progression,
    multi,
    tutorial,
    visibility,
    effectsFallback,
    generatedArtFallback,
  };
})()`, context);

if (result.initial.sites !== 3 || result.initial.popCap !== 15 || result.initial.players !== 2) throw new Error('初始戰局狀態錯誤');
if (!result.openingCameraInput.anchorCentered || !result.openingCameraInput.initiallyOutside || !result.openingCameraInput.stationaryBeforeEntry || !result.openingCameraInput.edgeScrollAfterEntry || !result.openingCameraInput.keyboardPan || !result.openingCameraInput.rightDragPan) throw new Error('開局鏡頭錨點、游標進入後邊緣平移、鍵盤或右鍵拖曳驗證失敗');
if (!result.restoredCameraInput.anchorCentered || !result.restoredCameraInput.initiallyOutside || !result.restoredCameraInput.stationaryBeforeEntry) throw new Error('復原存檔後的鏡頭輸入未重新置中');
if (result.economy.gatheredWood <= 0 || !result.economy.houseComplete || result.economy.popAfterTraining <= result.economy.popBeforeTraining) throw new Error('經濟或生產流程錯誤');
if (!result.combat.cavalryDefeated) throw new Error('兵種戰鬥流程錯誤');
if (result.saveVersion !== 4 || !result.restoredPaused) throw new Error('第四版存檔往返驗證失敗');
if (result.migration.civ !== 'chinese' || result.migration.chosenCiv !== 'chinese' || result.migration.projection !== 'topdown-v1' || result.migration.unit !== 'chuKoNu' || !result.migration.allCivsValid || result.migration.nextSaveVersion !== 4) throw new Error('第三版文明或投影存檔遷移失敗');
if (result.priorRosterMigration.civ !== 'mongols' || result.priorRosterMigration.chosenCiv !== 'mongols' || result.priorRosterMigration.unit !== 'mangudai' || !result.priorRosterMigration.allTypesValid || result.priorRosterMigration.nextSaveVersion !== 4) throw new Error('上一版十六文明存檔遷移失敗');
if (result.fogCells !== 58 * 42) throw new Error('迷霧資料尺寸錯誤');
if (result.projection.id !== 'topdown-v1' || result.projection.roundTripError > 1e-6 || result.projection.xCrossTalk > 1e-9 || result.projection.yCrossTalk > 1e-9 || result.projection.xDirection <= 0 || result.projection.yDirection <= 0) throw new Error('純 2D 俯視投影驗證失敗');
const expectedCivs=['britons','byzantines','celts','chinese','franks','goths','japanese','mongols','persians','saracens','teutons','turks','vikings'];
if (JSON.stringify(result.roster.keys)!==JSON.stringify(expectedCivs) || result.roster.uniqueUnits!==13 || !result.roster.dataDriven || !result.roster.canonicalUnique || !result.roster.castleAgeUnique) throw new Error('原版十三文明或其城堡時代特色單位資料不完整');
if (JSON.stringify(result.progressionData.ages)!==JSON.stringify(['黑暗時代','封建時代','城堡時代','帝王時代'])) throw new Error('時代名稱或順序錯誤');
const expectedTiers={town:1,house:1,mill:1,lumber:1,farm:1,barracks:1,range:2,stable:2,blacksmith:2,tower:2,wall:2,workshop:3,castle:3,wonder:4};
if (JSON.stringify(result.progressionData.tiers)!==JSON.stringify(expectedTiers) || !Object.keys(expectedTiers).slice(1).every(type=>result.progressionData.buildOrder.includes(type))) throw new Error('《帝王世紀 II》式建築時代分層不完整');
if (!result.progression.darkInitiallyBlocked || !result.progression.darkAfterMillStillBlocked || !result.progression.feudalReady || !result.progression.castleInitiallyBlocked || !result.progression.castleStillNeedsMilitary || !result.progression.castleReady || !result.progression.imperialInitiallyBlocked || !result.progression.imperialReady || !result.progression.farmNeedsMill || !result.progression.farmUnlocked || !result.progression.rangeNeedsBarracks || !result.progression.rangeAndStableUnlocked || !result.progression.workshopNeedsBlacksmith || !result.progression.castleBuildingsUnlocked || !result.progression.uniqueTraining.queued || result.progression.uniqueTraining.queueType!==result.progression.uniqueTraining.type || result.progression.uniqueTraining.age!==3 || result.progression.uniqueTraining.trainAt!=='castle') throw new Error('時代前置、建築前置或城堡特色單位生產流程錯誤');
if (result.multi.players !== 4 || result.multi.spawns !== 4 || result.multi.factions !== 4 || result.multi.civs !== 4) throw new Error('四方混戰初始化失敗');
if (!result.tutorial.active || result.tutorial.lessons < 12 || result.tutorial.step !== 1) throw new Error('新手教學未正確初始化');
if (!result.visibility.idleAnimated || !result.visibility.selection.unitFirst || !result.visibility.selection.cycled || !result.visibility.selection.lowZoomReach || result.visibility.selection.minimumHitRadius < 20 || !result.visibility.fullscreenEntered || !result.visibility.fullscreenExited) {
  console.error(JSON.stringify(result.visibility, null, 2));
  throw new Error('生動待機動畫、單位優先重疊選取或全螢幕處理失敗');
}
if (!result.effectsFallback.api || result.effectsFallback.available) throw new Error('WebGL2 不可用時未安全保留 Canvas 後備路徑');
if (!result.generatedArtFallback.api || result.generatedArtFallback.units !== 22 || result.generatedArtFallback.buildings !== 14 || result.generatedArtFallback.environment !== 8 || result.generatedArtFallback.effects !== 16 || !result.generatedArtFallback.noImageSafe) throw new Error('Imagegen 圖集 API、對應數量或無 Image 環境後備路徑錯誤');

console.log(JSON.stringify({ result: '通過', ...result }, null, 2));
