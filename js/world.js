'use strict';
let terrainAtlasHooked=false;
function civMods(civKey){return CIVS[civKey]?.mods||{}}
function powerMods(faction){return powerActive(faction)?(CIVS[playerFor(faction)?.civ]?.powerMods||{}):{}}
function roleModifier(table,def,type,fallback=1){if(Number.isFinite(table))return table;if(!table)return fallback;return table[type]??table[def?.role]??(def?.ranged?table.ranged:undefined)??table.all??fallback}
function civAdjustedCost(def,civKey){const c={...(def.cost||{})},mods=civMods(civKey),field=def.role?'unitCost':'buildingCost',mult=roleModifier(mods[field],def,def.key,1);for(const k in c)c[k]=Math.ceil(c[k]*mult);return c}
    function canAfford(player,cost){return Object.entries(cost||{}).every(([k,v])=>player.res[k]>=v)}
    function spend(player,cost){if(!canAfford(player,cost))return false;for(const k in cost)player.res[k]-=cost[k];return true}
    function refund(player,cost,ratio=.7){for(const k in cost)player.res[k]+=cost[k]*ratio}
    function ageCost(player){const base=player.age===1?{food:500}:player.age===2?{food:800,gold:200}:player.age===3?{food:1000,gold:800}:null;if(!base)return null;const mult=civMods(player.civ).ageCost||1;return Object.fromEntries(Object.entries(base).map(([k,v])=>[k,Math.ceil(v*mult)]))}
    function ownsCompletedBuilding(faction,type){return game.entities.some(e=>!e.dead&&e.faction===faction&&e.kind==='building'&&e.type===type&&e.construction>=1)}
    function ageRequirement(player){const owns=type=>ownsCompletedBuilding(player.faction,type);if(player.age===1)return{met:owns('mill')&&owns('lumber'),text:'需先完成磨坊與伐木場'};if(player.age===2)return{met:owns('blacksmith')&&(owns('range')||owns('stable')),text:'需先完成鐵匠鋪，並擁有靶場或馬廄'};if(player.age===3)return{met:owns('castle'),text:'需先完成一座城堡'};return{met:false,text:'已達最高時代'}}
    function makePlayer(faction,civ){const base=faction?{food:580,wood:520,gold:300,stone:220}:{food:320,wood:280,gold:140,stone:120},start=civMods(civ).startRes||{},res=Object.fromEntries(Object.entries(base).map(([k,v])=>[k,Math.round(v*(start[k]??start.all??1))]));return{faction,civ,color:FACTION_COLORS[faction]||CIVS[civ].color,res,age:1,pop:0,popCap:0,tech:{attack:0,armor:0,economy:0},ageUp:null,powerReady:0,powerUntil:0,score:0,kills:0,losses:0,eliminated:false}}
    function entityById(id){return game?.entities.find(e=>e.id===id)||game?.nodes.find(e=>e.id===id)||null}
    function maxHpFor(def,faction,civKey){const mods=civMods(civKey),mult=BUILD[def.key]?(mods.buildingHp||1):roleModifier(mods.unitHp,def,def.key,1);return Math.round(def.hp*mult)}
    function createUnit(type,faction,x,y){const d=UNIT[type],p=playerFor(faction),civ=p.civ,mods=civMods(civ),speed=d.speed*roleModifier(mods.unitSpeed,d,type,1),damage=d.damage*roleModifier(mods.unitDamage,d,type,1),armor=d.armor+roleModifier(mods.unitArmor,d,type,0)+(p.tech.armor||0),range=d.range*roleModifier(mods.unitRange,d,type,1),cool=d.cool*roleModifier(mods.unitCooldown,d,type,1),maxHp=maxHpFor({...d,key:type},faction,civ),radius=d.role==='siege'?16:d.role==='cavalry'?13:10;const u={id:nextId++,kind:'unit',type,faction,civ,x,y,prevX:x,prevY:y,radius,maxHp,hp:maxHp,armor,speed,damage,range,cool,attackTimer:rnd()*.4,anim:rnd()*10,angle:0,order:{type:'idle'},path:[],pathIndex:0,selected:false,dead:false,carrying:null,workTimer:0,flash:0,lastHit:0};game.entities.push(u);p.pop+=d.pop;return u}
    function createBuilding(type,faction,x,y,complete=1){const d=BUILD[type],p=playerFor(faction),civ=p.civ,mods=civMods(civ),maxHp=maxHpFor({...d,key:type},faction,civ),dx=WORLD_W*.5-x,dy=WORLD_H*.5-y,len=Math.hypot(dx,dy)||1,food=(d.food||0)*(type==='farm'?(mods.farmYield||1):1);const b={id:nextId++,kind:'building',type,faction,civ,x,y,prevX:x,prevY:y,radius:d.size,maxHp,hp:Math.max(1,maxHp*complete),construction:complete,buildTime:d.time||1,queue:[],attackTimer:0,selected:false,dead:false,flash:0,food,rally:{x:x+dx/len*105,y:y+dy/len*105},wonderTimer:0};game.entities.push(b);if(complete>=1&&d.pop)p.popCap=Math.min(MAX_POP,p.popCap+d.pop);return b}
    function createNode(type,x,y,amount=500,radius=18){const n={id:nextId++,kind:'resource',type,x,y,amount,radius,dead:false,wiggle:rnd()*10};game.nodes.push(n);return n}
    function addTreeCluster(cx,cy,count=16){for(let i=0;i<count;i++){const a=rnd()*Math.PI*2,r=22+Math.sqrt(rnd())*105,x=cx+Math.cos(a)*r,y=cy+Math.sin(a)*r;if(isLandPx(x,y))createNode('wood',x,y,260,14)}}
    function addMine(type,x,y){for(let i=0;i<5;i++){const a=i/5*Math.PI*2+rnd()*.3,r=10+rnd()*23;createNode(type,x+Math.cos(a)*r,y+Math.sin(a)*r,type==='gold'?520:460,17)}}
    function addBerries(x,y){for(let i=0;i<6;i++){const a=i/6*Math.PI*2,r=10+rnd()*27;createNode('food',x+Math.cos(a)*r,y+Math.sin(a)*r,210,12)}}
    function generateMap(){
      terrain=Array.from({length:MAP_H},()=>new Uint8Array(MAP_W));nav=Array.from({length:MAP_H},()=>new Uint8Array(MAP_W));
      const fords=[9,21,33];
      for(let y=0;y<MAP_H;y++)for(let x=0;x<MAP_W;x++){
        const river=MAP_W*.5+Math.sin(y*.28)*2.2+Math.sin(y*.67)*.65,nearFord=fords.some(f=>Math.abs(y-f)<=1);
        let t=Math.abs(x-river)<(nearFord?.72:1.6)?1:(hash2(x,y)>.84?2:0);
        if((x<12&&y>29)||(x>45&&y<13))t=0;
        terrain[y][x]=t;nav[y][x]=t===1?1:0;
      }
      renderTerrain();
    }
    function isLandCell(x,y){return x>=0&&y>=0&&x<MAP_W&&y<MAP_H&&nav[y][x]===0}
    function isLandPx(x,y){return isLandCell(Math.floor(x/TILE),Math.floor(y/TILE))}
    function nearestLandCell(cx,cy){if(isLandCell(cx,cy))return[cx,cy];for(let r=1;r<7;r++)for(let y=cy-r;y<=cy+r;y++)for(let x=cx-r;x<=cx+r;x++)if(isLandCell(x,y))return[x,y];return[clamp(cx,0,MAP_W-1),clamp(cy,0,MAP_H-1)]}
    function drawTerrainTile(tx,ty,type,atlas){
      const wx=tx*TILE,wy=ty*TILE,h=hash2(tx*7,ty*11);
      if(atlas){
        const qW=Math.floor(atlas.naturalWidth/2),qH=Math.floor(atlas.naturalHeight/2),quad=type===1?[0,qH]:type===2?[qW,0]:[0,0],sample=Math.min(220,qW,qH),roomX=Math.max(0,qW-sample),roomY=Math.max(0,qH-sample),sx=quad[0]+Math.floor(hash2(tx*31+type,ty*17)*roomX),sy=quad[1]+Math.floor(hash2(tx*13,ty*29+type)*roomY);
        terrainCtx.drawImage(atlas,sx,sy,sample,sample,wx,wy,TILE+.5,TILE+.5);
      }else{
        terrainCtx.fillStyle=type===1?(h>.5?'#315e64':'#294f58'):type===2?(h>.5?'#786241':'#665438'):(h>.5?'#66794e':'#526a43');terrainCtx.fillRect(wx,wy,TILE+.5,TILE+.5);
      }
      terrainCtx.fillStyle=type===1?'rgba(20,70,78,.14)':type===2?'rgba(95,66,34,.1)':'rgba(34,72,38,.08)';terrainCtx.fillRect(wx,wy,TILE+.5,TILE+.5);
      terrainCtx.strokeStyle=type===1?'rgba(151,211,207,.1)':'rgba(238,225,170,.045)';terrainCtx.lineWidth=.6;terrainCtx.strokeRect(wx+.3,wy+.3,TILE-.6,TILE-.6);
      if(type===1){terrainCtx.strokeStyle='rgba(184,225,218,.21)';terrainCtx.lineWidth=.75;for(let i=0;i<3;i++){const yy=wy+12+i*11+(h-.5)*4;terrainCtx.beginPath();terrainCtx.moveTo(wx+7,yy);terrainCtx.quadraticCurveTo(wx+TILE*.5,yy-2.5,wx+TILE-7,yy);terrainCtx.stroke()}}
      else if(!atlas){terrainCtx.fillStyle=type===2?'rgba(225,195,123,.2)':'rgba(217,231,157,.2)';for(let i=0;i<3;i++){const px=wx+hash2(tx*23+i,ty*17)*TILE,py=wy+hash2(tx*31,ty*29+i)*TILE;terrainCtx.fillRect(px,py,1.5,2.5)}}
    }
    function renderTerrain(){
      terrainCanvas=document.createElement('canvas');terrainCanvas.width=WORLD_W;terrainCanvas.height=WORLD_H;terrainCtx=terrainCanvas.getContext('2d');terrainCtx.imageSmoothingEnabled=true;terrainCtx.clearRect(0,0,terrainCanvas.width,terrainCanvas.height);
      const atlas=dom.atlas?.complete&&dom.atlas.naturalWidth>1&&dom.atlas.naturalHeight>1?dom.atlas:null;
      if(!atlas&&!terrainAtlasHooked&&dom.atlas?.addEventListener){terrainAtlasHooked=true;dom.atlas.addEventListener('load',()=>{if(terrain.length)renderTerrain()},{once:true})}
      for(let y=0;y<MAP_H;y++)for(let x=0;x<MAP_W;x++)drawTerrainTile(x,y,terrain[y][x],atlas);
      terrainCtx.lineCap='round';terrainCtx.strokeStyle='rgba(30,24,15,.18)';terrainCtx.lineWidth=18;terrainCtx.beginPath();terrainCtx.moveTo(340,1650);terrainCtx.bezierCurveTo(800,1380,1060,1170,1400,1010);terrainCtx.bezierCurveTo(1740,850,2000,640,2420,360);terrainCtx.stroke();terrainCtx.strokeStyle='rgba(255,239,190,.13)';terrainCtx.lineWidth=11;terrainCtx.setLineDash([5,13]);terrainCtx.stroke();terrainCtx.setLineDash([]);
      const shade=terrainCtx.createRadialGradient(WORLD_W*.5,WORLD_H*.5,100,WORLD_W*.5,WORLD_H*.5,Math.max(WORLD_W,WORLD_H)*.62);shade.addColorStop(.55,'rgba(0,0,0,0)');shade.addColorStop(1,'rgba(7,15,12,.28)');terrainCtx.globalCompositeOperation='source-atop';terrainCtx.fillStyle=shade;terrainCtx.fillRect(0,0,terrainCanvas.width,terrainCanvas.height);terrainCtx.globalCompositeOperation='source-over';
    }
    function setupWorld(){
      const corners=[
        {x:420,y:WORLD_H-381,inX:1,inY:-1},
        {x:WORLD_W-420,y:380,inX:-1,inY:1},
        {x:420,y:380,inX:1,inY:1},
        {x:WORLD_W-420,y:WORLD_H-381,inX:-1,inY:-1}
      ],cfg=DIFF[difficulty]||DIFF['征戰'];
      game.spawn=corners.slice(0,game.players.length).map(s=>({x:s.x,y:s.y}));
      for(let faction=0;faction<game.players.length;faction++){
        const s=corners[faction],length=Math.hypot(s.inX,s.inY),forward={x:s.inX/length,y:s.inY/length},right={x:-forward.y,y:forward.x},at=(f,r)=>({x:clamp(s.x+forward.x*f+right.x*r,80,WORLD_W-80),y:clamp(s.y+forward.y*f+right.y*r,80,WORLD_H-80)});
        createBuilding('town',faction,s.x,s.y,1);
        for(let i=0;i<5;i++){const q=at(-82,(i-2)*27);createUnit('villager',faction,q.x,q.y)}
        let q=at(112,34);createUnit('scout',faction,q.x,q.y);
        q=at(-20,235);addTreeCluster(q.x,q.y,18);q=at(280,130);addTreeCluster(q.x,q.y,15);
        q=at(110,-220);addMine('gold',q.x,q.y);q=at(245,-20);addMine('stone',q.x,q.y);q=at(-130,-185);addBerries(q.x,q.y);
        for(let i=0;i<cfg.start;i++){q=at(145,(i-(cfg.start-1)*.5)*25);createUnit('swordsman',faction,q.x,q.y)}
      }
      [[850,520],[900,1040],[700,1290],[2010,750],[1940,1430],[1740,1760]].forEach((q,i)=>{i%3===0?addMine('gold',...q):i%3===1?addTreeCluster(...q,12):addBerries(...q)});
      game.sites=[9,21,33].map((ty,i)=>({id:nextId++,x:(MAP_W*.5+Math.sin(ty*.28)*2.2)*TILE+TILE/2,y:ty*TILE+TILE/2,owner:-1,progress:0,captureBy:-1,contested:false,label:`第${['一','二','三'][i]}王旗`}));
    }
    function newGame(){
      seed=(Date.now()^Math.floor(Math.random()*0xffffffff))|0;nextId=1;controlGroups=[[],[],[],[]];buildMode=null;attackMove=false;
      const count=clamp(Math.round(Number(playerCount)||2),2,4),cfg=DIFF[difficulty]||DIFF['征戰'],civPool=Object.keys(CIVS).filter(k=>k!==chosenCiv);for(let i=civPool.length-1;i>0;i--){const j=Math.floor(rnd()*(i+1));[civPool[i],civPool[j]]=[civPool[j],civPool[i]]}playerCount=count;
      const players=[makePlayer(0,chosenCiv)];for(let faction=1;faction<count;faction++)players.push(makePlayer(faction,civPool[faction-1]));const ais=Array(count).fill(null);for(let faction=1;faction<count;faction++)ais[faction]={faction,think:cfg.think*(.28+faction*.12),wave:cfg.wave*(.82+rnd()*.28),build:7+faction*1.15+rnd()*1.5,train:2.8+faction*.45+rnd()};const tutorialActive=!!tutorialRequested;tutorialRequested=false;
      game={running:true,paused:false,ended:false,time:0,tick:0,speed:1,combat:0,playerCount:count,camera:{x:WORLD_W*.5,y:WORLD_H*.5,zoom:1,projection:'topdown-v1'},players,player:players[0],enemy:players[1],enemies:players.slice(1),ais,ai:ais[1],entities:[],nodes:[],sites:[],projectiles:[],particles:[],markers:[],selected:new Set(),fog:new Uint8Array(MAP_W*MAP_H),fogTimer:0,minimapTimer:0,autoSaveIn:30,supremacy:Array(count).fill(0),wonder:Array(count).fill(0),stats:{gathered:0,trained:0,built:0},tutorial:{active:tutorialActive,step:0,flags:{},granted:[],completed:false,checkIn:0},difficulty};
      generateMap();setupWorld();resize();centerCamera(game.spawn[0].x,game.spawn[0].y);resetCameraInputAnchor();updateFog(true);renderMenuState();dom.menu.classList.add('hidden');dom.game.classList.remove('hidden');Audio.init();Audio.ctx?.resume();updateHUD(true);notify(`斥候回報：敵對勢力為${players.slice(1).map(p=>CIVS[p.civ].name).join('、')}。`,'danger');notify('先分派村民採集木材與食物，再興建軍營。','good');last=performance.now();accumulator=0;
    }
    function findPath(sx,sy,tx,ty){
      let ax=clamp(Math.floor(sx/TILE),0,MAP_W-1),ay=clamp(Math.floor(sy/TILE),0,MAP_H-1),bx=clamp(Math.floor(tx/TILE),0,MAP_W-1),by=clamp(Math.floor(ty/TILE),0,MAP_H-1);[bx,by]=nearestLandCell(bx,by);const start=ay*MAP_W+ax,goal=by*MAP_W+bx;if(start===goal)return[{x:tx,y:ty}];
      const total=MAP_W*MAP_H,g=new Float32Array(total);g.fill(Infinity);const parent=new Int32Array(total);parent.fill(-1);const open=[start],inOpen=new Uint8Array(total),closed=new Uint8Array(total);g[start]=0;inOpen[start]=1;const dirs=[[1,0,1],[-1,0,1],[0,1,1],[0,-1,1],[1,1,1.414],[-1,1,1.414],[1,-1,1.414],[-1,-1,1.414]];
      let found=false,loops=0;while(open.length&&loops++<total*2){let best=0,bestF=Infinity;for(let i=0;i<open.length;i++){const n=open[i],x=n%MAP_W,y=(n/MAP_W)|0,h=Math.max(Math.abs(x-bx),Math.abs(y-by));if(g[n]+h<bestF){bestF=g[n]+h;best=i}}const cur=open.splice(best,1)[0];inOpen[cur]=0;if(cur===goal){found=true;break}closed[cur]=1;const cx=cur%MAP_W,cy=(cur/MAP_W)|0;for(const[dX,dY,c]of dirs){const nx=cx+dX,ny=cy+dY;if(!isLandCell(nx,ny))continue;if(dX&&dY&&(!isLandCell(cx+dX,cy)||!isLandCell(cx,cy+dY)))continue;const ni=ny*MAP_W+nx;if(closed[ni])continue;const ng=g[cur]+c;if(ng<g[ni]){g[ni]=ng;parent[ni]=cur;if(!inOpen[ni]){open.push(ni);inOpen[ni]=1}}}}
      if(!found)return[{x:(bx+.5)*TILE,y:(by+.5)*TILE}];const out=[];let n=goal;while(n!==start&&n>=0){out.push({x:(n%MAP_W+.5)*TILE,y:(((n/MAP_W)|0)+.5)*TILE});n=parent[n]}out.reverse();const smooth=[];for(let i=0;i<out.length;i++){if(i===out.length-1||i%3===2)smooth.push(out[i])}smooth[smooth.length-1]={x:tx,y:ty};return smooth
    }
    /* Camera contract: x/y are the world-space point at the exact viewport
       centre. px/py are derived world-space top-left coordinates, retained so
       the terrain renderer can crop its cache without duplicating camera math. */
    function syncCameraProjection(){const c=game.camera;c.px=c.x-viewW/(2*c.zoom);c.py=c.y-viewH/(2*c.zoom)}
    function clampCamera(){if(!game)return;const c=game.camera;c.zoom=clamp(Number.isFinite(c.zoom)?c.zoom:1,.62,1.65);const halfW=Math.max(0,viewW)/(2*c.zoom),halfH=Math.max(0,viewH)/(2*c.zoom);c.x=halfW*2>=WORLD_W?WORLD_W*.5:clamp(Number.isFinite(c.x)?c.x:WORLD_W*.5,halfW,WORLD_W-halfW);c.y=halfH*2>=WORLD_H?WORLD_H*.5:clamp(Number.isFinite(c.y)?c.y:WORLD_H*.5,halfH,WORLD_H-halfH);c.projection='topdown-v1';syncCameraProjection()}
    function centerCamera(x,y){if(!game)return;game.camera.x=x;game.camera.y=y;clampCamera()}
    function cameraProjectedTopLeft(){const c=game.camera;return{x:Number.isFinite(c.px)?c.px:c.x-viewW/(2*c.zoom),y:Number.isFinite(c.py)?c.py:c.y-viewH/(2*c.zoom)}}
    function worldToScreen(x,y,height=0){const c=cameraProjectedTopLeft(),z=game.camera.zoom;return{x:(x-c.x)*z,y:(y-height-c.y)*z}}
    function screenToWorld(x,y){const c=cameraProjectedTopLeft(),z=game.camera.zoom;return{x:c.x+x/z,y:c.y+y/z}}
    function panCameraScreen(dx,dy){if(!game)return;game.camera.x+=dx/game.camera.zoom;game.camera.y+=dy/game.camera.zoom;clampCamera()}
    function zoomCameraAt(sx,sy,newZoom){if(!game)return;const before=screenToWorld(sx,sy),z=clamp(Number(newZoom)||game.camera.zoom,.62,1.65);game.camera.zoom=z;game.camera.x=before.x+(viewW*.5-sx)/z;game.camera.y=before.y+(viewH*.5-sy)/z;clampCamera()}
