'use strict';
const $=s=>document.querySelector(s), $$=s=>[...document.querySelectorAll(s)];
    const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
    const lerp=(a,b,t)=>a+(b-a)*t;
    const dist=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y);
    const fmt=n=>Math.max(0,Math.floor(n)).toLocaleString('zh-Hant');
    const costText=c=>Object.entries(c||{}).map(([k,v])=>`${RES[k].short}${v}`).join(' ');
    const escapeHtml=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    let seed=0x73a9d12f;
    function rnd(){seed|=0;seed=seed+0x6D2B79F5|0;let t=seed;t=Math.imul(t^t>>>15,t|1);t^=t+Math.imul(t^t>>>7,t|61);return((t^t>>>14)>>>0)/4294967296}
    function hash2(x,y){let n=Math.imul(x+37,374761393)+Math.imul(y+91,668265263);n=(n^(n>>>13))*1274126177;return((n^(n>>>16))>>>0)/4294967295}

    const TILE=48, MAP_W=58, MAP_H=42, WORLD_W=MAP_W*TILE, WORLD_H=MAP_H*TILE, STEP=1/30, MAX_POP=80;
    const PROJECTION_ID='topdown-v1';
    const FACTION_COLORS=['#5bc5d8','#ec645b','#a77be8','#e5b955'];
    const AGES=['黑暗時代','封建時代','城堡時代','帝王時代'];
    const RES={food:{name:'食物',short:'糧',glyph:'穀'},wood:{name:'木材',short:'木',glyph:'木'},gold:{name:'黃金',short:'金',glyph:'金'},stone:{name:'石材',short:'石',glyph:'石'}};
    /*
      文明數值全部採資料驅動。mods 內的倍率以 1 為基準；unitArmor 為加減值，
      cost 與 ageCost 則以小於 1 表示折扣。powerMods 使用相同規則，僅在軍令期間套用。
    */
    const CIVS={
      britons:{name:'不列顛人',seal:'弓',style:'長弓列陣 · 牧野王國',color:'#4f89c6',accent:'#e6c96e',
        pros:['遠程射程＋10%，食物採集＋8%','長弓兵能在敵軍接近前傾瀉箭雨'],cons:['騎兵生命−10%'],
        mods:{unitRange:{ranged:1.1},gather:{food:1.08},unitHp:{cavalry:.9}},
        power:'長弓齊射',powerDesc:'12 秒內遠程射程＋18%、攻速＋22%。',powerMods:{duration:12,unitRange:{ranged:1.18},unitCooldown:{ranged:.78}},unique:'longbowman'},
      byzantines:{name:'拜占庭人',seal:'雙',style:'雙頭鷹旗 · 千年城垣',color:'#8b70bd',accent:'#e0b75c',
        pros:['建築生命＋15%，時代晉升成本−10%','拜占庭聖騎兵擅長踐破步兵陣線'],cons:['步兵攻擊−8%'],
        mods:{buildingHp:1.15,ageCost:.9,unitDamage:{infantry:.92}},
        power:'君士坦丁壁壘',powerDesc:'14 秒內建築減傷 30%，騎兵護甲＋2。',powerMods:{duration:14,buildingReduction:.3,unitArmor:{cavalry:2}},unique:'cataphract'},
      celts:{name:'塞爾特人',seal:'結',style:'高地戰吼 · 森林攻城',color:'#4f9b70',accent:'#d7a64b',
        pros:['木材採集＋12%，步兵移速＋10%','攻城器攻速＋12%，菘藍突襲者行動迅捷'],cons:['騎兵護甲−1'],
        mods:{gather:{wood:1.12},unitSpeed:{infantry:1.1},unitCooldown:{siege:.88},unitArmor:{cavalry:-1}},
        power:'高地戰吼',powerDesc:'12 秒內步兵移速＋25%、攻擊＋16%。',powerMods:{duration:12,unitSpeed:{infantry:1.25},unitDamage:{infantry:1.16}},unique:'woadRaider'},
      chinese:{name:'中國人',seal:'龍',style:'工巧農政 · 諸葛連弩',color:'#c94f4f',accent:'#e4c15e',
        pros:['村民成本−10%，農田存量＋12%','諸葛弩以密集連射壓制步兵'],cons:['起始食物−18%、黃金−10%'],
        mods:{unitCost:{worker:.9},farmYield:1.12,startRes:{food:.82,gold:.9}},
        power:'萬弩連發',powerDesc:'10 秒內遠程攻速＋35%、攻擊＋10%。',powerMods:{duration:10,unitCooldown:{ranged:.65},unitDamage:{ranged:1.1}},unique:'chuKoNu'},
      franks:{name:'法蘭克人',seal:'鳶',style:'鳶尾戰旗 · 重騎封臣',color:'#386faf',accent:'#e5cf77',
        pros:['騎兵生命＋12%，城堡成本−25%','擲斧兵可從步兵陣後投射重斧'],cons:['遠程單位射程−10%'],
        mods:{unitHp:{cavalry:1.12},buildingCost:{castle:.75},unitRange:{ranged:.9}},
        power:'封建重騎衝鋒',powerDesc:'10 秒內騎兵移速＋20%、攻擊＋20%，並恢復 10% 生命。',powerMods:{duration:10,unitSpeed:{cavalry:1.2},unitDamage:{cavalry:1.2},heal:{cavalry:.1}},unique:'throwingAxeman'},
      goths:{name:'哥德人',seal:'鴉',style:'部族洪流 · 破弓近衛',color:'#6f765f',accent:'#caa66b',
        pros:['步兵成本−18%、訓練速度＋15%','哥德衛隊對遠程單位極具威脅'],cons:['建築生命−10%'],
        mods:{unitCost:{infantry:.82},trainSpeed:{infantry:1.15},buildingHp:.9},
        power:'部族洪流',powerDesc:'12 秒內步兵移速＋20%、攻速＋22%。',powerMods:{duration:12,unitSpeed:{infantry:1.2},unitCooldown:{infantry:.78}},unique:'huskarl'},
      japanese:{name:'日本人',seal:'日',style:'武家刀陣 · 精耕漁獵',color:'#d2635c',accent:'#efcf83',
        pros:['步兵攻速＋14%，食物採集＋8%','日本武士善於迅速斬破重裝步兵'],cons:['騎兵生命−10%'],
        mods:{unitCooldown:{infantry:.86},gather:{food:1.08},unitHp:{cavalry:.9}},
        power:'武士決意',powerDesc:'10 秒內步兵攻擊＋20%、護甲＋2。',powerMods:{duration:10,unitDamage:{infantry:1.2},unitArmor:{infantry:2}},unique:'samurai'},
      mongols:{name:'蒙古人',seal:'狼',style:'蒼狼騎射 · 草原奔襲',color:'#6aa7ba',accent:'#d5a34d',
        pros:['食物採集＋10%，騎兵攻速＋12%','蒙古突騎機動迅捷並克制攻城器'],cons:['建築生命−10%'],
        mods:{gather:{food:1.1},unitCooldown:{cavalry:.88},buildingHp:.9},
        power:'草原風暴',powerDesc:'10 秒內騎兵移速＋22%、攻速＋25%。',powerMods:{duration:10,unitSpeed:{cavalry:1.22},unitCooldown:{cavalry:.75}},unique:'mangudai'},
      persians:{name:'波斯人',seal:'象',style:'萬王之國 · 象軍震地',color:'#b85656',accent:'#e7bd66',
        pros:['起始食物與木材＋8%，騎兵生命＋8%','戰象能摧毀密集軍隊與建築'],cons:['農田存量−10%'],
        mods:{startRes:{food:1.08,wood:1.08},unitHp:{cavalry:1.08},farmYield:.9},
        power:'萬王戰象',powerDesc:'12 秒內騎兵攻擊＋20%，並恢復 18% 生命。',powerMods:{duration:12,unitDamage:{cavalry:1.2},heal:{cavalry:.18}},unique:'warElephant'},
      saracens:{name:'薩拉森人',seal:'月',style:'新月商旅 · 馬穆魯克',color:'#c38d48',accent:'#4fa9a1',
        pros:['黃金採集＋12%，騎兵攻擊＋8%','馬穆魯克能以飛刃獵殺重騎兵'],cons:['農田存量−10%'],
        mods:{gather:{gold:1.12},unitDamage:{cavalry:1.08},farmYield:.9},
        power:'新月獵騎',powerDesc:'10 秒內騎兵射程＋18%、攻速＋20%。',powerMods:{duration:10,unitRange:{cavalry:1.18},unitCooldown:{cavalry:.8}},unique:'mameluke'},
      teutons:{name:'條頓人',seal:'十',style:'黑十字軍 · 鐵壁堡壘',color:'#8b8e98',accent:'#d7bd74',
        pros:['步兵護甲＋1，建築生命＋12%','條頓武士近戰攻防無雙'],cons:['騎兵移速−10%'],
        mods:{unitArmor:{infantry:1},buildingHp:1.12,unitSpeed:{cavalry:.9}},
        power:'條頓鐵壁',powerDesc:'14 秒內步兵護甲＋3，建築減傷 20%。',powerMods:{duration:14,unitArmor:{infantry:3},buildingReduction:.2},unique:'teutonicKnight'},
      turks:{name:'土耳其人',seal:'星',style:'火藥禁軍 · 黃金帝國',color:'#3f9b82',accent:'#ddaa54',
        pros:['黃金採集＋15%，遠程攻擊＋8%','土耳其火槍兵單發威力極高'],cons:['步兵生命−10%'],
        mods:{gather:{gold:1.15},unitDamage:{ranged:1.08},unitHp:{infantry:.9}},
        power:'蘇丹火網',powerDesc:'10 秒內遠程攻擊＋22%、射程＋12%。',powerMods:{duration:10,unitDamage:{ranged:1.22},unitRange:{ranged:1.12}},unique:'janissary'},
      vikings:{name:'維京人',seal:'艦',style:'北海長船 · 狂戰斧陣',color:'#6b82a7',accent:'#d49255',
        pros:['步兵生命＋12%，木材採集＋8%','狂戰士耐久且能撕裂前線'],cons:['騎兵成本＋10%'],
        mods:{unitHp:{infantry:1.12},gather:{wood:1.08},unitCost:{cavalry:1.1}},
        power:'奧丁狂怒',powerDesc:'12 秒內步兵攻速＋22%，並恢復 16% 生命。',powerMods:{duration:12,unitCooldown:{infantry:.78},heal:{infantry:.16}},unique:'berserk'}
    };
    const UNIT={
      villager:{name:'村民',glyph:'民',hp:55,damage:3,armor:0,speed:62,range:22,cool:1.3,cost:{food:50},time:15,pop:1,age:1,role:'worker',desc:'採集資源、興建與修理建築。'},
      scout:{name:'斥候騎兵',glyph:'斥',hp:100,damage:6,armor:1,speed:98,range:24,cool:1.25,cost:{food:80},time:18,pop:1,age:2,role:'cavalry',sight:300,desc:'高速偵察，視野廣闊。'},
      swordsman:{name:'民兵',glyph:'劍',hp:82,damage:8,armor:0,speed:59,range:27,cool:1.3,cost:{food:60,gold:20},time:16,pop:1,age:1,role:'infantry',desc:'黑暗時代即可訓練的基礎步兵。'},
      spear:{name:'長槍兵',glyph:'槍',hp:115,damage:10,armor:1,speed:58,range:28,cool:1.3,cost:{food:60,wood:20},time:16,pop:1,age:2,role:'infantry',bonus:{cavalry:20},desc:'廉價前排，強力克制騎兵。'},
      archer:{name:'弓箭手',glyph:'弓',hp:75,damage:11,armor:0,speed:61,range:225,cool:1.35,cost:{food:40,wood:45},time:18,pop:1,age:2,role:'ranged',bonus:{infantry:5},desc:'遠程壓制長槍兵，畏懼騎兵。'},
      cavalry:{name:'騎士',glyph:'騎',hp:195,damage:21,armor:3,speed:91,range:30,cool:1.45,cost:{food:90,gold:60},time:26,pop:2,age:3,role:'cavalry',bonus:{ranged:10,siege:12},desc:'城堡時代的重騎兵，擅長衝擊後排。'},
      crossbow:{name:'弩兵',glyph:'弩',hp:85,damage:17,armor:1,speed:56,range:220,cool:1.55,cost:{food:45,gold:55},time:21,pop:1,age:3,role:'ranged',bonus:{infantry:10,cavalry:8},desc:'穿甲遠程，克制重裝軍隊。'},
      ram:{name:'衝撞車',glyph:'車',hp:420,damage:35,armor:8,speed:35,range:34,cool:2,cost:{wood:170,gold:80},time:36,pop:3,age:3,role:'siege',bonus:{building:70},desc:'耐射擊，專門摧毀建築。'},
      catapult:{name:'投石車',glyph:'砲',hp:160,damage:32,armor:1,speed:34,range:270,cool:3,cost:{wood:120,gold:90},time:34,pop:3,age:3,role:'siege',bonus:{ranged:18,building:18},splash:58,desc:'拋射巨石，打擊密集軍隊。'},
      longbowman:{name:'長弓兵',glyph:'弓',hp:82,damage:17,armor:0,speed:55,range:278,cool:1.5,cost:{wood:48,gold:52},time:24,pop:1,age:3,role:'ranged',bonus:{infantry:8},unique:'britons',trainAt:'castle',desc:'射程冠絕戰場，但近身後十分脆弱。'},
      cataphract:{name:'拜占庭聖騎兵',glyph:'雙',hp:225,damage:23,armor:5,speed:82,range:31,cool:1.45,cost:{food:92,gold:72},time:29,pop:2,age:3,role:'cavalry',bonus:{infantry:18},unique:'byzantines',trainAt:'castle',splash:18,desc:'披掛重甲的精騎，專門踐破步兵陣線。'},
      woadRaider:{name:'菘藍突襲者',glyph:'藍',hp:145,damage:20,armor:2,speed:82,range:28,cool:1.2,cost:{food:72,gold:38},time:21,pop:1,age:3,role:'infantry',bonus:{siege:8},unique:'celts',trainAt:'castle',desc:'速度驚人的突擊步兵，適合繞後襲擊。'},
      chuKoNu:{name:'諸葛弩',glyph:'諸',hp:78,damage:9,armor:1,speed:55,range:220,cool:.72,cost:{wood:52,gold:48},time:24,pop:1,age:3,role:'ranged',bonus:{infantry:5},unique:'chinese',trainAt:'castle',splash:14,desc:'以極高射速連續發射弩箭。'},
      throwingAxeman:{name:'擲斧兵',glyph:'斧',hp:122,damage:18,armor:3,speed:55,range:142,cool:1.35,cost:{food:64,gold:48},time:23,pop:1,age:3,role:'infantry',ranged:true,bonus:{infantry:8},unique:'franks',trainAt:'castle',desc:'以重斧進行短程投射的耐久步兵。'},
      huskarl:{name:'哥德衛隊',glyph:'盔',hp:160,damage:18,armor:6,speed:68,range:28,cool:1.25,cost:{food:70,gold:42},time:20,pop:1,age:3,role:'infantry',bonus:{ranged:18},unique:'goths',trainAt:'castle',desc:'抗箭甲冑與高速步伐令弓兵聞風喪膽。'},
      samurai:{name:'日本武士',glyph:'武',hp:158,damage:21,armor:4,speed:59,range:29,cool:1.1,cost:{food:72,gold:48},time:22,pop:1,age:3,role:'infantry',bonus:{infantry:7,cavalry:5},unique:'japanese',trainAt:'castle',desc:'出手迅捷的精銳刀兵，善斬敵方菁英。'},
      mangudai:{name:'蒙古突騎',glyph:'鷹',hp:135,damage:15,armor:2,speed:96,range:228,cool:1.2,cost:{food:70,gold:62},time:26,pop:2,age:3,role:'cavalry',ranged:true,bonus:{siege:18},unique:'mongols',trainAt:'castle',desc:'高速騎射手，能迅速摧毀攻城器。'},
      warElephant:{name:'戰象',glyph:'象',hp:430,damage:31,armor:5,speed:48,range:36,cool:1.9,cost:{food:130,gold:95},time:38,pop:3,age:3,role:'cavalry',bonus:{building:25,infantry:10},unique:'persians',trainAt:'castle',splash:34,desc:'昂貴、緩慢而驚人的重型衝擊單位。'},
      mameluke:{name:'馬穆魯克',glyph:'月',hp:170,damage:17,armor:3,speed:86,range:118,cool:1.25,cost:{food:76,gold:64},time:27,pop:2,age:3,role:'cavalry',ranged:true,bonus:{cavalry:20},unique:'saracens',trainAt:'castle',desc:'投擲彎刀的駱駝精騎，強力克制騎兵。'},
      teutonicKnight:{name:'條頓武士',glyph:'十',hp:230,damage:27,armor:8,speed:40,range:28,cool:1.55,cost:{food:82,gold:58},time:29,pop:1,age:3,role:'infantry',bonus:{building:10},unique:'teutons',trainAt:'castle',desc:'極慢但近戰攻防無雙的重甲武士。'},
      janissary:{name:'土耳其火槍兵',glyph:'銃',hp:92,damage:24,armor:1,speed:56,range:235,cool:1.85,cost:{food:58,gold:68},time:27,pop:1,age:3,role:'ranged',bonus:{infantry:6},unique:'turks',trainAt:'castle',desc:'射速偏慢，但單發火力與射程優秀。'},
      berserk:{name:'狂戰士',glyph:'狂',hp:180,damage:21,armor:4,speed:62,range:29,cool:1.2,cost:{food:76,gold:44},time:24,pop:1,age:3,role:'infantry',bonus:{infantry:5},unique:'vikings',trainAt:'castle',regen:.75,desc:'能緩慢恢復生命的北海精銳戰士。'}
    };
    const BUILD={
      town:{name:'城鎮中心',glyph:'城',hp:2800,size:82,cost:{},age:1,pop:15,train:['villager'],desc:'王國心臟；訓練村民，遭摧毀即告戰敗。'},
      house:{name:'房舍',glyph:'舍',hp:420,size:36,cost:{wood:100},age:1,pop:10,time:18,desc:'提高 10 人口容量。'},
      mill:{name:'磨坊',glyph:'磨',hp:520,size:39,cost:{wood:100},age:1,time:20,desc:'解鎖農田，附近食物採集效率＋10%。'},
      lumber:{name:'伐木場',glyph:'木',hp:500,size:38,cost:{wood:100},age:1,time:20,desc:'附近木材採集效率＋10%，也是晉升封建時代的前置。'},
      farm:{name:'農田',glyph:'田',hp:260,size:42,cost:{wood:60},age:1,time:12,food:450,desc:'需先完成磨坊；提供可持續食物。'},
      barracks:{name:'軍營',glyph:'營',hp:780,size:51,cost:{wood:145},age:1,time:28,train:['swordsman','spear'],desc:'訓練步兵，並解鎖靶場與馬廄。'},
      blacksmith:{name:'鐵匠鋪',glyph:'鐵',hp:650,size:43,cost:{wood:150},age:2,time:27,desc:'研究經濟與軍事科技，並解鎖城堡與攻城工坊。'},
      range:{name:'靶場',glyph:'靶',hp:650,size:49,cost:{wood:165},age:2,time:30,train:['archer','crossbow'],desc:'需先完成軍營；訓練弓箭手與弩兵。'},
      stable:{name:'馬廄',glyph:'廄',hp:700,size:52,cost:{wood:190},age:2,time:32,train:['scout','cavalry'],desc:'需先完成軍營；訓練斥候騎兵與騎士。'},
      tower:{name:'箭塔',glyph:'塔',hp:720,size:34,cost:{wood:80,stone:150},age:2,time:32,attack:12,range:245,cool:1.5,desc:'自動射擊鄰近敵軍；最多四座。'},
      wall:{name:'石牆',glyph:'牆',hp:540,size:29,cost:{stone:35},age:2,time:10,desc:'便宜的封建時代防禦工事。'},
      castle:{name:'城堡',glyph:'堡',hp:3200,size:76,cost:{stone:500},age:3,time:65,attack:18,range:285,cool:1.35,desc:'訓練文明獨特兵種，也是晉升帝王時代的前置。'},
      workshop:{name:'攻城工坊',glyph:'坊',hp:760,size:56,cost:{wood:220,gold:80},age:3,time:38,train:['ram','catapult'],desc:'需先完成鐵匠鋪；製造攻城器械。'},
      wonder:{name:'世界奇觀',glyph:'觀',hp:1900,size:74,cost:{wood:800,gold:800,stone:800},age:4,time:80,desc:'完工後守住 180 秒即可獲勝。'}
    };
    const BUILD_ORDER=['house','mill','lumber','farm','barracks','blacksmith','range','stable','tower','wall','castle','workshop','wonder'];
    const TRAIN_AT={town:['villager'],barracks:['swordsman','spear'],range:['archer','crossbow'],stable:['scout','cavalry'],castle:[],workshop:['ram','catapult']};
    const DIFF={
      休閒:{aiRate:.72,wave:120,start:1,income:.12,think:2.3,counter:.25,label:'休閒',desc:'較慢決策、較少援軍，適合熟悉建造與剋制。'},
      征戰:{aiRate:1,wave:84,start:3,income:.22,think:1.6,counter:.48,label:'征戰',desc:'均衡的決策速度與攻勢，適合熟悉即時戰略的玩家。'},
      霸主:{aiRate:1.23,wave:64,start:4,income:.32,think:1.15,counter:.72,label:'霸主',desc:'更快擴張，會積極生產剋制兵種並夾擊弱點。'},
      天命:{aiRate:1.48,wave:49,start:5,income:.44,think:.78,counter:.9,label:'天命',desc:'高速經濟、精準反制與連續攻勢，只適合帝國老將。'}
    };

    const dom={menu:$('#menu'),game:$('#game'),canvas:$('#world'),hud:$('#hud'),resources:$('#resourceRow'),age:$('#ageLabel'),clock:$('#clock'),civ:$('#civHud'),selection:$('#selectionPanel'),commands:$('#commandGrid'),queue:$('#queueInfo'),minimap:$('#minimap'),notices:$('#notifications'),tooltip:$('#tooltip'),toast:$('#toast'),aria:$('#ariaLive'),buildHint:$('#buildHint'),atlas:$('#materialAtlas')};
    const ctx=dom.canvas.getContext('2d',{alpha:false}), mctx=dom.minimap.getContext('2d');
    let dpr=1, viewW=innerWidth, viewH=innerHeight, last=performance.now(), accumulator=0, raf=0;
    let chosenCiv='britons', difficulty='征戰', playerCount=2, tutorialRequested=false, game=null, nextId=1, reducedMotion=matchMedia('(prefers-reduced-motion:reduce)').matches;
    let mouse={x:viewW*.5,y:viewH*.5,worldX:0,worldY:0,down:false,startX:0,startY:0,button:0,drag:false,pan:false,inside:false}, keys=new Set(), controlGroups=[[],[],[],[]];
    let terrainCanvas=null, terrainCtx=null, terrain=[], nav=[], buildMode=null, attackMove=false, touchSelectMode=false;

    function resetCameraInputAnchor(){
      keys.clear();const x=viewW*.5,y=viewH*.5;Object.assign(mouse,{x,y,startX:x,startY:y,down:false,button:0,drag:false,pan:false,inside:false});
      if(game){const p=screenToWorld(x,y);mouse.worldX=p.x;mouse.worldY=p.y}dom.canvas.style.cursor='default';
    }

    function playerFor(faction){return game?.players?.[faction]||(faction===0?game?.player:game?.enemy)}
    function powerActive(faction){return (playerFor(faction)?.powerUntil||0)>game.time}

    const Audio={
      ctx:null,master:null,music:null,ambience:null,sfxBus:null,muted:false,volume:.72,timer:null,step:0,noiseBuffer:null,ambientSources:[],lastSfx:Object.create(null),pageHook:false,
      init(){
        if(this.ctx){if(this.ctx.state==='suspended')this.ctx.resume().catch(()=>{});this.setVolume(this.volume);this.startMusic();return}
        const A=window.AudioContext||window.webkitAudioContext;if(!A)return;
        try{this.ctx=new A()}catch{return}
        this.master=this.ctx.createGain();this.music=this.ctx.createGain();this.ambience=this.ctx.createGain();this.sfxBus=this.ctx.createGain();
        const comp=this.ctx.createDynamicsCompressor();comp.threshold.value=-18;comp.knee.value=18;comp.ratio.value=5;comp.attack.value=.006;comp.release.value=.24;
        this.music.gain.value=.2;this.ambience.gain.value=.12;this.sfxBus.gain.value=.78;this.music.connect(comp);this.ambience.connect(comp);this.sfxBus.connect(comp);comp.connect(this.master);this.master.connect(this.ctx.destination);this.master.gain.value=this.muted?0:this.volume;
        const b=this.ctx.createBuffer(2,this.ctx.sampleRate*4,this.ctx.sampleRate);for(let c=0;c<b.numberOfChannels;c++){const a=b.getChannelData(c);let drift=0;for(let i=0;i<a.length;i++){drift=drift*.985+(Math.random()*2-1)*.015;a[i]=(Math.random()*2-1)*.78+drift*.22}}this.noiseBuffer=b;
        this.setupAmbience();this.startMusic();if(!this.pageHook){this.pageHook=true;addEventListener('pagehide',()=>this.dispose(),{once:true})}
      },
      setVolume(v){this.volume=Math.max(0,Math.min(1,Number(v)||0));if(this.master&&this.ctx)this.master.gain.setTargetAtTime(this.muted?0:this.volume,this.ctx.currentTime,.035)},
      toggle(){this.muted=!this.muted;if(!this.muted&&this.ctx?.state==='suspended')this.ctx.resume().catch(()=>{});this.setVolume(this.volume);return this.muted},
      output(bus){return bus==='music'?this.music:bus==='ambience'?this.ambience:this.sfxBus},
      route(node,bus,pan=0){const out=this.output(bus);if(!out)return null;if(this.ctx.createStereoPanner){const p=this.ctx.createStereoPanner();p.pan.value=Math.max(-1,Math.min(1,pan));node.connect(p);p.connect(out);return p}node.connect(out);return null},
      tone(freq,dur=.12,type='sine',vol=.08,when=0,bus='sfx',slide=1,pan=0,attack=.012){
        if(!this.ctx||this.ctx.state!=='running'||this.muted)return;const t=this.ctx.currentTime+Math.max(0,when),o=this.ctx.createOscillator(),f=this.ctx.createBiquadFilter(),g=this.ctx.createGain(),end=t+Math.max(.035,dur),p=this.route(g,bus,pan);o.type=type;o.frequency.setValueAtTime(Math.max(28,freq),t);if(slide!==1)o.frequency.exponentialRampToValueAtTime(Math.max(28,freq*slide),end);f.type='lowpass';f.frequency.setValueAtTime(type==='sawtooth'||type==='square'?2600:4200,t);f.Q.value=.7;g.gain.setValueAtTime(.0001,t);g.gain.exponentialRampToValueAtTime(Math.max(.001,vol),t+Math.min(attack,dur*.35));g.gain.exponentialRampToValueAtTime(.0001,end);o.connect(f);f.connect(g);o.start(t);o.stop(end+.025);o.onended=()=>{o.disconnect();f.disconnect();g.disconnect();p?.disconnect()}
      },
      pluck(freq,dur=.42,vol=.05,when=0,bus='music',pan=0){
        if(!this.ctx||this.ctx.state!=='running'||this.muted)return;const t=this.ctx.currentTime+Math.max(0,when),end=t+dur,o=this.ctx.createOscillator(),h=this.ctx.createOscillator(),f=this.ctx.createBiquadFilter(),g=this.ctx.createGain(),hg=this.ctx.createGain(),p=this.route(g,bus,pan);o.type='triangle';h.type='sine';o.frequency.setValueAtTime(freq,t);h.frequency.setValueAtTime(freq*2.01,t);f.type='lowpass';f.frequency.setValueAtTime(3200,t);f.frequency.exponentialRampToValueAtTime(650,end);f.Q.value=1.4;g.gain.setValueAtTime(.0001,t);g.gain.exponentialRampToValueAtTime(Math.max(.001,vol),t+.009);g.gain.exponentialRampToValueAtTime(.0001,end);hg.gain.value=.22;o.connect(f);h.connect(hg);hg.connect(f);f.connect(g);o.start(t);h.start(t);o.stop(end+.025);h.stop(end+.025);o.onended=()=>{o.disconnect();h.disconnect();hg.disconnect();f.disconnect();g.disconnect();p?.disconnect()}
      },
      horn(freq,dur=.8,vol=.045,when=0,bus='music',pan=0){
        if(!this.ctx||this.ctx.state!=='running'||this.muted)return;const t=this.ctx.currentTime+Math.max(0,when),end=t+dur,o=this.ctx.createOscillator(),h=this.ctx.createOscillator(),f=this.ctx.createBiquadFilter(),g=this.ctx.createGain(),hg=this.ctx.createGain(),p=this.route(g,bus,pan);o.type='sawtooth';h.type='triangle';o.frequency.setValueAtTime(freq,t);h.frequency.setValueAtTime(freq*1.005,t);f.type='lowpass';f.frequency.value=1100;f.Q.value=1.8;g.gain.setValueAtTime(.0001,t);g.gain.linearRampToValueAtTime(Math.max(.001,vol),t+Math.min(.09,dur*.25));g.gain.exponentialRampToValueAtTime(.0001,end);hg.gain.value=.5;o.connect(f);h.connect(hg);hg.connect(f);f.connect(g);o.start(t);h.start(t);o.stop(end+.03);h.stop(end+.03);o.onended=()=>{o.disconnect();h.disconnect();hg.disconnect();f.disconnect();g.disconnect();p?.disconnect()}
      },
      bell(freq,dur=1,vol=.035,when=0,bus='sfx',pan=0){
        if(!this.ctx||this.ctx.state!=='running'||this.muted)return;const t=this.ctx.currentTime+Math.max(0,when),mix=this.ctx.createGain(),p=this.route(mix,bus,pan),partials=[1,2.01,3.97],nodes=[];mix.gain.value=1;partials.forEach((m,i)=>{const o=this.ctx.createOscillator(),g=this.ctx.createGain(),end=t+dur*(1-i*.18);o.type='sine';o.frequency.value=freq*m;g.gain.setValueAtTime(.0001,t);g.gain.exponentialRampToValueAtTime(Math.max(.001,vol/(1+i*1.7)),t+.008+i*.004);g.gain.exponentialRampToValueAtTime(.0001,end);o.connect(g);g.connect(mix);o.start(t);o.stop(end+.025);nodes.push(o,g)});nodes[0].onended=()=>{for(const n of nodes)n.disconnect();mix.disconnect();p?.disconnect()}
      },
      noise(dur=.1,filter=1000,vol=.08,when=0,bus='sfx',kind='bandpass',q=1.1,pan=0){
        if(!this.ctx||this.ctx.state!=='running'||this.muted||!this.noiseBuffer)return;const t=this.ctx.currentTime+Math.max(0,when),s=this.ctx.createBufferSource(),f=this.ctx.createBiquadFilter(),g=this.ctx.createGain(),p=this.route(g,bus,pan),end=t+Math.max(.03,dur);s.buffer=this.noiseBuffer;f.type=kind;f.frequency.value=filter;f.Q.value=q;g.gain.setValueAtTime(Math.max(.001,vol),t);g.gain.exponentialRampToValueAtTime(.0001,end);s.connect(f);f.connect(g);s.start(t,Math.random()*Math.max(.01,this.noiseBuffer.duration-dur));s.stop(end+.02);s.onended=()=>{s.disconnect();f.disconnect();g.disconnect();p?.disconnect()}
      },
      drum(vol=.055,when=0,bus='music',pan=0){this.noise(.13,180,vol*.72,when,bus,'lowpass',.6,pan);this.tone(92,.2,'sine',vol,when,bus,.48,pan,.004)},
      setupAmbience(){
        if(!this.ctx||!this.noiseBuffer||this.ambientSources.length)return;const make=(type,freq,q,gain,pan)=>{const s=this.ctx.createBufferSource(),f=this.ctx.createBiquadFilter(),g=this.ctx.createGain(),p=this.route(g,'ambience',pan);s.buffer=this.noiseBuffer;s.loop=true;f.type=type;f.frequency.value=freq;f.Q.value=q;g.gain.value=gain;s.connect(f);f.connect(g);s.start();this.ambientSources.push({source:s,nodes:[f,g,p]});return g};
        const wind=make('lowpass',620,.3,.045,-.22),water=make('bandpass',1150,.55,.018,.3);if(!wind||!water)return;const lfo=this.ctx.createOscillator(),depth=this.ctx.createGain();lfo.frequency.value=reducedMotion?.045:.075;depth.gain.value=reducedMotion?.004:.009;lfo.connect(depth);depth.connect(wind.gain);lfo.start();this.ambientSources.push({source:lfo,nodes:[depth]})
      },
      scene(){
        if(!game)return{combat:0,age:1,index:0,settlement:0};let engaged=0,recent=0,settlement=0;for(const e of game.entities||[]){if(e.dead)continue;if(e.kind==='unit'&&(e.order?.type==='attack'||e.order?.type==='attackMove'))engaged++;if(e.lastHit&&game.time-e.lastHit<2.4)recent++;if(e.faction===0&&e.kind==='building'&&e.construction>=1)settlement++}const bolts=(game.projectiles||[]).reduce((n,p)=>n+(!p.dead),0),combat=Math.min(1,Math.max(0,(game.combat||0)+engaged*.025+recent*.055+bolts*.045)),keys=Object.keys(CIVS),index=Math.max(0,keys.indexOf(game.player?.civ));return{combat,age:game.player?.age||1,index,settlement:Math.min(1,settlement/18)}
      },
      sfx(name){
        if(!this.ctx||this.ctx.state!=='running'||this.muted)return;const now=this.ctx.currentTime,cool={click:.045,select:.075,move:.11,build:.18,arrow:.05,sword:.065,siege:.16,alert:1.4,age:.45,power:.6,win:1,lose:1}[name]||0;if(now-(this.lastSfx[name]??-99)<cool)return;this.lastSfx[name]=now;const v=.9+Math.random()*.2,pan=(Math.random()-.5)*.34;
        switch(name){
          case'click':this.tone(430*v,.055,'triangle',.038,0,'sfx',.82,pan,.004);this.tone(680*v,.045,'sine',.018,.025,'sfx',.9,pan);break;
          case'select':this.pluck(470*v,.18,.04,0,'sfx',pan);this.tone(760*v,.1,'sine',.018,.045,'sfx',.97,pan);break;
          case'move':this.tone(220*v,.085,'triangle',.035,0,'sfx',.78,pan,.004);this.pluck(315*v,.14,.021,.045,'sfx',-pan);break;
          case'build':this.noise(.055,820*v,.045,0,'sfx','bandpass',1.7,pan);this.tone(128*v,.11,'triangle',.05,0,'sfx',.72,pan,.003);this.noise(.035,1450,.022,.065,'sfx','bandpass',2,-pan);break;
          case'arrow':this.noise(.095,2500*v,.026,0,'sfx','highpass',.45,pan);this.tone(1180*v,.105,'sine',.018,0,'sfx',.48,pan,.003);break;
          case'sword':this.noise(.085,1900*v,.041,0,'sfx','bandpass',3.2,pan);this.tone(235*v,.07,'square',.025,0,'sfx',.72,pan,.002);this.tone(1800*v,.12,'sine',.016,.012,'sfx',.91,-pan,.002);break;
          case'siege':this.drum(.115,0,'sfx',pan);this.noise(.36,125,.095,0,'sfx','lowpass',.5,pan);this.tone(54*v,.38,'sine',.095,0,'sfx',.38,pan,.003);break;
          case'alert':this.horn(174,.52,.062,0,'sfx',-.08);this.horn(146,.66,.058,.24,'sfx',.08);this.drum(.045,.04,'sfx');break;
          case'age':[0,4,7,12].forEach((n,i)=>this.pluck(262*Math.pow(2,n/12),.72,.046,i*.105,'sfx',(i-1.5)*.12));this.bell(523,1.35,.045,.38,'sfx');this.horn(131,1.05,.038,.12,'sfx');break;
          case'power':[0,7,12,16].forEach((n,i)=>this.horn(147*Math.pow(2,n/12),.78,.047,i*.09,'sfx',(i-1.5)*.1));this.drum(.085,0,'sfx');this.drum(.075,.28,'sfx');this.bell(587,1.2,.035,.34,'sfx');break;
          case'win':[0,4,7,12,16,19].forEach((n,i)=>{const f=196*Math.pow(2,n/12);this.horn(f,1.2,.054,i*.13,'sfx',(i-2.5)*.08);this.bell(f*2,1.7,.028,.12+i*.13,'sfx',(2.5-i)*.08)});[0,.27,.54,.82].forEach(t=>this.drum(.085,t,'sfx'));break;
          case'lose':[0,-2,-5,-9].forEach((n,i)=>this.horn(165*Math.pow(2,n/12),1.05,.046,i*.22,'sfx',(i-1.5)*.09));this.noise(1.5,180,.045,.3,'sfx','lowpass',.4);this.bell(110,1.8,.028,.7,'sfx');break;
        }
      },
      startMusic(){
        if(this.timer)return;const tick=()=>{if(!this.ctx)return;const active=this.ctx.state==='running'&&!this.muted&&game&&!game.paused&&!game.ended,t=this.ctx.currentTime;if(this.music)this.music.gain.setTargetAtTime(active?.2:0,t,.2);if(this.ambience)this.ambience.gain.setTargetAtTime(active?.12:0,t,.28);if(!active)return;const s=this.scene(),roots=[110,116.54,123.47,130.81,138.59,146.83,155.56,164.81,103.83,123.47,110,146.83,98],modes=[[0,2,3,5,7,9,10],[0,2,4,5,7,9,11],[0,2,3,5,7,8,10],[0,2,4,7,9,12,14]],scale=modes[s.index%modes.length],progression=[0,5,3,7],root=roots[s.index%roots.length],bar=Math.floor(this.step/8),harm=progression[bar%progression.length],motif=[0,2,4,3,1,5,4,2],degree=(motif[(this.step+s.index)%motif.length]+bar+s.index)%scale.length,note=scale[degree]+harm+(this.step%8>5?12:0),freq=root*Math.pow(2,note/12),dense=s.combat>.18||this.step%2===0;
          if(dense)this.pluck(freq,.34+s.age*.035,.03+s.combat*.018,0,'music',((this.step%5)-2)*.1);if(this.step%4===0)this.horn(root/2*Math.pow(2,harm/12),1.25,.024+s.combat*.02,0,'music',-.12);if(this.step%8===0){[0,scale[2],scale[4]].forEach((n,i)=>this.pluck(root*Math.pow(2,(harm+n+12)/12),.86,.021,i*.055,'music',(i-1)*.22));if(s.age>2)this.bell(root*4*Math.pow(2,harm/12),1.3,.012,.12,'music',.28)}if(s.combat>.14&&(s.combat>.55||this.step%2===0))this.drum(.035+s.combat*.025,0,'music',(this.step%2?-.18:.18));if(s.combat>.58&&this.step%4===2)this.horn(root*Math.pow(2,(harm+7)/12),.58,.026+s.combat*.012,0,'music',.16);if(s.combat<.18&&this.step%16===7&&Math.random()<(reducedMotion?.35:.65))this.bell(880+110*(s.index%3),.8,.008+s.settlement*.006,0,'ambience',.36);if(s.combat<.1&&this.step%12===5&&s.settlement>.25)this.noise(.045,1550,.008,0,'ambience','bandpass',2.8,-.25);this.step++};tick();this.timer=setInterval(tick,360)},
      stop(){if(!this.ctx)return;const t=this.ctx.currentTime;this.music?.gain.setTargetAtTime(0,t,.08);this.ambience?.gain.setTargetAtTime(0,t,.08);this.ctx.suspend().catch(()=>{})},
      dispose(){if(this.timer){clearInterval(this.timer);this.timer=null}for(const a of this.ambientSources){try{a.source.stop()}catch{}try{a.source.disconnect()}catch{}for(const n of a.nodes||[])try{n?.disconnect()}catch{}}this.ambientSources.length=0;if(this.ctx){this.ctx.close().catch(()=>{});this.ctx=null}this.master=this.music=this.ambience=this.sfxBus=null}
    };
