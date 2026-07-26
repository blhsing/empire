import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const html = read('index.html');
const scripts = [...html.matchAll(/<script src="([^"]+)"/g)].map(match => match[1]);
const styles = [...html.matchAll(/<link rel="stylesheet" href="([^"]+)"/g)].map(match => match[1]);
const localRefs = [...html.matchAll(/(?:src|href)="([^"#]+)"/g)].map(match => match[1]);

for (const relative of localRefs) {
  if (/^(?:data:|https?:)/.test(relative)) continue;
  if (!fs.existsSync(path.join(root, relative))) throw new Error(`缺少引用檔案：${relative}`);
}

const joinedJs = scripts.map(read).join('\n');
new Function(joinedJs);

const joinedCss = styles.map(read).join('\n');
const smallFonts = [...joinedCss.matchAll(/font-size\s*:\s*([0-9.]+)px/g)]
  .map(match => Number(match[1]))
  .filter(size => size < 12);
if (smallFonts.length) throw new Error(`找到小於 12px 的字級：${smallFonts.join(', ')}`);

for (const stylesheet of styles) {
  const css = read(stylesheet);
  for (const match of css.matchAll(/url\(['"]?([^)'"\s]+)/g)) {
    const url = match[1];
    if (/^(?:data:|https?:)/.test(url)) continue;
    const resolved = path.resolve(root, path.dirname(stylesheet), url);
    if (!fs.existsSync(resolved)) throw new Error(`缺少 CSS 資產：${resolved}`);
  }
}

const allText = `${html}\n${joinedJs}\n${joinedCss}`;
if (/https?:\/\//.test(allText)) throw new Error('發現外部網路引用');
const simplifiedTerms = ['开始', '暂停', '设置', '资源', '升级', '游戏', '载入', '导出', '导入', '建筑', '选择'];
const foundSimplified = simplifiedTerms.filter(term => allText.includes(term));
if (foundSimplified.length) throw new Error(`發現簡體中文：${foundSimplified.join('、')}`);

const expectedCivilizations = ['britons', 'byzantines', 'celts', 'chinese', 'franks', 'goths', 'japanese', 'mongols', 'persians', 'saracens', 'teutons', 'turks', 'vikings'];
const coreSource = read('js/core-data-audio.js');
const civStart = coreSource.indexOf('const CIVS={');
const civEnd = coreSource.indexOf('const UNIT={', civStart);
if (civStart < 0 || civEnd < 0) throw new Error('找不到文明資料表');
const civBlock = coreSource.slice(civStart, civEnd);
const declaredCivilizations = [...civBlock.matchAll(/^\s+([a-zA-Z][a-zA-Z0-9]*):\{name:'([^']+)'/gm)]
  .map(([, key, name]) => [key, name]);
if (declaredCivilizations.length !== 13) throw new Error(`文明數量應為 13，實際為 ${declaredCivilizations.length}`);
const declaredKeys = declaredCivilizations.map(([key]) => key);
if (JSON.stringify(declaredKeys) !== JSON.stringify(expectedCivilizations)) throw new Error(`文明名單不是《帝王世紀 II：帝王時代》原版 13 文明：${declaredKeys.join('、')}`);
if (!coreSource.includes("const AGES=['黑暗時代','封建時代','城堡時代','帝王時代']")) throw new Error('四個時代名稱或順序不符合《帝王世紀 II》');

if (!joinedJs.includes("mouse.pan=e.button===2") || joinedJs.includes('mouse.pan=e.button===1')) throw new Error('地圖平移未限定為滑鼠右鍵拖曳');
const coreInputSource = read('js/core-data-audio.js');
const worldSource = read('js/world.js');
const inputSource = read('js/ui-input.js');
if (!/let mouse=\{x:viewW\*\.5,y:viewH\*\.5,[^}]*inside:false\}/.test(coreInputSource)
  || !/function resetCameraInputAnchor\(\)\{[\s\S]*?const x=viewW\*\.5,y=viewH\*\.5;[\s\S]*?inside:false/.test(coreInputSource)
  || !/centerCamera\(game\.spawn\[0\]\.x,game\.spawn\[0\]\.y\);resetCameraInputAnchor\(\)/.test(worldSource)
  || !/resize\(\);clampCamera\(\);resetCameraInputAnchor\(\)/.test(inputSource)) throw new Error('開局或復原存檔後未將鏡頭輸入錨點重置於視窗中央');
if (!inputSource.includes('mouse.inside&&mouse.y<viewH-175')
  || !inputSource.includes("dom.canvas.addEventListener('pointerenter'")
  || !inputSource.includes("dom.canvas.addEventListener('pointerleave'")
  || !inputSource.includes("keys.has('w')")
  || !inputSource.includes("keys.has('ArrowRight')")) throw new Error('邊緣平移未要求真實游標進入，或鍵盤鏡頭控制遺失');
if (!html.includes('id="tutorialBtn"') || !joinedJs.includes('TUTORIAL_STEPS')) throw new Error('缺少新手教學模式');
if (!html.includes('data-players="4"') || !html.includes('data-diff="天命"')) throw new Error('缺少玩家數或 AI 難度選項');
if (!html.includes('id="materialAtlas"')) throw new Error('缺少本機地表材質圖集');
if (!fs.existsSync(path.join(root, 'assets/medieval-terrain-atlas-v2.png')) || !joinedJs.includes("MEDIEVAL_ATLAS_SRC='assets/medieval-terrain-atlas-v2.png'")) throw new Error('缺少新版中世紀地表圖集或其離線引用');
const generatedArtSource = read('js/generated-art.js');
const generatedAssets = [
  'assets/generated/units-common.png',
  'assets/generated/units-unique-a.png',
  'assets/generated/units-unique-b.png',
  'assets/generated/buildings-common.png',
  'assets/generated/buildings-advanced.png',
  'assets/generated/environment.png',
  'assets/generated/effects-ui.png',
];
for (const asset of generatedAssets) {
  const absolute = path.join(root, asset);
  if (!fs.existsSync(absolute) || fs.statSync(absolute).size < 10_000) throw new Error(`缺少或損壞 Imagegen 圖集：${asset}`);
  if (!generatedArtSource.includes(asset)) throw new Error(`Imagegen 載入器未引用圖集：${asset}`);
}
if (!scripts.includes('js/generated-art.js') || scripts.indexOf('js/generated-art.js') > scripts.indexOf('js/render.js')) throw new Error('Imagegen 圖集載入器未在渲染器之前載入');
const expectedUnits = ['villager','scout','swordsman','spear','archer','cavalry','crossbow','ram','catapult','longbowman','cataphract','woadRaider','chuKoNu','throwingAxeman','huskarl','samurai','mangudai','warElephant','mameluke','teutonicKnight','janissary','berserk'];
const expectedBuildings = ['town','house','mill','lumber','farm','barracks','blacksmith','range','stable','tower','wall','castle','workshop','wonder'];
for (const type of expectedUnits) if (!new RegExp(`\\b${type}:\\{atlas:`).test(generatedArtSource)) throw new Error(`缺少單位 Imagegen 對應：${type}`);
for (const type of expectedBuildings) if (!new RegExp(`\\b${type}:\\{atlas:`).test(generatedArtSource)) throw new Error(`缺少建築 Imagegen 對應：${type}`);
if (!generatedArtSource.includes('getEnvironmentSprite') || !generatedArtSource.includes('getEffectSprite') || !generatedArtSource.includes("typeof root.Image!=='function'")) throw new Error('Imagegen 場景／特效 API 或離線後備不完整');
const renderSource = read('js/render.js');
if (!renderSource.includes('GeneratedArt.getUnitSprite') || !renderSource.includes("generatedArtSprite('getBuildingSprite'") || !renderSource.includes("generatedArtSprite('getEnvironmentSprite'") || !renderSource.includes("generatedArtSprite('getEffectSprite'") || !renderSource.includes("globalCompositeOperation='screen'")) throw new Error('戰場未完整採用 Imagegen 單位、建築、場景與特效圖集');
const activityRequirements = ['function unitActivityState','function buildingProgressStates','function siteProgressState','function indexFrameActivity','function drawTargetWorkFeedback','function drawVillagerWorkTool','function drawUnitGrounding','function drawProgressOverlays'];
for (const requirement of activityRequirements) if (!joinedJs.includes(requirement)) throw new Error(`缺少進行中狀態或可讀性動畫：${requirement}`);
if (!renderSource.includes('drop-shadow(0 2px 1px') || !renderSource.includes("ctx.strokeStyle='rgba(0,0,0,.92)'" ) || !renderSource.includes('ctx.strokeStyle=team') || !/drawFog\(\);drawCombatFeedback\([^)]+\);drawProgressOverlays/.test(renderSource)) throw new Error('單位缺少背景分離輪廓，或進度提示未置於迷霧後的受控覆疊層');
if (!/function drawProgressOverlays[\s\S]{0,1800}visibleAt\(e\.x,e\.y\)/.test(renderSource)) throw new Error('進度覆疊未以可見性檢查防止迷霧資訊洩漏');
if (!renderSource.includes("resource==='wood'") || !renderSource.includes("resource==='gold'||resource==='stone'") || !renderSource.includes("resource==='food'") || !renderSource.includes("state.kind==='build'")) throw new Error('村民伐木、採礦、採食或施工工具動畫不完整');
const webglSource = read('js/webgl-effects.js');
const hudSource = read('css/hud.css');
if (!html.includes('id="worldFx"') || !scripts.includes('js/webgl-effects.js') || !webglSource.includes("getContext('webgl2'") || !webglSource.includes('globalThis.EmpireFX') || !webglSource.includes('uVisibility') || !webglSource.includes('pointVisible(activeGame') || !/#worldFx\{[^}]*pointer-events:none/.test(hudSource)) throw new Error('缺少不攔截操作且遵守戰爭迷霧的可選 WebGL2 特效層');
if (!joinedJs.includes('function drawGroundLife') || !joinedJs.includes('function drawAmbientMotes') || !joinedJs.includes('function drawCombatFeedback') || !joinedJs.includes('function drawBuildingActivity')) throw new Error('缺少生動地表、環境、建築或戰鬥繪圖效果');
if (!joinedJs.includes("const PROJECTION_ID='topdown-v1'") || !joinedJs.includes("CURRENT_PROJECTION='topdown-v1'")) throw new Error('投影識別不是純 2D 俯視模式');
if (/\bISO_[A-Z_]+\b|\biso(?:Project|Unproject)\b/.test(joinedJs)) throw new Error('仍殘留等角投影座標運算');
if (!joinedJs.includes('function unitIdlePose') || !joinedJs.includes('function friendlyUnitHitsAtScreen') || !joinedJs.includes('function unitScreenHitRadius') || !joinedJs.includes('selectionCycle')) throw new Error('缺少生動待機動畫或重疊選取機制');
if (!/for\(const entry of scenery\)[\s\S]{0,500}drawBuilding\(entry\.e\)[\s\S]{0,500}for\(const entry of units\)drawUnit\(entry\.e,alpha\)/.test(read('js/render.js'))) throw new Error('單位未在建築之後繪製，仍可能遭建築遮住');
if (!joinedJs.includes("const SAVE_KEY='帝國餘燼_戰局_v4'") || !joinedJs.includes('return{v:4') || !joinedJs.includes('LEGACY_CIV_MAP') || !joinedJs.includes("assyrians:'mongols'")) throw new Error('缺少第四版存檔或舊文明名單遷移');
if (!joinedJs.includes("function ageRequirement") || !joinedJs.includes("farm:['mill']") || !joinedJs.includes("range:['barracks']") || !joinedJs.includes("workshop:['blacksmith']") || !joinedJs.includes("castle:['blacksmith']")) throw new Error('缺少《帝王世紀 II》式時代或建築前置關係');
if (!html.includes('id="fullscreenBtn"') || !joinedJs.includes('requestFullscreen') || !joinedJs.includes('exitFullscreen')) throw new Error('缺少全螢幕模式');
if (!/\^Digit\(\[1-4\]\)\$/.test(inputSource) || !inputSource.includes('if(e.shiftKey){controlGroups[n-1]') || inputSource.includes('if(e.ctrlKey){controlGroups[n-1]')) throw new Error('編隊快捷鍵未避開 Chrome 的 Ctrl＋數字分頁切換');

console.log(JSON.stringify({
  result: '通過',
  scripts,
  styles,
  localReferences: localRefs.length,
  htmlBytes: Buffer.byteLength(html),
  javascriptBytes: Buffer.byteLength(joinedJs),
  cssBytes: Buffer.byteLength(joinedCss),
  minimumFontSize: 12,
  externalNetworkReferences: 0,
  civilizations: 13,
  historicalRoster: declaredCivilizations.map(([, name]) => name),
  projection: 'topdown-v1',
  rightDragPan: true,
  tutorialMode: true,
  animatedIdleUnits: true,
  animatedProgressStates: true,
  highContrastUnits: true,
  overlapSelection: true,
  optionalWebGL2Effects: true,
  imagegenAtlases: generatedAssets.length + 1,
  fullscreenMode: true,
  chromeSafeControlGroups: true,
}, null, 2));
