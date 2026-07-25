'use strict';

/*
 * True top-down renderer. The simulation, terrain cache, camera and picking all
 * share the same Cartesian world coordinates. Characters are deliberately
 * oversized and high-contrast so their pose and orders remain legible at the
 * minimum camera zoom.
 */

const RENDER_FONT='"Microsoft JhengHei","PingFang TC",sans-serif';
const TAU=Math.PI*2;
const MEDIEVAL_ATLAS_SRC='assets/medieval-terrain-atlas-v2.png';
let medievalAtlasImage=null,medievalAtlasState=0,medievalTerrainCache=null,medievalTerrainSource=null,medievalTerrainMap=null;
let frameWorkTargets=new Map(),frameVisibleUnits=0;

function renderZoom(){return game?.camera?.zoom||1}
function onScreen(p,margin=120){return p.x>-margin&&p.y>-margin&&p.x<viewW+margin&&p.y<viewH+margin}
function polygon(c,points){if(!points.length)return;c.beginPath();c.moveTo(points[0][0],points[0][1]);for(let i=1;i<points.length;i++)c.lineTo(points[i][0],points[i][1]);c.closePath()}
function roundedRect(c,x,y,w,h,r){r=Math.max(0,Math.min(r,w/2,h/2));c.beginPath();c.moveTo(x+r,y);c.arcTo(x+w,y,x+w,y+h,r);c.arcTo(x+w,y+h,x,y+h,r);c.arcTo(x,y+h,x,y,r);c.arcTo(x,y,x+w,y,r);c.closePath()}
function shade(hex,mult=1){
  const m=/^#([0-9a-f]{6})$/i.exec(hex||'');if(!m)return hex||'#888888';
  const n=parseInt(m[1],16),r=clamp(Math.round((n>>16)*mult),0,255),g=clamp(Math.round(((n>>8)&255)*mult),0,255),b=clamp(Math.round((n&255)*mult),0,255);
  return`rgb(${r},${g},${b})`;
}
function factionColor(faction,neutral='#d4b568'){
  if(!Number.isFinite(faction)||faction<0)return neutral;
  if(faction===0)return CIVS[game.player.civ]?.color||'#5bc5d8';
  return(typeof FACTION_COLORS!=='undefined'&&FACTION_COLORS[faction])||'#df6159';
}
function teamColor(e){return factionColor(Number(e.faction))}
function entityWorldPosition(e,alpha=1){
  if(e.kind==='unit'&&Number.isFinite(e.prevX))return{x:lerp(e.prevX,e.x,alpha),y:lerp(e.prevY,e.y,alpha)};
  return{x:e.x,y:e.y};
}
function worldRectPath(cx,cy,halfW,halfH=halfW){
  const a=worldToScreen(cx-halfW,cy-halfH),b=worldToScreen(cx+halfW,cy+halfH);
  ctx.beginPath();ctx.rect(a.x,a.y,b.x-a.x,b.y-a.y);
}
function worldDiamondPath(cx,cy,half){
  const p=worldToScreen(cx,cy),z=renderZoom();polygon(ctx,[[p.x,p.y-half*z],[p.x+half*z,p.y],[p.x,p.y+half*z],[p.x-half*z,p.y]]);
}
function tileRectPath(tx,ty){worldRectPath((tx+.5)*TILE,(ty+.5)*TILE,TILE*.505,TILE*.505)}
function tileDiamondPath(tx,ty){tileRectPath(tx,ty)}
function localDiamond(c,w,d,y=0){polygon(c,[[0,y-d],[w,y],[0,y+d],[-w,y]])}
function motionAmount(){return reducedMotion?.3:1}
function windAt(x=0,y=0,time=game?.time||0){const a=motionAmount();return{x:(Math.sin(time*.72+x*.0021+y*.0013)*2.4+1.4)*a,y:Math.cos(time*.47+x*.0017-y*.0011)*1.15*a}}

function spawnParticle(x,y,color,size=2){
  if(game.particles.length>420)return;
  game.particles.push({x,y,baseX:x,baseY:y,vx:(rnd()-.5)*34,vy:-12-rnd()*28,life:.55+rnd()*.4,max:.95,color,size});
}
function spawnFloat(x,y,text,color){
  if(game.particles.length>420)return;
  game.particles.push({x,y,baseX:x,baseY:y,vx:(rnd()-.5)*4,vy:-22,life:.8,max:.8,color,size:12,text});
}
function burst(x,y,color,count=9){
  if(reducedMotion)count=Math.ceil(count*.35);for(let i=0;i<count;i++)spawnParticle(x+(rnd()-.5)*8,y+(rnd()-.5)*8,color,2+rnd()*3);
  if(game.particles.length<420&&count>=3){const effect=count>=20?'siegeExplosion':count>=11?'dust':count<=4?'arrowImpact':'embers';game.particles.push({x,y,baseX:x,baseY:y,vx:0,vy:0,life:.28,max:.28,color,size:Math.min(22,8+count*.55),ring:true,effect})}
}
function marker(x,y,type='move'){
  game.markers.push({x,y,type,life:.65,max:.65});
  const color=type==='attack'?'#f06d62':type==='gather'?'#7ed499':'#e9c872';for(let i=0;i<5;i++)spawnParticle(x,y,color,2);
}
function visibleAt(x,y){const tx=clamp((x/TILE)|0,0,MAP_W-1),ty=clamp((y/TILE)|0,0,MAP_H-1);return game.fog[ty*MAP_W+tx]===2}
function exploredAt(x,y){const tx=clamp((x/TILE)|0,0,MAP_W-1),ty=clamp((y/TILE)|0,0,MAP_H-1);return game.fog[ty*MAP_W+tx]>0}
function indexFrameActivity(){
  frameWorkTargets=new Map();
  for(const u of game.entities){
    if(u.dead||u.kind!=='unit'||u.type!=='villager')continue;const state=unitActivityState(u);if(!state.active||!state.target)continue;
    const entry=frameWorkTargets.get(state.target.id)||{count:0,kind:state.kind,resource:state.resource||'build',workers:[]};entry.count++;entry.workers.push(u);frameWorkTargets.set(state.target.id,entry);
  }
}
function targetWork(target){return frameWorkTargets.get(target?.id)||null}
function workBeat(id=0,speed=1){const cycle=(game.time*(1.72*speed)+(Number(id)||0)*.137)%1,windup=cycle<.58?cycle/.58:1-(cycle-.58)/.42;return{cycle,swing:clamp(windup,0,1),impact:cycle>=.55&&cycle<.68}}

function ensureMedievalAtlas(){
  if(medievalAtlasState!==0||typeof Image==='undefined')return;medievalAtlasState=1;
  try{
    const image=new Image();medievalAtlasImage=image;
    image.addEventListener('load',()=>{medievalAtlasState=2;medievalTerrainCache=null;if(game&&terrain?.length)buildMedievalTerrainCache()},{once:true});
    image.addEventListener('error',()=>{medievalAtlasState=-1;medievalAtlasImage=null;medievalTerrainCache=null},{once:true});image.src=MEDIEVAL_ATLAS_SRC;
  }catch{medievalAtlasState=-1;medievalAtlasImage=null}
}
function buildMedievalTerrainCache(){
  const atlas=medievalAtlasImage;if(medievalAtlasState!==2||!atlas?.naturalWidth||!terrainCanvas?.width||!terrain?.length)return null;
  const cache=document.createElement('canvas');cache.width=terrainCanvas.width;cache.height=terrainCanvas.height;const c=cache.getContext('2d');if(!c)return null;
  c.imageSmoothingEnabled=true;c.imageSmoothingQuality='high';c.drawImage(terrainCanvas,0,0);const qW=Math.floor(atlas.naturalWidth/2),qH=Math.floor(atlas.naturalHeight/2),margin=12,sample=Math.max(32,Math.min(250,qW-margin*2,qH-margin*2)),roomX=Math.max(0,qW-margin*2-sample),roomY=Math.max(0,qH-margin*2-sample);
  c.globalAlpha=.42;
  for(let y=0;y<MAP_H;y++)for(let x=0;x<MAP_W;x++){
    const type=terrain[y][x],quad=type===1?[qW,0]:type===2?[0,qH]:type===3?[qW,qH]:[0,0],sx=quad[0]+margin+Math.floor(hash2(x*31+type,y*17+5)*roomX),sy=quad[1]+margin+Math.floor(hash2(x*13+7,y*29+type)*roomY);
    c.drawImage(atlas,sx,sy,sample,sample,x*TILE,y*TILE,TILE+.6,TILE+.6);
  }
  c.globalAlpha=1;c.fillStyle='rgba(12,29,20,.055)';c.fillRect(0,0,cache.width,cache.height);medievalTerrainCache=cache;medievalTerrainSource=terrainCanvas;medievalTerrainMap=terrain;return cache;
}

function generatedArtSprite(method,type){
  return typeof GeneratedArt!=='undefined'&&typeof GeneratedArt[method]==='function'?GeneratedArt[method](type):null;
}
function drawGeneratedEffect(type,x,y,width,height=width,alpha=1,rotation=0){
  const sprite=generatedArtSprite('getEffectSprite',type);if(!sprite)return false;
  ctx.save();ctx.translate(x,y);ctx.rotate(rotation);ctx.globalAlpha*=alpha;ctx.globalCompositeOperation='screen';ctx.imageSmoothingEnabled=true;ctx.imageSmoothingQuality='high';
  ctx.drawImage(sprite.image,sprite.sx,sprite.sy,sprite.sw,sprite.sh,-width/2,-height/2,width,height);ctx.restore();return true;
}

function drawHealth(e,yOverride){
  if(e.hp>=e.maxHp&&!e.selected)return;
  const building=e.kind==='building',w=building?64:Math.max(38,e.radius*2.3),h=building?6:5,y=yOverride??(building?-36:-30),ratio=clamp(e.hp/e.maxHp,0,1);
  ctx.fillStyle='rgba(2,7,10,.9)';roundedRect(ctx,-w/2,y,w,h,2);ctx.fill();
  ctx.fillStyle=ratio>.5?'#72e092':ratio>.25?'#efc45e':'#ff6860';roundedRect(ctx,-w/2+1,y+1,(w-2)*ratio,h-2,1);ctx.fill();
  ctx.strokeStyle='rgba(255,244,214,.4)';ctx.lineWidth=1;roundedRect(ctx,-w/2+.5,y+.5,w-1,h-1,1.5);ctx.stroke();
}
function drawEntityHealth(e,alpha){
  if(e.hp>=e.maxHp&&!e.selected)return;const wp=entityWorldPosition(e,alpha),p=worldToScreen(wp.x,wp.y),z=renderZoom();
  const buildingArt=e.kind==='building'?generatedArtSprite('getBuildingSprite',e.type):null,offset=e.kind==='building'?Math.max(30,buildingMetrics(e).dep*z+15,(buildingArt?.height||0)*z*.52+8):UNIT[e.type]?.role==='cavalry'||isElephantUnit(e)?34:28;
  ctx.save();ctx.translate(p.x,p.y-offset);drawHealth(e,0);ctx.restore();
}

function drawTargetWorkFeedback(type,work,id){
  if(!work?.count)return;const beat=workBeat(id),strength=Math.min(1.7,.7+work.count*.18),palette=type==='wood'?['#d9bd7b','#86b765']:type==='gold'?['#fff0a0','#e3b341']:type==='stone'?['#edf2ee','#9ca8aa']:type==='build'?['#ffe1a0','#d19755']:['#ffc59b','#84b85d'];
  ctx.save();ctx.globalCompositeOperation='screen';ctx.globalAlpha=.22+.18*Math.abs(Math.sin(game.time*4+id));ctx.strokeStyle=palette[1];ctx.lineWidth=1.4;ctx.beginPath();ctx.arc(0,2,24+Math.sin(game.time*3+id)*2,-.2,Math.PI*.95);ctx.stroke();
  if(beat.impact){for(let i=0;i<5;i++){const a=-2.65+i*.48+(id%5)*.06,r=(9+i*2.8)*strength,x=Math.cos(a)*r,y=-5+Math.sin(a)*r*.7;ctx.fillStyle=i%2?palette[0]:palette[1];ctx.beginPath();ctx.arc(x,y,1.6+(i%3)*.55,0,TAU);ctx.fill()}ctx.strokeStyle=palette[0];ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(-5,-13);ctx.lineTo(-11,-21);ctx.moveTo(1,-15);ctx.lineTo(2,-25);ctx.moveTo(7,-12);ctx.lineTo(14,-19);ctx.stroke()}
  ctx.restore();
}

function drawNode(n){
  const p=worldToScreen(n.x,n.y);if(!onScreen(p,70))return;const z=Math.max(renderZoom(),.76),t=game.time,phase=t*.9+n.wiggle,work=targetWork(n),beat=workBeat(n.id||n.wiggle);
  ctx.save();ctx.translate(p.x,p.y);ctx.scale(z,z);
  const artKey=n.type==='wood'?(hash2(n.id||n.x,n.wiggle||n.y)>.48?'pine':'oak'):n.type,sprite=generatedArtSprite('getEnvironmentSprite',artKey);
  if(sprite){
    const wind=n.type==='wood'?windAt(n.x,n.y,t):{x:0,y:0},motion=motionAmount(),breathe=1+(n.type==='wood'?Math.sin(phase*.72)*.018*motion:Math.sin(phase)*.008*motion);
    ctx.rotate(wind.x*.006+(work?.count?(beat.swing-.5)*.055*motion:0));ctx.scale(breathe+((work?.count&&beat.impact)?0.025:0),1/breathe);ctx.imageSmoothingEnabled=true;ctx.imageSmoothingQuality='high';
    ctx.drawImage(sprite.image,sprite.sx,sprite.sy,sprite.sw,sprite.sh,-sprite.width/2,-sprite.height*.68,sprite.width,sprite.height);
    if(n.type==='gold'){const glint=Math.max(0,Math.sin(t*2.1+n.wiggle));if(glint>.55)drawGeneratedEffect('embers',4,-10,36,36,(glint-.55)*.62,.2)}
    drawTargetWorkFeedback(n.type,work,n.id||n.wiggle);
    ctx.restore();return;
  }
  if(n.type==='wood'){
    const wind=windAt(n.x,n.y,t),sway=Math.sin(phase)*1.25+wind.x*.55;ctx.rotate(sway*.012);
    ctx.fillStyle='#1b2821';ctx.beginPath();ctx.arc(3,4,18,0,TAU);ctx.fill();
    const crowns=[[-7,-7,12,'#214d35'],[8,-5,13,'#2d6240'],[-2,7,14,'#39764a'],[0,-13,11,'#4b8755']];
    for(const[x,y,r,c]of crowns){ctx.fillStyle=c;ctx.beginPath();ctx.arc(x,y,r,0,TAU);ctx.fill()}
    ctx.strokeStyle='rgba(213,242,172,.42)';ctx.lineWidth=1.3;ctx.beginPath();ctx.arc(-3,-13,7,3.4,5.7);ctx.stroke();
    ctx.fillStyle='rgba(218,242,161,.4)';for(let i=0;i<4;i++){const a=n.wiggle+i*1.73+Math.sin(t*.38+i),r=8+i*2.2;ctx.beginPath();ctx.ellipse(Math.cos(a)*r+wind.x*.25,Math.sin(a)*r-4,2.2,1.2,a,0,TAU);ctx.fill()}
    ctx.fillStyle='#66482d';ctx.beginPath();ctx.arc(0,1,4,0,TAU);ctx.fill();
  }else if(n.type==='gold'||n.type==='stone'){
    const colors=n.type==='gold'?['#6d4a22','#b48237','#edc65c','#fff0a0']:['#48535a','#758187','#acb7b8','#e3e9e5'];
    ctx.fillStyle=colors[0];polygon(ctx,[[-19,8],[-15,-8],[-3,-18],[15,-12],[20,3],[9,16],[-7,15]]);ctx.fill();
    ctx.fillStyle=colors[1];polygon(ctx,[[-15,-8],[-3,-18],[0,5],[-7,15],[-19,8]]);ctx.fill();
    ctx.fillStyle=colors[2];polygon(ctx,[[-3,-18],[15,-12],[20,3],[0,5]]);ctx.fill();
    ctx.strokeStyle=colors[3];ctx.lineWidth=1.2;ctx.beginPath();ctx.moveTo(-8,-8);ctx.lineTo(-2,-13);ctx.lineTo(8,-9);ctx.stroke();
  }else{
    const wind=windAt(n.x,n.y,t);ctx.rotate(wind.x*.009);ctx.fillStyle='#355f3b';ctx.beginPath();ctx.arc(0,1,13,0,TAU);ctx.fill();
    for(let i=0;i<10;i++){const a=i/10*TAU,r=7+(i%3)*3;ctx.fillStyle=i%2?'#8f3041':'#ca5260';ctx.beginPath();ctx.arc(Math.cos(a)*r+wind.x*.2,Math.sin(a)*r,4.2,0,TAU);ctx.fill()}
    ctx.strokeStyle='rgba(255,219,188,.52)';ctx.lineWidth=1;ctx.beginPath();ctx.arc(-4,-6,3,Math.PI,TAU);ctx.stroke();
  }
  drawTargetWorkFeedback(n.type,work,n.id||n.wiggle);
  ctx.restore();
}

function drawSiteActivity(s,y=4){
  const state=siteProgressState(s);if(!state)return;const t=game.time,amp=motionAmount();ctx.save();ctx.globalCompositeOperation='screen';
  if(state.kind==='contested'){
    const pulse=.45+.35*Math.sin(t*7+s.id);ctx.lineWidth=4;ctx.setLineDash([7,5]);ctx.lineDashOffset=-t*18;ctx.strokeStyle=`rgba(255,104,91,${.55+pulse*.25})`;ctx.beginPath();ctx.arc(0,y,51,0,TAU);ctx.stroke();ctx.setLineDash([]);ctx.strokeStyle='rgba(255,231,176,.78)';ctx.lineWidth=2.5;ctx.beginPath();ctx.moveTo(-14,-10);ctx.lineTo(14,18);ctx.moveTo(14,-10);ctx.lineTo(-14,18);ctx.stroke();
  }else{
    for(let i=0;i<7;i++){const a=-Math.PI/2+i/7*TAU+t*.72*amp,r=46-i%2*3;ctx.fillStyle=state.color;ctx.globalAlpha=.35+(i%3)*.18;ctx.beginPath();ctx.arc(Math.cos(a)*r,y+Math.sin(a)*r,2+(i%2),0,TAU);ctx.fill()}
  }
  ctx.restore();
}

function drawSite(s){
  const p=worldToScreen(s.x,s.y);if(!onScreen(p,100))return;const z=renderZoom(),pulse=1+Math.sin(game.time*2+s.id)*.025;
  ctx.save();ctx.translate(p.x,p.y);ctx.scale(z*pulse,z*pulse);
  const sprite=generatedArtSprite('getEnvironmentSprite','site');
  if(sprite){
    ctx.fillStyle='rgba(7,12,15,.62)';ctx.beginPath();ctx.ellipse(3,8,43,34,0,0,TAU);ctx.fill();ctx.strokeStyle=factionColor(s.owner);ctx.lineWidth=4;ctx.beginPath();ctx.ellipse(0,5,39,31,0,0,TAU);ctx.stroke();
    ctx.save();ctx.rotate(Math.sin(game.time*1.7+s.id)*.006*motionAmount());ctx.imageSmoothingEnabled=true;ctx.imageSmoothingQuality='high';ctx.drawImage(sprite.image,sprite.sx,sprite.sy,sprite.sw,sprite.sh,-sprite.width/2,-sprite.height*.68,sprite.width,sprite.height);ctx.restore();
    ctx.fillStyle=factionColor(s.owner,'#c3aa66');ctx.strokeStyle='rgba(255,242,201,.85)';ctx.lineWidth=1.4;ctx.beginPath();ctx.arc(23,19,7,0,TAU);ctx.fill();ctx.stroke();
    if(s.captureBy>=0&&s.progress<6){ctx.strokeStyle=factionColor(s.captureBy);ctx.lineWidth=5;ctx.beginPath();ctx.arc(0,5,48,-Math.PI/2,-Math.PI/2+TAU*s.progress/6);ctx.stroke()}drawSiteActivity(s,5);
    ctx.restore();return;
  }
  ctx.fillStyle='rgba(7,12,15,.65)';ctx.beginPath();ctx.arc(3,5,46,0,TAU);ctx.fill();
  ctx.fillStyle='#676f6e';ctx.beginPath();ctx.arc(0,0,42,0,TAU);ctx.fill();ctx.strokeStyle='#9ea69e';ctx.lineWidth=2;ctx.stroke();
  ctx.strokeStyle=factionColor(s.owner);ctx.lineWidth=4;ctx.beginPath();ctx.arc(0,0,35,0,TAU);ctx.stroke();
  for(let i=0;i<8;i++){const a=i/8*TAU;ctx.save();ctx.translate(Math.cos(a)*34,Math.sin(a)*34);ctx.rotate(a);ctx.fillStyle='#3d4545';ctx.fillRect(-4,-7,8,14);ctx.restore()}
  ctx.fillStyle='#27231c';ctx.beginPath();ctx.arc(0,0,7,0,TAU);ctx.fill();
  const wave=Math.sin(game.time*3+s.id*.61)*3;ctx.save();ctx.rotate(-.45);ctx.fillStyle='#3a2c20';ctx.fillRect(-2,-4,4,35);ctx.fillStyle=factionColor(s.owner,'#c3aa66');polygon(ctx,[[2,-2],[23+wave,-8],[22+wave*.6,5],[2,9]]);ctx.fill();ctx.restore();
  if(s.captureBy>=0&&s.progress<6){ctx.strokeStyle=factionColor(s.captureBy);ctx.lineWidth=5;ctx.beginPath();ctx.arc(0,0,49,-Math.PI/2,-Math.PI/2+TAU*s.progress/6);ctx.stroke()}drawSiteActivity(s,0);
  ctx.restore();
}

function buildingPalette(b){
  const civ=CIVS[b.civ]||Object.values(CIVS)[0],styles={
    britons:{roof:'#3f536b',wall:'#aaa89c',wood:'#59402e'},byzantines:{roof:'#67508d',wall:'#c8b58b',wood:'#5d4230'},
    celts:{roof:'#3e6a4a',wall:'#999c8a',wood:'#563d2c'},chinese:{roof:'#315d54',wall:'#b9a77f',wood:'#673f30'},
    franks:{roof:'#3d6082',wall:'#b9b2a2',wood:'#5d4030'},goths:{roof:'#4e5c61',wall:'#989b95',wood:'#4f392c'},
    japanese:{roof:'#3b5268',wall:'#aaa59b',wood:'#52372c'},mongols:{roof:'#65727b',wall:'#a89575',wood:'#5b402c'},
    persians:{roof:'#87434b',wall:'#d0b27e',wood:'#60422e'},saracens:{roof:'#278f87',wall:'#d0b580',wood:'#67482f'},
    teutons:{roof:'#495c6b',wall:'#b8b9b2',wood:'#51392e'},turks:{roof:'#327b78',wall:'#c5aa7d',wood:'#60422e'},
    vikings:{roof:'#4a6173',wall:'#9fa7a5',wood:'#5b4130'}
  },s=styles[b.civ]||{roof:shade(civ.color,.72),wall:'#b5aa91',wood:'#5c402e'};
  return{team:teamColor(b),accent:civ.accent||civ.color,roof:s.roof,roofDark:shade(s.roof,.68),roofLight:shade(s.roof,1.18),wall:s.wall,wallLight:shade(s.wall,1.12),wallDark:shade(s.wall,.67),wood:s.wood,dark:'#252b2d'};
}

function buildingMetrics(b){
  const d=BUILD[b.type];let baseScale=.78,depScale=.76;
  if(b.type==='wall'){baseScale=1.02;depScale=.36}else if(b.type==='tower'){baseScale=.72;depScale=.72}else if(b.type==='castle'){baseScale=.9;depScale=.88}else if(b.type==='wonder')baseScale=.9;else if(b.type==='farm')depScale=.68;
  const base=d.size*baseScale,dep=d.size*depScale;
  return{w:base,dep,h:dep};
}

/* Compatibility helpers retained for input code and older saves; all shapes
 * are flat roof/footprint shapes now, never vertical prisms. */
function drawPrism(w,d,h,p,baseY=0){
  ctx.fillStyle=p.wallDark||'#51605f';roundedRect(ctx,-w,-d+baseY,w*2,d*2,Math.min(9,d*.3));ctx.fill();
  ctx.fillStyle=p.wallLight||'#91aaa1';roundedRect(ctx,-w+3,-d+3+baseY,w*2-6,d*2-6,Math.min(7,d*.25));ctx.fill();
}
function drawCivRoof(civ,w,d,h,p,baseY=0){
  if(civ==='chinese'||civ==='japanese'){
    ctx.fillStyle=p.roofDark;polygon(ctx,[[-w,-d*.82+baseY],[-w*.82,d*.82+baseY],[0,d+baseY],[w*.82,d*.82+baseY],[w,-d*.82+baseY],[0,-d+baseY]]);ctx.fill();
    ctx.fillStyle=p.roof;polygon(ctx,[[-w*.93,-d*.72+baseY],[0,-d+3+baseY],[0,d*.9+baseY],[-w*.78,d*.72+baseY]]);ctx.fill();
    ctx.strokeStyle=p.roofLight;ctx.lineWidth=1.5;ctx.beginPath();ctx.moveTo(0,-d+3+baseY);ctx.lineTo(0,d*.88+baseY);ctx.moveTo(-w*.88,0+baseY);ctx.lineTo(w*.88,0+baseY);ctx.stroke();
  }else if(['byzantines','persians','saracens','turks'].includes(civ)){
    ctx.fillStyle=p.roofDark;ctx.beginPath();ctx.ellipse(0,baseY,w,d,0,0,TAU);ctx.fill();ctx.fillStyle=p.roof;ctx.beginPath();ctx.ellipse(-w*.1,-d*.1+baseY,w*.82,d*.82,0,0,TAU);ctx.fill();
    ctx.strokeStyle=p.roofLight;ctx.lineWidth=1.3;for(let i=0;i<8;i++){const a=i/8*TAU;ctx.beginPath();ctx.moveTo(0,baseY);ctx.lineTo(Math.cos(a)*w*.88,baseY+Math.sin(a)*d*.88);ctx.stroke()}ctx.fillStyle=p.accent;ctx.beginPath();ctx.arc(0,baseY,4,0,TAU);ctx.fill();
  }else if(civ==='mongols'){
    ctx.fillStyle=p.roofDark;ctx.beginPath();ctx.arc(0,baseY,Math.min(w,d),0,TAU);ctx.fill();ctx.strokeStyle=p.roofLight;ctx.lineWidth=2;for(let i=0;i<6;i++){const a=i/6*TAU;ctx.beginPath();ctx.moveTo(0,baseY);ctx.lineTo(Math.cos(a)*w*.88,baseY+Math.sin(a)*d*.88);ctx.stroke()}ctx.fillStyle=p.team;ctx.beginPath();ctx.arc(0,baseY,5,0,TAU);ctx.fill();
  }else{
    ctx.fillStyle=p.roofDark;roundedRect(ctx,-w,-d+baseY,w*2,d*2,Math.min(11,d*.38));ctx.fill();
    ctx.fillStyle=p.roof;polygon(ctx,[[-w+4,-d+4+baseY],[w-4,-d+4+baseY],[w*.7,d-4+baseY],[-w*.7,d-4+baseY]]);ctx.fill();
    ctx.strokeStyle=p.roofLight;ctx.lineWidth=1.4;ctx.beginPath();ctx.moveTo(0,-d+4+baseY);ctx.lineTo(0,d-4+baseY);ctx.stroke();
    if(civ==='vikings'||civ==='celts'){ctx.fillStyle=p.accent;for(const x of[-w*.76,w*.76]){ctx.beginPath();ctx.arc(x,baseY,3,0,TAU);ctx.fill()}}
  }
}
function drawScaffold(w,h,progress){
  ctx.save();ctx.globalAlpha=.75;ctx.strokeStyle='#80623e';ctx.lineWidth=2;
  const d=Math.max(12,h*.55);ctx.strokeRect(-w,-d,w*2,d*2);for(let x=-w+10;x<w;x+=14){ctx.beginPath();ctx.moveTo(x,-d);ctx.lineTo(x+8,d);ctx.stroke()}
  ctx.restore();
}

function drawFarm(b,p,m,prog){
  ctx.fillStyle=prog<1?'#62543c':'#745d30';roundedRect(ctx,-m.w,-m.dep,m.w*2,m.dep*2,6);ctx.fill();
  ctx.save();roundedRect(ctx,-m.w+3,-m.dep+3,m.w*2-6,m.dep*2-6,4);ctx.clip();
  ctx.strokeStyle=prog<1?'#9a8055':'#d2af53';ctx.lineWidth=2;for(let y=-m.dep;y<=m.dep;y+=9){ctx.beginPath();ctx.moveTo(-m.w,y);ctx.quadraticCurveTo(0,y+4,m.w,y);ctx.stroke()}
  if(prog>.55){ctx.strokeStyle='#799245';ctx.lineWidth=2;const sway=(reducedMotion?.35:1)*Math.sin(game.time*2.1+b.id*.53)*2;for(let x=-m.w*.75;x<m.w*.8;x+=11){ctx.beginPath();ctx.moveTo(x,5);ctx.lineTo(x+sway,-8);ctx.stroke()}}
  ctx.restore();
  ctx.fillStyle=p.team;ctx.beginPath();ctx.arc(-m.w+9,-m.dep+9,5,0,TAU);ctx.fill();
}

function drawCastle(b,p,m){
  const w=m.w,dep=m.dep;ctx.fillStyle=p.wallDark;roundedRect(ctx,-w,-dep,w*2,dep*2,10);ctx.fill();
  ctx.fillStyle=p.wall;roundedRect(ctx,-w+7,-dep+7,w*2-14,dep*2-14,7);ctx.fill();
  ctx.fillStyle='#434949';roundedRect(ctx,-w*.48,-dep*.38,w*.96,dep*.76,5);ctx.fill();
  ctx.fillStyle=p.roofDark;roundedRect(ctx,-w*.34,-dep*.28,w*.68,dep*.56,4);ctx.fill();
  for(const sx of[-1,1])for(const sy of[-1,1]){
    const x=sx*w*.75,y=sy*dep*.72;ctx.fillStyle=p.wallDark;ctx.beginPath();ctx.arc(x,y,16,0,TAU);ctx.fill();ctx.fillStyle=p.wallLight;ctx.beginPath();ctx.arc(x,y,12,0,TAU);ctx.fill();
    ctx.fillStyle=p.roof;for(let i=0;i<6;i++){const a=i/6*TAU;ctx.fillRect(x+Math.cos(a)*11-2,y+Math.sin(a)*11-2,4,4)}
  }
  ctx.strokeStyle=p.wallLight;ctx.lineWidth=3;ctx.setLineDash([7,5]);roundedRect(ctx,-w+3,-dep+3,w*2-6,dep*2-6,9);ctx.stroke();ctx.setLineDash([]);
  const flutter=(reducedMotion?.3:1)*Math.sin(game.time*3+b.id)*3;ctx.fillStyle=p.team;ctx.save();ctx.translate(0,-dep*.12);ctx.rotate(-.25);ctx.fillRect(-2,-3,4,35);polygon(ctx,[[2,-2],[25+flutter,-7],[23+flutter*.7,7],[2,10]]);ctx.fill();ctx.restore();
}

function drawBlacksmith(b,p,m){
  drawPrism(m.w,m.dep,0,p);drawCivRoof(b.civ,m.w*.72,m.dep*.7,0,p);
  const glow=.72+(reducedMotion?0:Math.sin(game.time*5+b.id)*.22);ctx.save();ctx.shadowColor='#ff9b45';ctx.shadowBlur=13;ctx.fillStyle=`rgba(255,133,54,${glow})`;ctx.beginPath();ctx.arc(-m.w*.42,m.dep*.34,7,0,TAU);ctx.fill();ctx.restore();
  ctx.fillStyle='#333a3a';polygon(ctx,[[4,-6],[18,-6],[23,-1],[16,3],[15,11],[7,11],[7,3],[1,0]]);ctx.fill();
  ctx.save();ctx.translate(14,-2);ctx.rotate(-.8+(reducedMotion?0:Math.sin(game.time*4.2+b.id)*.28));ctx.fillStyle='#68482e';ctx.fillRect(-2,-2,4,19);ctx.fillStyle='#a9b1b0';roundedRect(ctx,-7,-6,14,7,2);ctx.fill();ctx.restore();
  ctx.fillStyle=p.team;ctx.fillRect(-m.w+5,-m.dep+5,9,9);
}

function drawMill(b,p,m){
  ctx.fillStyle=p.wallDark;ctx.beginPath();ctx.arc(0,0,m.w*.82,0,TAU);ctx.fill();ctx.fillStyle=p.wallLight;ctx.beginPath();ctx.arc(0,0,m.w*.7,0,TAU);ctx.fill();
  ctx.fillStyle=p.roof;ctx.beginPath();ctx.arc(0,0,m.w*.5,0,TAU);ctx.fill();ctx.strokeStyle=p.roofLight;ctx.lineWidth=1.5;ctx.stroke();
  const turn=game.time*(reducedMotion?.08:.34)+b.id*.2;ctx.save();ctx.translate(0,0);ctx.rotate(turn);ctx.strokeStyle=p.wood;ctx.lineWidth=4;for(let i=0;i<4;i++){ctx.save();ctx.rotate(i*Math.PI/2);ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(0,-m.w*.82);ctx.stroke();ctx.fillStyle='rgba(231,218,176,.75)';polygon(ctx,[[-3,-m.w*.18],[-9,-m.w*.7],[4,-m.w*.78],[5,-m.w*.2]]);ctx.fill();ctx.restore()}ctx.fillStyle=p.team;ctx.beginPath();ctx.arc(0,0,6,0,TAU);ctx.fill();ctx.restore();
  ctx.fillStyle='#b59151';for(const a of[.4,2.2,4.3]){ctx.beginPath();ctx.arc(Math.cos(a)*m.w*.68,Math.sin(a)*m.dep*.68,5,0,TAU);ctx.fill()}
}

function drawLumber(b,p,m){
  ctx.fillStyle='rgba(76,58,37,.86)';roundedRect(ctx,-m.w,-m.dep,m.w*2,m.dep*2,6);ctx.fill();
  for(const side of[-1,1])for(let i=0;i<4;i++){
    const y=-m.dep*.62+i*10;ctx.fillStyle=i%2?'#765132':'#8b6039';roundedRect(ctx,side*m.w*.35-15,y,30,7,3);ctx.fill();ctx.fillStyle='#b58a55';ctx.beginPath();ctx.arc(side*m.w*.35+15,y+3.5,3.5,0,TAU);ctx.fill();
  }
  ctx.fillStyle=p.roofDark;polygon(ctx,[[-m.w*.34,-m.dep*.82],[m.w*.34,-m.dep*.82],[m.w*.48,m.dep*.16],[-m.w*.48,m.dep*.16]]);ctx.fill();ctx.fillStyle=p.roof;polygon(ctx,[[-m.w*.29,-m.dep*.73],[0,-m.dep*.82],[0,m.dep*.06],[-m.w*.4,m.dep*.08]]);ctx.fill();
  const saw=(reducedMotion?0:Math.sin(game.time*3.7+b.id)*.08);ctx.save();ctx.translate(0,m.dep*.5);ctx.rotate(saw);ctx.fillStyle='#aeb8b8';ctx.beginPath();for(let i=0;i<20;i++){const a=i/20*TAU,r=i%2?11:14;ctx.lineTo(Math.cos(a)*r,Math.sin(a)*r)}ctx.closePath();ctx.fill();ctx.fillStyle='#4d3b2a';ctx.beginPath();ctx.arc(0,0,4,0,TAU);ctx.fill();ctx.restore();
  ctx.fillStyle=p.team;ctx.fillRect(-m.w+5,-m.dep+5,9,9);
}

function drawBuildingDetails(b,p,m){
  const flutter=(reducedMotion?.35:1)*Math.sin(game.time*2.8+b.id*.47)*2.5;
  ctx.fillStyle=p.team;ctx.save();ctx.translate(-m.w*.55,-m.dep*.45);ctx.rotate(-.35);ctx.fillRect(-1,-2,3,20);polygon(ctx,[[2,-1],[17+flutter,-5],[16+flutter*.7,5],[2,8]]);ctx.fill();ctx.restore();
  if(b.type==='barracks'){
    ctx.strokeStyle='#ead7a1';ctx.lineWidth=2.4;ctx.beginPath();ctx.moveTo(-14,-13);ctx.lineTo(14,13);ctx.moveTo(14,-13);ctx.lineTo(-14,13);ctx.stroke();
  }else if(b.type==='range'){
    ctx.strokeStyle='#ead7a1';ctx.lineWidth=2;for(const r of[7,13]){ctx.beginPath();ctx.arc(0,0,r,0,TAU);ctx.stroke()}ctx.beginPath();ctx.moveTo(-16,0);ctx.lineTo(16,0);ctx.stroke();
  }else if(b.type==='stable'){
    ctx.strokeStyle='#e4c993';ctx.lineWidth=3;ctx.beginPath();ctx.arc(0,1,13,.15,Math.PI-.15);ctx.stroke();
  }else if(b.type==='workshop'){
    const turn=(reducedMotion?.2:1)*game.time*.38;ctx.save();ctx.translate(m.w*.35,0);ctx.rotate(turn);ctx.strokeStyle='#282d2c';ctx.lineWidth=4;ctx.beginPath();ctx.arc(0,0,11,0,TAU);ctx.stroke();for(let i=0;i<6;i++){const a=i/6*TAU;ctx.beginPath();ctx.moveTo(Math.cos(a)*9,Math.sin(a)*9);ctx.lineTo(Math.cos(a)*16,Math.sin(a)*16);ctx.stroke()}ctx.restore();
  }
}

function drawBuildingActivity(b,p,m,prog){
  const t=game.time,amp=motionAmount(),states=buildingProgressStates(b),busy=states.length>0,ageState=states.find(s=>s.kind==='age'),wonderState=states.find(s=>s.kind==='wonder'),work=targetWork(b);
  ctx.save();ctx.strokeStyle='rgba(255,246,213,.18)';ctx.lineWidth=1.2;
  if(['tower','wonder','mill'].includes(b.type)){ctx.beginPath();ctx.ellipse(0,0,m.w*.9,m.dep*.9,0,Math.PI*1.05,Math.PI*1.83);ctx.stroke()}else{roundedRect(ctx,-m.w+2,-m.dep+2,m.w*2-4,m.dep*2-4,7);ctx.stroke()}
  if(prog<1){
    const crane=Math.sin(t*1.15+b.id)*.22*amp;ctx.strokeStyle='#6f5033';ctx.lineWidth=3;ctx.beginPath();ctx.moveTo(-m.w*.68,m.dep*.65);ctx.lineTo(-m.w*.68,-m.dep*.75);ctx.lineTo(m.w*.2,-m.dep*.75);ctx.stroke();ctx.save();ctx.translate(m.w*.2,-m.dep*.75);ctx.rotate(crane);ctx.strokeStyle='#c1a26d';ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(0,m.dep*.8);ctx.stroke();ctx.fillStyle='#90704a';ctx.fillRect(-5,m.dep*.72,10,8);ctx.restore();
    for(let i=0;i<5;i++){const cycle=(t*.32+hash2(b.id*7+i,17)*1.7)%1,x=-m.w*.7+hash2(b.id+i,31)*m.w*1.4,y=-m.dep*.4-cycle*m.dep*.75;ctx.fillStyle=`rgba(223,191,133,${(1-cycle)*.2})`;ctx.beginPath();ctx.arc(x,y,2+cycle*4,0,TAU);ctx.fill()}
    ctx.fillStyle='#8b633b';for(const x of[-m.w*.55,m.w*.43])roundedRect(ctx,x-9,m.dep*.48,18,7,3),ctx.fill();
  }else{
    if(busy){
      const pulse=.45+Math.sin(t*4+b.id)*.22,primary=states[0];ctx.save();ctx.globalCompositeOperation='screen';ctx.fillStyle=primary?.color||`rgba(255,190,82,${pulse*.35})`;ctx.globalAlpha=.18+pulse*.25;ctx.beginPath();ctx.arc(0,m.dep*.52,10+pulse*5,0,TAU);ctx.fill();ctx.restore();
      for(let i=0;i<5;i++){const a=t*(reducedMotion?.35:1.45)+i*TAU/5+b.id,r=i%2?m.w*.38:m.w*.3;ctx.fillStyle=i===0?p.team:(primary?.color||'#e7c77d');ctx.globalAlpha=.48+(i%2)*.25;ctx.beginPath();ctx.arc(Math.cos(a)*r,Math.sin(a)*m.dep*.34,2.2+(i%2),0,TAU);ctx.fill()}ctx.globalAlpha=1;
    }
    if(['house','town','castle'].includes(b.type)){
      const glow=.45+(reducedMotion?0:Math.sin(t*3.2+b.id)*.18);ctx.save();ctx.globalCompositeOperation='screen';ctx.fillStyle=`rgba(255,188,78,${glow})`;for(const x of[-m.w*.34,m.w*.34]){ctx.beginPath();ctx.arc(x,m.dep*.22,3.2,0,TAU);ctx.fill()}ctx.restore();
    }
    if(['blacksmith','workshop'].includes(b.type))for(let i=0;i<4;i++){
      const a=t*4.5+b.id+i*1.9,r=7+(i%2)*5;ctx.fillStyle=`rgba(255,${145+i*17},73,${.2+Math.max(0,Math.sin(a))*.45})`;ctx.beginPath();ctx.arc(-m.w*.34+Math.cos(a)*r,m.dep*.28+Math.sin(a)*r*.5,1.5,0,TAU);ctx.fill();
    }
    if(['blacksmith','workshop'].includes(b.type)){
      const camp=generatedArtSprite('getEnvironmentSprite','campfire');if(camp){const h=42,w=h*camp.sw/camp.sh,pulse=1+(reducedMotion?0:Math.sin(t*5+b.id)*.035);ctx.save();ctx.translate(-m.w*.3,m.dep*.32);ctx.scale(pulse,1/pulse);ctx.globalAlpha=.72;ctx.drawImage(camp.image,camp.sx,camp.sy,camp.sw,camp.sh,-w/2,-h*.7,w,h);ctx.restore()}
    }
    if(b.type==='stable'){const nod=Math.sin(t*2.4+b.id)*2*amp;ctx.fillStyle='#4b3628';ctx.beginPath();ctx.ellipse(m.w*.34,-m.dep*.1+nod,6,10,.25,0,TAU);ctx.fill();ctx.fillStyle='#1d2525';ctx.beginPath();ctx.arc(m.w*.36,-m.dep*.17+nod,1,0,TAU);ctx.fill()}
    if(ageState){ctx.save();ctx.globalCompositeOperation='screen';ctx.strokeStyle=ageState.color;ctx.lineWidth=2.4;for(let i=0;i<3;i++){const r=m.w*(.42+i*.16),a=t*(.55+i*.18)*amp+i;ctx.globalAlpha=.28+i*.12;ctx.beginPath();ctx.arc(0,0,r,a,a+Math.PI*1.15);ctx.stroke()}for(let i=0;i<6;i++){const a=i/6*TAU+t*.65*amp,r=m.w*.65;ctx.globalAlpha=.45;ctx.fillStyle=ageState.color;ctx.beginPath();ctx.arc(Math.cos(a)*r,Math.sin(a)*m.dep*.62,2.4,0,TAU);ctx.fill()}ctx.restore()}
    if(wonderState){ctx.save();ctx.globalCompositeOperation='screen';for(let i=0;i<7;i++){const cycle=(t*.22+i*.17+b.id*.03)%1,x=(hash2(b.id+i,71)-.5)*m.w*1.15,y=m.dep*.55-cycle*m.dep*1.65;ctx.globalAlpha=(1-cycle)*.42;ctx.fillStyle=wonderState.color;ctx.beginPath();ctx.arc(x,y,2+cycle*3,0,TAU);ctx.fill()}ctx.restore()}
  }
  if(work?.count)drawTargetWorkFeedback(work.resource||'build',work,b.id);
  if(b.activityFlash>0){const q=1-clamp(b.activityFlash,0,1),r=m.w*(.55+q*.65);ctx.save();ctx.globalCompositeOperation='screen';ctx.globalAlpha=clamp(b.activityFlash,0,1)*.8;ctx.strokeStyle='#fff0ac';ctx.lineWidth=3*(1-q)+1;ctx.beginPath();ctx.ellipse(0,0,r,r*m.dep/m.w,0,0,TAU);ctx.stroke();ctx.restore()}
  ctx.restore();
}

function drawGeneratedBuildingSprite(b,p,m,prog){
  const sprite=generatedArtSprite('getBuildingSprite',b.type);if(!sprite)return false;
  const busy=buildingProgressStates(b).length>0,breathe=1+(reducedMotion?0:Math.sin(game.time*(busy?1.28:.72)+b.id*.39)*(busy ? 0.007 : 0.003)),w=sprite.width,h=sprite.height;
  ctx.save();ctx.scale(breathe,1/breathe);ctx.imageSmoothingEnabled=true;ctx.imageSmoothingQuality='high';
  if(prog<1){
    ctx.globalAlpha*=.16+.12*prog;ctx.drawImage(sprite.image,sprite.sx,sprite.sy,sprite.sw,sprite.sh,-w/2,-h/2,w,h);
    ctx.globalAlpha=clamp((.5+.45*prog)*(.48+.52*prog),0,1);ctx.beginPath();ctx.rect(-w/2,h/2-h*prog,w,h*prog);ctx.clip();ctx.drawImage(sprite.image,sprite.sx,sprite.sy,sprite.sw,sprite.sh,-w/2,-h/2,w,h);
  }else ctx.drawImage(sprite.image,sprite.sx,sprite.sy,sprite.sw,sprite.sh,-w/2,-h/2,w,h);
  ctx.restore();
  const flutter=(reducedMotion?.3:1)*Math.sin(game.time*3+b.id)*2;ctx.save();ctx.translate(-m.w*.58,m.dep*.5);ctx.fillStyle='rgba(5,10,12,.82)';ctx.strokeStyle='rgba(255,242,202,.76)';ctx.lineWidth=1.2;ctx.beginPath();ctx.arc(0,0,7,0,TAU);ctx.fill();ctx.stroke();ctx.fillStyle=p.team;polygon(ctx,[[-4,-4],[5,-3+flutter*.08],[4,4+flutter*.08],[-4,4]]);ctx.fill();ctx.restore();return true;
}

function drawGeneratedConstructionSprite(b,m,prog){
  const sprite=generatedArtSprite('getEnvironmentSprite','construction');if(!sprite)return false;
  const w=Math.max(m.w*1.55,Math.min(118,(BUILD[b.type]?.size||50)*1.7)),h=w*sprite.sh/sprite.sw,sway=Math.sin(game.time*1.2+b.id)*.008*motionAmount();
  ctx.save();ctx.rotate(sway);ctx.globalAlpha*=.72+.18*prog;ctx.imageSmoothingEnabled=true;ctx.imageSmoothingQuality='high';ctx.drawImage(sprite.image,sprite.sx,sprite.sy,sprite.sw,sprite.sh,-w/2,-h*.6,w,h);ctx.restore();return true;
}

function drawBuilding(b){
  const screen=worldToScreen(b.x,b.y);if(!onScreen(screen,180))return;const p=buildingPalette(b),m=buildingMetrics(b),prog=clamp(b.construction??1,0,1),z=renderZoom();
  ctx.save();ctx.translate(screen.x,screen.y);ctx.scale(z,z);ctx.globalAlpha=.48+.52*prog;
  if(prog<1){ctx.fillStyle='rgba(92,66,38,.34)';ctx.beginPath();ctx.ellipse(0,m.dep*.2,m.w*1.02,m.dep*.82,0,0,TAU);ctx.fill();if(!drawGeneratedConstructionSprite(b,m,prog))drawScaffold(m.w,m.dep,prog)}
  const generated=drawGeneratedBuildingSprite(b,p,m,prog);
  if(!generated){
  if(b.type==='farm')drawFarm(b,p,m,prog);
  else if(b.type==='castle')drawCastle(b,p,m);
  else if(b.type==='blacksmith')drawBlacksmith(b,p,m);
  else if(b.type==='mill')drawMill(b,p,m);
  else if(b.type==='lumber')drawLumber(b,p,m);
  else if(b.type==='wall'){
    ctx.rotate(Math.PI*.25);drawPrism(m.w,m.dep,0,p);ctx.fillStyle=p.team;for(let x=-m.w+7;x<m.w;x+=14)ctx.fillRect(x,-m.dep+3,7,m.dep*2-6);
  }else if(b.type==='tower'){
    ctx.fillStyle=p.wallDark;ctx.beginPath();ctx.arc(0,0,m.w,0,TAU);ctx.fill();ctx.fillStyle=p.wallLight;ctx.beginPath();ctx.arc(0,0,m.w-5,0,TAU);ctx.fill();
    ctx.fillStyle=p.roof;for(let i=0;i<8;i++){const a=i/8*TAU;ctx.beginPath();ctx.arc(Math.cos(a)*(m.w-7),Math.sin(a)*(m.w-7),5,0,TAU);ctx.fill()}
    ctx.fillStyle='#172326';ctx.beginPath();ctx.arc(0,0,7,0,TAU);ctx.fill();drawBuildingDetails(b,p,m);
  }else if(b.type==='wonder'){
    ctx.fillStyle=p.wallDark;ctx.beginPath();ctx.arc(0,0,m.w,0,TAU);ctx.fill();ctx.fillStyle=p.wallLight;ctx.beginPath();ctx.arc(0,0,m.w-6,0,TAU);ctx.fill();
    ctx.strokeStyle=p.accent;ctx.lineWidth=7;for(const r of[m.w*.78,m.w*.5]){ctx.beginPath();ctx.arc(0,0,r,0,TAU);ctx.stroke()}
    ctx.fillStyle=p.roof;for(let i=0;i<8;i++){const a=i/8*TAU;ctx.beginPath();ctx.arc(Math.cos(a)*m.w*.63,Math.sin(a)*m.w*.63,8,0,TAU);ctx.fill()}
    const pulse=1+Math.sin(game.time*2)*.12;ctx.fillStyle='#f3d77e';ctx.shadowColor='#f3d77e';ctx.shadowBlur=14;ctx.beginPath();ctx.arc(0,0,9*pulse,0,TAU);ctx.fill();ctx.shadowBlur=0;
  }else if(b.type==='town'){
    drawPrism(m.w,m.dep,0,p);drawCivRoof(b.civ,m.w*.72,m.dep*.72,0,p);
    for(const sx of[-1,1])for(const sy of[-1,1]){ctx.save();ctx.translate(sx*m.w*.72,sy*m.dep*.7);ctx.fillStyle=p.wallDark;ctx.beginPath();ctx.arc(0,0,13,0,TAU);ctx.fill();ctx.fillStyle=p.roof;ctx.beginPath();ctx.arc(0,0,9,0,TAU);ctx.fill();ctx.restore()}
    ctx.fillStyle=p.accent;ctx.beginPath();ctx.arc(0,0,7,0,TAU);ctx.fill();drawBuildingDetails(b,p,m);
  }else{
    drawPrism(m.w,m.dep,0,p);drawCivRoof(b.civ,m.w*.86,m.dep*.82,0,p);drawBuildingDetails(b,p,m);
    if(b.type==='house'){ctx.fillStyle='rgba(255,206,103,.72)';for(const x of[-m.w*.38,m.w*.38]){ctx.beginPath();ctx.arc(x,0,4,0,TAU);ctx.fill()}}
  }
  }
  ctx.globalAlpha=1;
  drawBuildingActivity(b,p,m,prog);
  const hp=b.hp/b.maxHp;if(hp<.55){ctx.fillStyle=`rgba(45,48,46,${.18+(1-hp)*.28})`;for(let i=0;i<3;i++){const a=game.time*.35+i*2.1+b.id;ctx.beginPath();ctx.arc(Math.cos(a)*m.w*.35,Math.sin(a)*m.dep*.35,5+i*2,0,TAU);ctx.fill()}}
  if(hp<.72){ctx.strokeStyle='rgba(49,42,34,.72)';ctx.lineWidth=1.7;for(let i=0;i<3;i++){const a=hash2(b.id+i,23)*TAU,x=Math.cos(a)*m.w*.45,y=Math.sin(a)*m.dep*.42;ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(x*1.35+Math.sin(a)*7,y*1.35+Math.cos(a)*5);ctx.lineTo(x*1.55+Math.cos(a)*4,y*1.55-Math.sin(a)*4);ctx.stroke()}}
  if(hp<.3){const flick=(reducedMotion?.3:1)*Math.sin(game.time*8)*3;ctx.fillStyle='#f4a23f';polygon(ctx,[[-6,5],[0,-10-flick],[6,5],[0,11]]);ctx.fill();ctx.fillStyle='#ffe18b';polygon(ctx,[[-2,4],[0,-5-flick*.5],[3,4],[0,7]]);ctx.fill()}
  if(b.flash>0){ctx.globalCompositeOperation='screen';ctx.fillStyle=`rgba(255,239,199,${clamp(b.flash*3,0,.65)})`;ctx.beginPath();ctx.ellipse(0,0,m.w,m.dep,0,0,TAU);ctx.fill();ctx.globalCompositeOperation='source-over'}
  ctx.restore();
}

/* Pure deterministic pose sampler. It intentionally has no dependency on the
 * renderer clock or preference globals, which makes animation reproducible in
 * exported saves and straightforward to test. */
function unitIdlePose(u,time=0,reduce=false){
  const id=Number(u?.id)||0,moving=!!u?.path?.length,action=u?.order?.type||'idle',amp=reduce?.3:1;
  const phase=time*(moving?7.4:2.15)+id*1.731,slow=time*.73+id*2.117,gait=moving?Math.sin(phase):Math.sin(phase*.53)*.28;
  const work=(action==='gather'||action==='build')&&!moving?Math.sin(time*7.6+id*.83):0;
  const attack=action==='attack'&&!moving?Math.sin(time*8.8+id*.61):0;
  const active=Math.abs(work)>Math.abs(attack)?work:attack;
  return{
    phase,moving,action,
    bob:(moving?Math.abs(Math.sin(phase))*2.8:Math.sin(phase)*1.55+Math.sin(slow)*.45)*amp,
    breath:1+(moving?Math.sin(phase)*.018:Math.sin(phase)*.052)*amp,
    sway:(moving?gait*.045:Math.sin(slow)*.075)*amp,
    headTurn:(moving?gait*.08:Math.sin(slow)*.3+Math.sin(slow*.37)*.08)*amp,
    arm:(moving?gait*.48:Math.sin(phase*.81+1.2)*.32)*amp,
    foot:(moving?gait*4.4:(Math.sin(phase*.64)>0?Math.sin(phase*.64)*2.6:0))*amp,
    tail:Math.sin(time*3.2+id*1.31)*.48*amp,
    crew:Math.sin(time*2.7+id*.91)*.34*amp,
    banner:Math.sin(time*4.1+id*.47)*.34*amp,
    work:work*amp,attack:attack*amp,gear:(Math.sin(phase*.91+1.4)*.24+active*.72)*amp,
    lunge:active*3.1*amp
  };
}

function projectedFacing(angle){return{right:Math.cos(angle||0)>=0,angle:Number(angle)||0}}
function unitFacingRotation(u){return(Number.isFinite(u.angle)?u.angle:0)+Math.PI/2}
function isElephantUnit(u){return u?.type==='warElephant'||/elephant/i.test(u?.type||'')}
function isCamelUnit(u){return/camel/i.test(u?.type||'')||u?.type==='mameluke'}

function drawLimbs(team,pose,bodyY=0){
  ctx.strokeStyle='#252b2d';ctx.lineWidth=4;ctx.lineCap='round';
  ctx.beginPath();ctx.moveTo(-4,bodyY+5);ctx.lineTo(-5,bodyY+12+pose.foot);ctx.moveTo(4,bodyY+5);ctx.lineTo(5,bodyY+12-pose.foot);ctx.stroke();
  ctx.fillStyle='#171d1e';for(const[x,y]of[[-5,bodyY+13+pose.foot],[5,bodyY+13-pose.foot]]){ctx.beginPath();ctx.ellipse(x,y,3.5,5,0,0,TAU);ctx.fill()}
  ctx.strokeStyle=shade(team,.72);ctx.lineWidth=4;ctx.beginPath();ctx.moveTo(-6,bodyY-2);ctx.lineTo(-10-pose.arm*3,bodyY+5);ctx.moveTo(6,bodyY-2);ctx.lineTo(10+pose.arm*3,bodyY+5);ctx.stroke();
}

function drawHumanUnit(u,d,team,pose,bodyX=0,bodyY=0,rider=false){
  const civ=CIVS[u.civ]||CIVS[game.player.civ],heavy=d.role==='infantry',ranged=d.role==='ranged'||d.ranged,work=pose.work,attack=pose.attack;
  ctx.save();ctx.translate(bodyX,bodyY+pose.lunge);
  if(!rider)drawLimbs(team,pose,0);
  ctx.fillStyle=shade(team,.68);ctx.beginPath();ctx.ellipse(0,1,8.5,11,0,0,TAU);ctx.fill();ctx.strokeStyle='rgba(8,14,16,.78)';ctx.lineWidth=1.6;ctx.stroke();
  ctx.fillStyle=team;ctx.beginPath();ctx.ellipse(0,-1,7.2,9.5,0,0,TAU);ctx.fill();ctx.strokeStyle='rgba(255,245,211,.22)';ctx.lineWidth=1;ctx.stroke();
  ctx.strokeStyle='rgba(255,255,235,.55)';ctx.lineWidth=1.2;ctx.beginPath();ctx.moveTo(-4,-7);ctx.lineTo(-3,6);ctx.stroke();
  if(u.type==='woadRaider'){ctx.strokeStyle='#4ba5d1';ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(-6,-5);ctx.lineTo(6,5);ctx.moveTo(5,-6);ctx.lineTo(-4,6);ctx.stroke()}
  if(u.type==='teutonicKnight'){ctx.fillStyle='#e8e7dc';ctx.beginPath();ctx.ellipse(0,-1,7.4,9.7,0,0,TAU);ctx.fill();ctx.strokeStyle='#22282a';ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(0,-9);ctx.lineTo(0,7);ctx.moveTo(-5,-2);ctx.lineTo(5,-2);ctx.stroke()}
  const headX=Math.sin(pose.headTurn)*3.2,headY=-12-Math.cos(pose.headTurn)*1.1;
  ctx.fillStyle='#d8b38c';ctx.beginPath();ctx.arc(headX,headY,5.4,0,TAU);ctx.fill();ctx.strokeStyle='rgba(34,27,23,.72)';ctx.lineWidth=1.1;ctx.stroke();
  ctx.fillStyle=heavy?'#59666a':'#394b50';ctx.beginPath();ctx.arc(headX,headY,6.1,Math.PI,TAU);ctx.fill();
  ctx.fillStyle=civ.accent||'#d4bd75';
  if(u.type==='cataphract'){ctx.save();ctx.translate(headX,headY-5);ctx.rotate(pose.headTurn*.3);polygon(ctx,[[-5,1],[-2,-10],[2,-10],[6,1]]);ctx.fill();ctx.restore()}
  else if(u.type==='samurai'){ctx.fillRect(headX-7,headY-7,14,4);ctx.beginPath();ctx.arc(headX,headY-8,3,0,TAU);ctx.fill();ctx.strokeStyle=civ.accent;ctx.lineWidth=2;ctx.beginPath();ctx.arc(headX,headY-7,8,Math.PI*1.05,Math.PI*1.95);ctx.stroke()}
  else if(u.type==='berserk'){ctx.fillStyle='#6d7680';ctx.fillRect(headX-6,headY-7,12,4);ctx.strokeStyle='#e5dfc8';ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(headX-5,headY-6);ctx.quadraticCurveTo(headX-11,headY-12,headX-12,headY-6);ctx.moveTo(headX+5,headY-6);ctx.quadraticCurveTo(headX+11,headY-12,headX+12,headY-6);ctx.stroke()}
  else if(u.type==='janissary'){ctx.fillStyle='#eee8d1';roundedRect(ctx,headX-5,headY-15,10,12,4);ctx.fill();ctx.fillStyle=civ.accent;ctx.fillRect(headX-5,headY-5,10,3)}
  else if(u.type==='teutonicKnight'){ctx.fillStyle='#e7e6db';ctx.fillRect(headX-6,headY-7,12,5);ctx.strokeStyle='#25292b';ctx.lineWidth=1.5;ctx.beginPath();ctx.moveTo(headX,headY-8);ctx.lineTo(headX,headY-1);ctx.stroke()}
  else if(u.type==='huskarl'){ctx.fillStyle='#3c4549';ctx.fillRect(headX-6,headY-7,12,4);ctx.fillRect(headX-1,headY-5,2,7)}
  else if(u.type==='mangudai'){ctx.fillStyle='#6d5038';ctx.beginPath();ctx.arc(headX,headY-5,7,Math.PI,TAU);ctx.fill()}
  else if(u.type==='longbowman'){ctx.fillStyle='#385542';ctx.beginPath();ctx.arc(headX,headY,6.8,Math.PI,TAU);ctx.fill()}
  const handX=10+pose.arm*3,handY=4,toolSwing=work*.95+attack*.7+pose.gear*.35;
  ctx.save();ctx.translate(handX,handY);ctx.rotate(toolSwing);
  if(u.type==='longbowman'){
    ctx.strokeStyle='#cda86e';ctx.lineWidth=2.5;ctx.beginPath();ctx.arc(-4,-7,16,-Math.PI*.72,Math.PI*.72);ctx.stroke();ctx.beginPath();ctx.moveTo(-14,-18);ctx.lineTo(-14,4);ctx.stroke();ctx.strokeStyle='#e8d7a4';ctx.beginPath();ctx.moveTo(-14,-7);ctx.lineTo(12,-7);ctx.stroke();
  }else if(u.type==='chuKoNu'){
    ctx.fillStyle='#7b5634';roundedRect(ctx,-5,-14,11,25,3);ctx.fill();ctx.strokeStyle='#d9c18b';ctx.lineWidth=2;for(const x of[-4,0,4]){ctx.beginPath();ctx.moveTo(x,-17);ctx.lineTo(x,-29);ctx.stroke()}ctx.fillStyle='#a77843';ctx.fillRect(-9,-13,18,6);
  }else if(u.type==='throwingAxeman'||u.type==='huskarl'||u.type==='berserk'){
    ctx.strokeStyle='#775234';ctx.lineWidth=3;ctx.beginPath();ctx.moveTo(0,8);ctx.lineTo(0,-17);ctx.stroke();ctx.fillStyle='#bfc7c5';polygon(ctx,[[-8,-21],[7,-18],[7,-9],[-8,-13]]);ctx.fill();if(u.type==='berserk'){ctx.save();ctx.rotate(-1.2);ctx.translate(-12,2);ctx.fillStyle='#aeb8b7';polygon(ctx,[[-6,-9],[6,-9],[5,-2],[-5,-3]]);ctx.fill();ctx.restore()}
  }else if(u.type==='mameluke'){
    ctx.strokeStyle='#d7c78f';ctx.lineWidth=3;ctx.beginPath();ctx.arc(0,-7,12,-Math.PI*.25,Math.PI*.8);ctx.stroke();ctx.fillStyle='#d9e0dc';polygon(ctx,[[-2,-20],[4,-26],[5,-17]]);ctx.fill();
  }else if(u.type==='janissary'){
    ctx.strokeStyle='#4c3a2b';ctx.lineWidth=4;ctx.beginPath();ctx.moveTo(0,8);ctx.lineTo(0,-27);ctx.stroke();ctx.strokeStyle='#b8c0bd';ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(-3,-25);ctx.lineTo(3,-25);ctx.stroke();
  }else if(u.type==='samurai'){
    ctx.strokeStyle='#e2e5de';ctx.lineWidth=3;ctx.beginPath();ctx.moveTo(0,9);ctx.quadraticCurveTo(4,-7,1,-25);ctx.stroke();ctx.strokeStyle='#6f4b2d';ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(-5,-4);ctx.lineTo(6,-4);ctx.stroke();
  }else if(u.type==='woadRaider'){
    ctx.strokeStyle='#e2e4dd';ctx.lineWidth=3;ctx.beginPath();ctx.moveTo(0,8);ctx.quadraticCurveTo(6,-7,1,-23);ctx.stroke();ctx.fillStyle='#7b5433';ctx.fillRect(-5,-3,10,3);
  }else if(u.type==='teutonicKnight'){
    ctx.strokeStyle='#e1e5e1';ctx.lineWidth=4;ctx.beginPath();ctx.moveTo(0,10);ctx.lineTo(0,-25);ctx.stroke();ctx.strokeStyle='#8a6a43';ctx.lineWidth=3;ctx.beginPath();ctx.moveTo(-7,-5);ctx.lineTo(7,-5);ctx.stroke();
  }else if(ranged){
    ctx.strokeStyle='#dbc48f';ctx.lineWidth=2.1;ctx.beginPath();ctx.arc(0,-5,10,-Math.PI*.72,Math.PI*.72);ctx.stroke();ctx.beginPath();ctx.moveTo(-6,-12);ctx.lineTo(-6,2);ctx.stroke();
  }else if(u.type==='spear'||u.type==='cataphract'){
    ctx.strokeStyle='#d8bf83';ctx.lineWidth=2.4;ctx.beginPath();ctx.moveTo(0,7);ctx.lineTo(0,-25);ctx.stroke();ctx.fillStyle='#d6dcd7';polygon(ctx,[[-3,-24],[0,-32],[3,-24]]);ctx.fill();
  }else if(d.role==='worker'){
    ctx.strokeStyle='#704c2f';ctx.lineWidth=2.6;ctx.beginPath();ctx.moveTo(0,7);ctx.lineTo(0,-17);ctx.stroke();ctx.fillStyle='#c2a26c';ctx.fillRect(-7,-19,14,5);
  }else{
    ctx.strokeStyle='#e0c789';ctx.lineWidth=2.6;ctx.beginPath();ctx.moveTo(0,7);ctx.lineTo(0,-18);ctx.stroke();ctx.fillStyle='#c6d0ce';polygon(ctx,[[-2,-18],[0,-25],[3,-18]]);ctx.fill();
  }
  ctx.restore();
  if(['cataphract','huskarl','teutonicKnight'].includes(u.type)){ctx.fillStyle=u.type==='teutonicKnight'?'#e5e4da':u.type==='huskarl'?'#4e5b62':'#744139';ctx.strokeStyle='#d2b36e';ctx.lineWidth=1.4;roundedRect(ctx,-14,-7,9,18,2);ctx.fill();ctx.stroke();if(u.type==='teutonicKnight'){ctx.strokeStyle='#202628';ctx.lineWidth=1.6;ctx.beginPath();ctx.moveTo(-9,-5);ctx.lineTo(-9,9);ctx.moveTo(-13,1);ctx.lineTo(-5,1);ctx.stroke()}}
  if(u.carrying){ctx.fillStyle=u.carrying.type==='gold'?'#efc65e':u.carrying.type==='stone'?'#aeb8b9':'#856140';ctx.beginPath();ctx.arc(-10,4,4,0,TAU);ctx.fill()}
  ctx.restore();
}

function drawMount(u,team,pose){
  if(isElephantUnit(u)){
    ctx.strokeStyle='#505d5f';ctx.lineWidth=6;for(const[x,y,s]of[[-10,-4,1],[10,-4,-1],[-10,12,-1],[10,12,1]]){ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(x+s*pose.foot*.45,y+8);ctx.stroke()}
    ctx.fillStyle='#6f7d80';ctx.beginPath();ctx.ellipse(0,4,19,27,0,0,TAU);ctx.fill();ctx.strokeStyle='#3f4b4d';ctx.lineWidth=1.8;ctx.stroke();
    ctx.fillStyle='#7f8c8e';ctx.beginPath();ctx.arc(Math.sin(pose.headTurn)*3,-23,12,0,TAU);ctx.fill();
    ctx.strokeStyle='#7f8c8e';ctx.lineWidth=7;ctx.beginPath();ctx.moveTo(2,-27);ctx.quadraticCurveTo(10,-38+pose.tail*3,7,-44);ctx.stroke();
    ctx.strokeStyle='#d9dfd9';ctx.lineWidth=2;for(const x of[-7,7]){ctx.beginPath();ctx.moveTo(x,-28);ctx.quadraticCurveTo(x*1.7,-35,x*1.5,-39);ctx.stroke()}
    ctx.fillStyle=team;roundedRect(ctx,-14,-4,28,16,3);ctx.fill();
    if(u.type==='warElephant'){ctx.strokeStyle='#e5cc83';ctx.lineWidth=2;ctx.stroke();ctx.fillStyle='#6b422e';for(const x of[-11,11])ctx.fillRect(x-2,-9,4,19);ctx.fillStyle='#d9c98d';for(const x of[-11,11]){ctx.beginPath();ctx.arc(x,-9,3,0,TAU);ctx.fill()}}
    return{bodyX:0,bodyY:-7};
  }
  const camel=isCamelUnit(u),headTurn=pose.headTurn;
  ctx.strokeStyle=camel?'#7d5734':'#3a2c24';ctx.lineWidth=4;for(const[x,y,s]of[[-8,-1,1],[8,-1,-1],[-8,13,-1],[8,13,1]]){ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(x+s*pose.foot*.55,y+7);ctx.stroke()}
  ctx.fillStyle=camel?'#9d7043':'#574032';ctx.beginPath();ctx.ellipse(0,6,13,22,0,0,TAU);ctx.fill();ctx.strokeStyle=camel?'#64452d':'#30251f';ctx.lineWidth=1.6;ctx.stroke();
  if(camel){ctx.fillStyle='#b0804e';ctx.beginPath();ctx.arc(0,1,8,0,TAU);ctx.fill()}
  ctx.fillStyle=camel?'#835b37':'#3a2c24';ctx.beginPath();ctx.ellipse(Math.sin(headTurn)*2,-18,8,11,headTurn*.2,0,TAU);ctx.fill();
  ctx.strokeStyle=camel?'#6f4b2e':'#30231d';ctx.lineWidth=3;ctx.beginPath();ctx.moveTo(0,26);ctx.quadraticCurveTo(pose.tail*15,31,pose.tail*19,35);ctx.stroke();
  if(u.type==='cataphract'){
    ctx.fillStyle='#aeb6b7';for(const y of[-6,1,8,15])roundedRect(ctx,-12,y,24,6,2),ctx.fill();ctx.strokeStyle='#e2d4a2';ctx.lineWidth=1;ctx.beginPath();ctx.moveTo(-10,-4);ctx.lineTo(10,17);ctx.stroke();
  }else if(u.type==='mangudai'){
    ctx.fillStyle='#76583d';roundedRect(ctx,-12,-1,24,13,4);ctx.fill();ctx.strokeStyle='#d1b77b';ctx.lineWidth=1.4;ctx.stroke();
  }
  ctx.fillStyle=team;roundedRect(ctx,-11,0,22,11,2);ctx.fill();return{bodyX:0,bodyY:-2};
}

function drawSiegeUnit(u,d,team,pose){
  const recoil=pose.attack*4,turn=pose.crew;
  ctx.fillStyle='#4b3424';roundedRect(ctx,-18,-20+recoil,36,40,5);ctx.fill();ctx.strokeStyle='#bea070';ctx.lineWidth=2;ctx.stroke();
  ctx.fillStyle='#24292a';for(const x of[-20,20])for(const y of[-12,12]){ctx.beginPath();ctx.arc(x,y,6,0,TAU);ctx.fill();ctx.strokeStyle='#8d744f';ctx.lineWidth=2;ctx.stroke()}
  ctx.fillStyle='#d2aa83';for(const x of[-8,8]){ctx.beginPath();ctx.arc(x+turn*2,8,4.5,0,TAU);ctx.fill()}
  if(u.type==='ram'){
    ctx.fillStyle='#2d2720';roundedRect(ctx,-8,-28+recoil,16,55,6);ctx.fill();ctx.fillStyle=team;ctx.fillRect(-6,-13,12,24);ctx.fillStyle='#c1b38d';polygon(ctx,[[-8,-27+recoil],[0,-37+recoil],[8,-27+recoil]]);ctx.fill();
  }else{
    ctx.save();ctx.translate(0,6);ctx.rotate(pose.gear*.34-pose.attack*.85);ctx.strokeStyle='#a98b5d';ctx.lineWidth=5;ctx.beginPath();ctx.moveTo(0,8);ctx.lineTo(0,-32);ctx.stroke();ctx.fillStyle='#75746c';ctx.beginPath();ctx.arc(0,-34,5,0,TAU);ctx.fill();ctx.restore();
    ctx.fillStyle=team;ctx.save();ctx.rotate(pose.banner*.15);ctx.fillRect(-13,-18,8,7);ctx.restore();
  }
}

function drawUnitMotionUnderlay(u,d,team,pose){
  if(pose.moving){
    const step=(Math.sin(pose.phase)+1)*.5;ctx.fillStyle=`rgba(205,188,139,${.1+step*.13})`;for(const side of[-1,1]){ctx.beginPath();ctx.ellipse(side*7-pose.foot*.3,15+side*pose.foot*.2,4+step*2,2.2,0,0,TAU);ctx.fill()}
    ctx.strokeStyle='rgba(240,224,174,.18)';ctx.lineWidth=1;for(const side of[-1,1]){ctx.beginPath();ctx.moveTo(side*10,12);ctx.lineTo(side*14,20+side*pose.foot);ctx.stroke()}
  }
  if(d.role!=='worker'&&d.role!=='siege'){
    ctx.fillStyle=shade(team,.72);ctx.save();ctx.translate(0,7);ctx.rotate(pose.banner*.18);polygon(ctx,[[-5,-4],[5,-4],[3,12+pose.banner*4],[-2,9]]);ctx.fill();ctx.restore();
  }
}
function drawUnitAccents(u,d,team,pose){
  const striking=u.order?.type==='attack'&&!pose.moving&&(Math.abs(pose.attack)>.45||u.attackTimer>0&&u.attackTimer<.2),working=(u.order?.type==='gather'||u.order?.type==='build')&&!pose.moving;
  if(striking){
    ctx.save();ctx.globalCompositeOperation='screen';ctx.strokeStyle=d.ranged?'rgba(246,220,144,.62)':'rgba(255,237,190,.68)';ctx.lineWidth=2.2;ctx.beginPath();ctx.arc(1,-3,d.role==='cavalry'?27:22,-Math.PI*.9,-Math.PI*.08);ctx.stroke();ctx.fillStyle='rgba(255,235,169,.75)';ctx.beginPath();ctx.arc(16,-15,2.3,0,TAU);ctx.fill();ctx.restore();
  }
  if(working){
    const pulse=Math.max(0,pose.work);ctx.fillStyle=`rgba(245,203,119,${.18+pulse*.5})`;for(let i=0;i<3;i++){const a=i*2.1+pose.phase*.7;ctx.beginPath();ctx.arc(11+Math.cos(a)*7,-8+Math.sin(a)*5,1.2+i*.25,0,TAU);ctx.fill()}
  }
  if(!pose.moving&&!striking&&d.role!=='siege'){
    const glint=Math.max(0,Math.sin(game.time*.9+u.id*2.3));if(glint>.92){ctx.fillStyle=`rgba(255,245,196,${(glint-.92)*5})`;ctx.beginPath();ctx.arc(-4,-7,1.8,0,TAU);ctx.fill()}
  }
}

function drawVillagerWorkTool(u,pose,state){
  if(u.type!=='villager'||!state?.active||!['gather','build'].includes(state.kind))return;const beat=workBeat(u.id),rawSwing=-1.08+beat.swing*1.72,swing=-.2+rawSwing*motionAmount(),resource=state.kind==='build'?'build':state.resource;
  ctx.save();ctx.translate(13,-1);ctx.rotate(swing);ctx.lineCap='round';ctx.strokeStyle='#6f492d';ctx.lineWidth=3.4;ctx.beginPath();ctx.moveTo(0,9);ctx.lineTo(0,-19);ctx.stroke();
  if(resource==='wood'){ctx.fillStyle='#d5dcda';ctx.strokeStyle='#4e5b5d';ctx.lineWidth=1;polygon(ctx,[[-2,-22],[10,-25],[9,-14],[-2,-17]]);ctx.fill();ctx.stroke()}
  else if(resource==='gold'||resource==='stone'){ctx.strokeStyle=resource==='gold'?'#f2d276':'#d6dede';ctx.lineWidth=3;ctx.beginPath();ctx.moveTo(-10,-22);ctx.quadraticCurveTo(0,-28,11,-22);ctx.stroke();ctx.fillStyle='#d5dcda';polygon(ctx,[[-12,-24],[-7,-19],[-11,-18]]);ctx.fill();polygon(ctx,[[12,-24],[7,-19],[11,-18]]);ctx.fill()}
  else if(resource==='food'){ctx.strokeStyle='#e7d59b';ctx.lineWidth=3;ctx.beginPath();ctx.arc(5,-20,10,-Math.PI*.2,Math.PI*.82);ctx.stroke();ctx.fillStyle='#bdc878';ctx.beginPath();ctx.ellipse(10,-25,4,2,-.5,0,TAU);ctx.fill()}
  else{ctx.fillStyle='#c8d0cf';ctx.strokeStyle='#596365';ctx.lineWidth=1;roundedRect(ctx,-8,-24,16,8,2);ctx.fill();ctx.stroke()}
  if(beat.impact){ctx.globalCompositeOperation='screen';const color=state.color||'#f1d07b';ctx.strokeStyle=color;ctx.lineWidth=2;ctx.globalAlpha=.88;for(let i=0;i<4;i++){const a=-2.8+i*.62;ctx.beginPath();ctx.moveTo(Math.cos(a)*7,-22+Math.sin(a)*5);ctx.lineTo(Math.cos(a)*15,-22+Math.sin(a)*12);ctx.stroke()}ctx.fillStyle=color;ctx.beginPath();ctx.arc(0,-22,4.5,0,TAU);ctx.fill()}
  ctx.restore();
}

function drawGeneratedUnitSprite(u,d,team,pose){
  const sprite=typeof GeneratedArt!=='undefined'&&GeneratedArt.getUnitSprite(u.type);if(!sprite)return false;
  const state=unitActivityState(u),working=state.active&&(state.kind==='gather'||state.kind==='build'),active=pose.attack*.12+pose.work*(working ? 0.13 : 0.055),forward=pose.lunge*(working?1.55:1)+(pose.moving?Math.abs(pose.foot)*.14:0);
  ctx.save();ctx.translate(0,forward-Math.abs(pose.work)*(working?2.4:0));ctx.rotate(active);ctx.scale(1+Math.abs(active)*.2,1-Math.abs(active)*.11);
  /* Imagegen figures face screen-down.  The procedural unit transform faces
     screen-up, so this half-turn lets the existing movement angle orient both. */
  ctx.rotate(Math.PI);
  ctx.imageSmoothingEnabled=true;ctx.imageSmoothingQuality='high';ctx.filter=frameVisibleUnits>120?'drop-shadow(0 2px 1px rgba(0,0,0,.96))':`drop-shadow(0 2px 1px rgba(0,0,0,.96)) drop-shadow(0 0 2px ${team})`;
  ctx.drawImage(sprite.image,sprite.sx,sprite.sy,sprite.sw,sprite.sh,-sprite.width/2,-sprite.height/2,sprite.width,sprite.height);
  ctx.restore();
  drawVillagerWorkTool(u,pose,state);

  /* A compact heraldic marker keeps factions readable without recolouring the
     historically detailed sprite, and remains visible in dense formations. */
  ctx.save();ctx.translate(-14,14);ctx.fillStyle='rgba(2,6,9,.94)';ctx.beginPath();ctx.arc(0,0,7.5,0,TAU);ctx.fill();ctx.strokeStyle='rgba(255,255,255,.72)';ctx.lineWidth=1.2;ctx.stroke();
  ctx.fillStyle=team;ctx.beginPath();ctx.arc(0,0,5.5,0,TAU);ctx.fill();ctx.strokeStyle='rgba(255,244,205,.86)';ctx.lineWidth=1;ctx.stroke();ctx.restore();
  return true;
}

function drawUnitGrounding(u,screen,scale){
  const d=UNIT[u.type],large=isElephantUnit(u),siege=d.role==='siege',cavalry=d.role==='cavalry',rx=(large?23:siege?21:cavalry?17:13)*scale,ry=(large?13:siege?12:cavalry?10:8)*scale,team=teamColor(u),pulse=1+(reducedMotion?0:Math.sin(game.time*2.4+u.id)*.025),powered=powerActive(u.faction),regenerating=!!d.regen&&u.hp<u.maxHp&&game.time-(u.lastHit??-99)>4;
  if(powered||regenerating)drawGeneratedEffect('healAura',screen.x,screen.y,rx*3.2,ry*3.5,(powered ? 0.35 : 0.2)+(reducedMotion?0:Math.sin(game.time*3+u.id)*.06),game.time*.18+u.id);
  ctx.save();ctx.translate(screen.x,screen.y+4*scale);ctx.scale(pulse,1/pulse);ctx.fillStyle='rgba(3,8,11,.3)';ctx.beginPath();ctx.ellipse(0,0,rx,ry,0,0,TAU);ctx.fill();ctx.fillStyle='rgba(3,8,11,.58)';ctx.beginPath();ctx.ellipse(0,0,rx*.7,ry*.66,0,0,TAU);ctx.fill();
  ctx.strokeStyle='rgba(0,0,0,.92)';ctx.lineWidth=Math.max(3,3.4*scale);ctx.beginPath();ctx.ellipse(0,0,rx*.88,ry*.82,0,0,TAU);ctx.stroke();ctx.strokeStyle=team;ctx.globalAlpha=.9;ctx.lineWidth=Math.max(1.5,1.8*scale);ctx.stroke();ctx.strokeStyle='rgba(255,255,255,.58)';ctx.globalAlpha=.72;ctx.lineWidth=1;ctx.beginPath();ctx.ellipse(0,0,rx*.88,ry*.82,0,-Math.PI*.88,-Math.PI*.18);ctx.stroke();ctx.restore();
}

function drawUnit(u,alpha){
  const d=UNIT[u.type],wp=entityWorldPosition(u,alpha),screen=worldToScreen(wp.x,wp.y);if(!onScreen(screen,90))return;
  const team=teamColor(u),pose=unitIdlePose(u,game.time+alpha*STEP,reducedMotion),elephant=isElephantUnit(u);
  const scale=Math.max(renderZoom(),.82)*(elephant?1.18:d.role==='siege'?1.08:1);
  drawUnitGrounding(u,screen,scale);
  ctx.save();ctx.translate(screen.x,screen.y-pose.bob*scale);ctx.scale(scale*pose.breath,scale/pose.breath);ctx.rotate(unitFacingRotation(u)+pose.sway);
  drawUnitMotionUnderlay(u,d,team,pose);
  const generated=drawGeneratedUnitSprite(u,d,team,pose);
  if(!generated){
    if(d.role==='siege')drawSiegeUnit(u,d,team,pose);
    else if(d.role==='cavalry'){const saddle=drawMount(u,team,pose);drawHumanUnit(u,d,team,pose,saddle.bodyX,saddle.bodyY,true)}
    else drawHumanUnit(u,d,team,pose);
  }
  drawUnitAccents(u,d,team,pose);
  if(u.flash>0){ctx.globalCompositeOperation='screen';ctx.fillStyle=`rgba(255,255,255,${clamp(u.flash*4,0,.72)})`;ctx.beginPath();ctx.ellipse(0,0,18,25,0,0,TAU);ctx.fill();ctx.globalCompositeOperation='source-over'}
  ctx.restore();
}

function entityDepth(e){return e.y}
/* Kept as a harmless API shim for old input/tests. A flat top-down building is
 * never considered to occlude a unit and no x-ray silhouette is drawn. */
function unitOccludedByBuilding(){return false}
function drawOccludedUnitSilhouette(){}
function drawOccludedFriendlyUnits(){}

function drawStackHoverCue(){
  if(mouse.down||buildMode||attackMove||typeof friendlyUnitHitsAtScreen!=='function')return;const hits=friendlyUnitHitsAtScreen(mouse.x,mouse.y);if(hits.length<2)return;
  const count=Math.min(hits.length,8);for(let i=0;i<count;i++){const u=hits[i],p=worldToScreen(u.x,u.y),a=i/count*TAU,rad=22+Math.floor(i/4)*14,x=p.x+Math.cos(a)*rad,y=p.y+Math.sin(a)*rad;
    ctx.save();ctx.translate(x,y);ctx.fillStyle='rgba(5,12,17,.94)';ctx.strokeStyle=teamColor(u);ctx.lineWidth=1.7;ctx.beginPath();ctx.arc(0,0,9,0,TAU);ctx.fill();ctx.stroke();ctx.fillStyle='#fff4d7';ctx.font=`700 12px ${RENDER_FONT}`;ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(String(i+1),0,.5);ctx.restore()}
}

function drawEntityShadow(entry,alpha){
  const e=entry.e,wp=entityWorldPosition(e,alpha),p=worldToScreen(wp.x,wp.y);if(!onScreen(p,180))return;const z=renderZoom();
  const sunX=(5+Math.sin(game.time/140)*1.4)*z,sunY=(7+Math.cos(game.time/170)*1.1)*z;ctx.save();ctx.globalCompositeOperation='multiply';ctx.fillStyle='rgba(4,9,11,.27)';
  if(entry.kind==='building'){
    const m=buildingMetrics(e);ctx.beginPath();ctx.ellipse(p.x+sunX,p.y+sunY,m.w*z*1.03,m.dep*z*.96,.03,0,TAU);ctx.fill();ctx.fillStyle='rgba(2,6,8,.2)';ctx.beginPath();ctx.ellipse(p.x+2*z,p.y+3*z,m.w*z*.9,m.dep*z*.84,0,0,TAU);ctx.fill();
  }else if(entry.kind==='resource'){
    const r=e.type==='wood'?19:16;ctx.beginPath();ctx.ellipse(p.x+sunX,p.y+sunY,r*z*1.08,r*z*.62,.06,0,TAU);ctx.fill();
  }else if(entry.kind==='site'){
    ctx.beginPath();ctx.ellipse(p.x+sunX,p.y+sunY,45*z,38*z,0,0,TAU);ctx.fill();
  }else{
    const d=UNIT[e.type],large=isElephantUnit(e),cav=d?.role==='cavalry',siege=d?.role==='siege',pose=unitIdlePose(e,game.time+alpha*STEP,reducedMotion),pulse=1-(pose.breath-1)*1.3;
    ctx.beginPath();ctx.ellipse(p.x+sunX*.65,p.y+sunY*.65,(large?22:siege?20:cav?16:11)*z*pulse,(large?15:siege?14:cav?11:8)*z/pulse,.04,0,TAU);ctx.fill();ctx.fillStyle='rgba(1,5,7,.18)';ctx.beginPath();ctx.ellipse(p.x,p.y+2*z,(large?16:siege?15:cav?11:8)*z,(large?9:siege?9:cav?7:5)*z,0,0,TAU);ctx.fill();
  }
  ctx.restore();
}

function drawSelectionBase(e,alpha){
  if(!e.selected)return;const wp=entityWorldPosition(e,alpha),p=worldToScreen(wp.x,wp.y),z=renderZoom(),r=(e.radius+8)*z;
  drawGeneratedEffect('selectionRing',p.x,p.y,r*3.15,r*2.42,.38+(reducedMotion?0:Math.sin(game.time*3+e.id)*.08),game.time*.08);
  ctx.save();ctx.fillStyle=`${teamColor(e)}26`;ctx.strokeStyle='rgba(255,255,255,.34)';ctx.lineWidth=1.2;ctx.beginPath();ctx.ellipse(p.x,p.y,r,r*.78,0,0,TAU);ctx.fill();ctx.stroke();ctx.restore();
}
function drawSelection(e,alpha){
  if(!e.selected)return;const wp=entityWorldPosition(e,alpha),p=worldToScreen(wp.x,wp.y),z=renderZoom(),r=(e.radius+9)*z,pulse=1+(reducedMotion?0:Math.sin(game.time*4+e.id)*.035);
  ctx.save();ctx.translate(p.x,p.y);ctx.scale(pulse,pulse);ctx.strokeStyle=teamColor(e);ctx.lineWidth=Math.max(2,3*z);ctx.setLineDash(e.kind==='building'?[8,5]:[12,4]);ctx.beginPath();ctx.ellipse(0,0,r,r*.78,0,0,TAU);ctx.stroke();ctx.setLineDash([]);
  ctx.strokeStyle='rgba(255,255,255,.92)';ctx.lineWidth=1.2;for(let i=0;i<4;i++){const a=i/4*TAU,span=.22;ctx.beginPath();ctx.ellipse(0,0,r+3,r*.78+2,0,a-span,a+span);ctx.stroke()}
  if(e.kind==='unit'){ctx.fillStyle='#fff4c8';for(let i=0;i<4;i++){const a=i/4*TAU,x=Math.cos(a)*(r+5),y=Math.sin(a)*(r*.78+4);ctx.save();ctx.translate(x,y);ctx.rotate(a+Math.PI/2);polygon(ctx,[[-3,0],[3,0],[0,-5]]);ctx.fill();ctx.restore()}}
  ctx.restore();
}

function drawActivityPill(x,y,label,color,ratio=null,compact=false){
  ctx.save();ctx.font=`700 12px ${RENDER_FONT}`;ctx.textAlign='center';ctx.textBaseline='middle';const text=String(label||''),textWidth=ctx.measureText?.(text)?.width||text.length*12,width=compact?24:Math.max(56,Math.min(132,textWidth+18)),height=compact?24:25;
  ctx.translate(Math.round(x)+.5,Math.round(y)+.5);ctx.fillStyle='rgba(4,10,14,.92)';ctx.strokeStyle='rgba(0,0,0,.95)';ctx.lineWidth=3;roundedRect(ctx,-width/2,-height/2,width,height,compact?12:6);ctx.fill();ctx.stroke();ctx.strokeStyle=color||'#e4c778';ctx.lineWidth=1.5;roundedRect(ctx,-width/2,-height/2,width,height,compact?12:6);ctx.stroke();
  if(Number.isFinite(ratio)){const r=clamp(ratio,0,1);ctx.fillStyle='rgba(255,255,255,.09)';roundedRect(ctx,-width/2+4,height/2-6,width-8,3,1.5);ctx.fill();ctx.fillStyle=color||'#e4c778';roundedRect(ctx,-width/2+4,height/2-6,(width-8)*r,3,1.5);ctx.fill()}
  ctx.fillStyle='#fff8df';ctx.lineWidth=2.5;ctx.strokeStyle='rgba(0,0,0,.88)';ctx.strokeText(text,0,Number.isFinite(ratio)?-2:0);ctx.fillText(text,0,Number.isFinite(ratio)?-2:0);ctx.restore();
}

function drawProgressOverlays(drawable,alpha){
  for(const entry of drawable){const e=entry.e;if(e.dead)continue;
    if(entry.kind==='unit'){
      if(e.faction!==0||!visibleAt(e.x,e.y))continue;const state=unitActivityState(e),showWork=state.active&&(state.kind==='gather'||state.kind==='build'),showOrder=e.selected&&state.active&&!showWork;if(!showWork&&!showOrder)continue;
      const wp=entityWorldPosition(e,alpha),p=worldToScreen(wp.x,wp.y),d=UNIT[e.type],base=d.role==='cavalry'||isElephantUnit(e)?64:55,visualScale=Math.max(renderZoom(),.82),bob=(reducedMotion?0:Math.sin(game.time*4.2+e.id)*2);drawActivityPill(p.x,p.y-base*visualScale+bob,e.selected?state.label:state.short,state.color,null,!e.selected);
    }else if(entry.kind==='building'){
      if(e.faction!==0||!visibleAt(e.x,e.y))continue;const states=buildingProgressStates(e);if(!states.length)continue;const p=worldToScreen(e.x,e.y),m=buildingMetrics(e),z=renderZoom(),baseY=p.y+(m.dep+15)*z;
      states.slice(0,3).forEach((state,i)=>drawActivityPill(p.x,baseY+i*29,state.label,state.color,state.ratio,false));
    }else if(entry.kind==='site'){
      if(!visibleAt(e.x,e.y))continue;const state=siteProgressState(e);if(!state)continue;const p=worldToScreen(e.x,e.y),bob=(reducedMotion?0:Math.sin(game.time*3+e.id)*2);drawActivityPill(p.x,p.y-55*renderZoom()+bob,state.label,state.color,state.ratio,false);
    }
  }
}

function drawProjectile(p){
  const total=p.totalDist||p._flightDistance||Math.max(1,Math.hypot(p.tx-(p.startX??p.x),p.ty-(p.startY??p.y))),traveled=Number.isFinite(p.traveled)?p.traveled:total-Math.hypot(p.tx-p.x,p.ty-p.y),progress=clamp(traveled/total,0,1),height=Math.sin(progress*Math.PI)*(p.siege?72:28)+4;
  const ground=worldToScreen(p.x,p.y),target=worldToScreen(p.tx,p.ty),z=Math.max(renderZoom(),.72),screen={x:ground.x,y:ground.y-height*z};if(!onScreen(screen,80))return;
  ctx.save();ctx.fillStyle='rgba(3,7,9,.28)';ctx.beginPath();ctx.ellipse(ground.x+4*z,ground.y+3*z,(p.siege?7:4)*z,(p.siege?4:2)*z,0,0,TAU);ctx.fill();
  const dx=target.x-screen.x,dy=target.y-screen.y,len=Math.hypot(dx,dy)||1,ux=dx/len,uy=dy/len,trail=(p.siege?28:19)*z,g=ctx.createLinearGradient(screen.x-ux*trail,screen.y-uy*trail,screen.x,screen.y);g.addColorStop(0,'rgba(255,190,92,0)');g.addColorStop(1,p.siege?'rgba(255,180,78,.58)':'rgba(255,235,170,.5)');ctx.strokeStyle=g;ctx.lineWidth=(p.siege?4:1.5)*z;ctx.beginPath();ctx.moveTo(screen.x-ux*trail,screen.y-uy*trail);ctx.lineTo(screen.x,screen.y);ctx.stroke();
  if(p.siege)for(let i=1;i<=3;i++){ctx.fillStyle=`rgba(255,${128+i*24},62,${.34/i})`;ctx.beginPath();ctx.arc(screen.x-ux*trail*i*.24+Math.sin(game.time*9+i)*3,screen.y-uy*trail*i*.24+Math.cos(game.time*7+i)*2,(4-i*.8)*z,0,TAU);ctx.fill()}
  ctx.translate(screen.x,screen.y);ctx.rotate(Math.atan2(dy,dx));ctx.fillStyle=p.color;ctx.shadowColor=p.color;ctx.shadowBlur=p.siege?11:6;
  if(p.siege){ctx.beginPath();ctx.arc(0,0,6*z,0,TAU);ctx.fill();ctx.fillStyle='rgba(255,225,151,.4)';ctx.beginPath();ctx.arc(-2*z,-2*z,2*z,0,TAU);ctx.fill()}
  else{ctx.fillRect(-8*z,-z,15*z,2*z);polygon(ctx,[[8*z,0],[3*z,-3*z],[3*z,3*z]]);ctx.fill()}ctx.restore();
}

function drawParticle(p){
  if(!Number.isFinite(p.baseX)){p.baseX=p.x;p.baseY=p.y}const origin=worldToScreen(p.baseX,p.baseY),z=renderZoom(),x=origin.x+(p.x-p.baseX)*z,y=origin.y+(p.y-p.baseY)*z;if(!onScreen({x,y},70))return;
  ctx.save();const life=clamp(p.life/p.max,0,1);ctx.globalAlpha=life;ctx.fillStyle=p.color;
  if(p.text){ctx.font=`700 12px ${RENDER_FONT}`;ctx.textAlign='center';ctx.lineWidth=3;ctx.strokeStyle='rgba(3,8,11,.75)';ctx.strokeText(p.text,x,y);ctx.fillText(p.text,x,y)}
  else if(p.ring){const t=1-life,r=p.size*z*(.45+t*.9),fxSize=(p.size+8)*z*(1.25+t*1.8);if(p.effect)drawGeneratedEffect(p.effect,x,y,fxSize,fxSize,Math.min(.9,.5+t*.35),p.x*.013+p.y*.007);ctx.globalAlpha=life*.78;ctx.strokeStyle=p.color;ctx.lineWidth=Math.max(1,2.4*z*(1-t));ctx.shadowColor=p.color;ctx.shadowBlur=8;ctx.beginPath();ctx.arc(x,y,r,0,TAU);ctx.stroke();ctx.shadowBlur=0}
  else{ctx.shadowColor=p.color;ctx.shadowBlur=p.size>3?6:3;ctx.beginPath();ctx.arc(x,y,Math.max(1,p.size*z),0,TAU);ctx.fill();ctx.shadowBlur=0}ctx.restore();
}

function drawMarker(m){
  const p=worldToScreen(m.x,m.y);if(!onScreen(p,60))return;const t=1-m.life/m.max,r=(10+t*24)*renderZoom();
  ctx.save();ctx.translate(p.x,p.y);ctx.globalAlpha=m.life/m.max;drawGeneratedEffect(m.type==='attack'?'swordSlash':m.type==='gather'?'healAura':'selectionRing',0,0,r*2.8,r*2.35,.58,game.time*.16);ctx.strokeStyle=m.type==='attack'?'#ff6d65':m.type==='gather'?'#75dba0':'#f2d27a';ctx.lineWidth=2;ctx.beginPath();ctx.arc(0,0,r,0,TAU);ctx.stroke();
  if(m.type==='attack'){ctx.beginPath();ctx.moveTo(-r*.72,-r*.72);ctx.lineTo(r*.72,r*.72);ctx.moveTo(r*.72,-r*.72);ctx.lineTo(-r*.72,r*.72);ctx.stroke()}else{ctx.beginPath();ctx.moveTo(-r*.4,0);ctx.lineTo(0,r*.4);ctx.lineTo(r*.65,-r*.45);ctx.stroke()}ctx.restore();
}

function visibleTileBounds(pad=1){
  const a=screenToWorld(0,0),b=screenToWorld(viewW,viewH);return{x0:clamp(Math.floor(Math.min(a.x,b.x)/TILE)-pad,0,MAP_W-1),x1:clamp(Math.floor(Math.max(a.x,b.x)/TILE)+pad,0,MAP_W-1),y0:clamp(Math.floor(Math.min(a.y,b.y)/TILE)-pad,0,MAP_H-1),y1:clamp(Math.floor(Math.max(a.y,b.y)/TILE)+pad,0,MAP_H-1)};
}
function drawGroundLife(){
  const bounds=visibleTileBounds(),z=renderZoom(),t=game.time,amp=motionAmount();ctx.save();ctx.lineCap='round';
  for(let y=bounds.y0;y<=bounds.y1;y++)for(let x=bounds.x0;x<=bounds.x1;x++){
    const type=terrain[y]?.[x];if(type===1||!exploredAt((x+.5)*TILE,(y+.5)*TILE))continue;const density=hash2(x*7+3,y*11+5);if(density<.32)continue;
    const count=density>.76?3:2;for(let i=0;i<count;i++){
      const wx=(x+hash2(x*31+i*17,y*19+2))*TILE,wy=(y+hash2(x*13+6,y*29+i*23))*TILE,p=worldToScreen(wx,wy);if(!onScreen(p,20))continue;
      const phase=t*1.35+x*.73+y*.47+i*1.9,lean=(Math.sin(phase)*2.3+1.1)*amp*z,height=(4+hash2(x+i*5,y+i*9)*5)*z;
      ctx.strokeStyle=type===2?'rgba(211,181,112,.27)':'rgba(159,202,106,.25)';ctx.lineWidth=Math.max(.65,z*.85);ctx.beginPath();ctx.moveTo(p.x,p.y);ctx.quadraticCurveTo(p.x+lean*.45,p.y-height*.55,p.x+lean,p.y-height);ctx.stroke();
      if(density>.88&&i===0){ctx.fillStyle=type===2?'rgba(239,201,118,.3)':'rgba(231,219,133,.34)';ctx.beginPath();ctx.arc(p.x+lean,p.y-height,1.3*z,0,TAU);ctx.fill()}
    }
  }ctx.restore();
}
function drawAmbientMotes(){
  const count=reducedMotion?28:72,t=game.time,z=renderZoom();ctx.save();ctx.globalCompositeOperation='screen';
  for(let i=0;i<count;i++){
    const bx=hash2(i*17+4,i*7+13)*WORLD_W,by=hash2(i*11+19,i*29+3)*WORLD_H,drift=18+hash2(i,41)*22,x=bx+Math.sin(t*.16+i*2.17)*drift,y=by+Math.cos(t*.12+i*.91)*drift*.55;if(!visibleAt(x,y))continue;const p=worldToScreen(x,y);if(!onScreen(p,10))continue;
    const pulse=.18+Math.max(0,Math.sin(t*1.4+i*1.37))*.42,size=(.7+hash2(i,79)*1.1)*Math.max(.8,z);ctx.fillStyle=`rgba(${i%5===0?'139,214,220':'241,218,151'},${pulse})`;ctx.beginPath();ctx.arc(p.x,p.y,size,0,TAU);ctx.fill();
  }ctx.restore();
}
function drawShoreFoam(x,y,a,b,time){
  const pulse=.18+(Math.sin(time*2.1+x*.8+y*.47)+1)*.08;ctx.strokeStyle=`rgba(215,244,230,${pulse})`;ctx.lineWidth=Math.max(1,1.25*renderZoom());ctx.setLineDash([4,6]);ctx.lineDashOffset=-time*5;ctx.beginPath();ctx.moveTo(a.x,a.y);ctx.quadraticCurveTo((a.x+b.x)*.5+Math.sin(time+x+y)*2,(a.y+b.y)*.5+Math.cos(time*.8+x-y)*2,b.x,b.y);ctx.stroke();ctx.setLineDash([]);
}
function drawWaterHighlights(){
  const time=game.time,bounds=visibleTileBounds();ctx.save();ctx.globalCompositeOperation='screen';
  for(let y=bounds.y0;y<=bounds.y1;y++)for(let x=bounds.x0;x<=bounds.x1;x++)if(terrain[y]?.[x]===1){
    const a=worldToScreen(x*TILE,y*TILE),b=worldToScreen((x+1)*TILE,(y+1)*TILE),phase=Math.sin(time*1.8+x*.91+y*.43),w=b.x-a.x,h=b.y-a.y;
    ctx.fillStyle=`rgba(74,166,184,${.025+(phase+1)*.018})`;ctx.fillRect(a.x,a.y,w,h);
    ctx.strokeStyle=`rgba(190,239,232,${.1+(phase+1)*.055})`;ctx.lineWidth=1;for(let i=0;i<2;i++){const yy=a.y+h*(.3+i*.36)+phase*2;ctx.beginPath();ctx.moveTo(a.x+w*.13,yy);ctx.quadraticCurveTo(a.x+w*.5,yy-3-phase,a.x+w*.87,yy);ctx.stroke()}
    if(y===0||terrain[y-1]?.[x]!==1)drawShoreFoam(x,y,{x:a.x,y:a.y},{x:b.x,y:a.y},time);
    if(y===MAP_H-1||terrain[y+1]?.[x]!==1)drawShoreFoam(x,y,{x:a.x,y:b.y},{x:b.x,y:b.y},time);
    if(x===0||terrain[y]?.[x-1]!==1)drawShoreFoam(x,y,{x:a.x,y:a.y},{x:a.x,y:b.y},time);
    if(x===MAP_W-1||terrain[y]?.[x+1]!==1)drawShoreFoam(x,y,{x:b.x,y:a.y},{x:b.x,y:b.y},time);
    if(hash2(x*3+7,y*5+11)>.78){const r=(time*7+hash2(x,y)*19)%18,alpha=(1-r/18)*.18;ctx.strokeStyle=`rgba(207,243,237,${alpha})`;ctx.lineWidth=1;ctx.beginPath();ctx.ellipse(a.x+w*(.25+hash2(x,3)*.5),a.y+h*(.25+hash2(5,y)*.5),r*renderZoom(),r*.42*renderZoom(),0,0,TAU);ctx.stroke()}
  }ctx.restore();
}

function drawFog(){
  const bounds=visibleTileBounds();ctx.save();
  for(let y=bounds.y0;y<=bounds.y1;y++)for(let x=bounds.x0;x<=bounds.x1;x++){
    const v=game.fog[y*MAP_W+x];if(v===2)continue;ctx.fillStyle=v===0?'rgba(2,6,10,.94)':'rgba(7,15,22,.5)';tileRectPath(x,y);ctx.fill();
  }ctx.restore();
}

function drawNightLighting(){
  const phase=(Math.sin(game.time/95*TAU-Math.PI/2)+1)/2,night=clamp(.25-phase*.3,0,.22);if(night<=0)return;
  ctx.save();const sky=ctx.createLinearGradient(0,0,viewW,viewH);sky.addColorStop(0,`rgba(17,29,58,${night*.82})`);sky.addColorStop(1,`rgba(5,14,35,${night})`);ctx.fillStyle=sky;ctx.fillRect(0,0,viewW,viewH);
  ctx.globalCompositeOperation='lighter';let lights=0;for(const b of game.entities){if(lights>=24||b.dead||b.kind!=='building'||b.construction<1||!visibleAt(b.x,b.y))continue;const p=worldToScreen(b.x,b.y);if(!onScreen(p,170))continue;const radius=(b.type==='wonder'?155:b.type==='town'?110:72)*Math.max(.8,renderZoom()),g=ctx.createRadialGradient(p.x,p.y,0,p.x,p.y,radius);g.addColorStop(0,`rgba(255,181,78,${night*.32})`);g.addColorStop(.35,`rgba(232,125,45,${night*.13})`);g.addColorStop(1,'rgba(0,0,0,0)');ctx.fillStyle=g;ctx.fillRect(p.x-radius,p.y-radius,radius*2,radius*2);lights++}ctx.restore();
}

function drawCombatFeedback(drawable,alpha){
  let danger=0;ctx.save();ctx.globalCompositeOperation='screen';
  for(const entry of drawable){
    const e=entry.e,age=e.lastHit>0?game.time-e.lastHit:99;if(age<0||age>.34)continue;const p=worldToScreen(entityWorldPosition(e,alpha).x,entityWorldPosition(e,alpha).y),t=clamp(age/.34,0,1),base=e.kind==='building'?Math.min(54,e.radius*.65):UNIT[e.type]?.role==='cavalry'?22:17,r=(base+t*18)*renderZoom();ctx.globalAlpha=1;drawGeneratedEffect(e.kind==='building'?'siegeExplosion':'arrowImpact',p.x,p.y,r*2.45,r*2.45,(1-t)*.42,e.id*.73);ctx.globalAlpha=(1-t)*.62;ctx.strokeStyle=e.faction===0?'#ff8177':'#ffe3aa';ctx.lineWidth=Math.max(1,2.6*(1-t));ctx.beginPath();ctx.arc(p.x,p.y,r,0,TAU);ctx.stroke();ctx.fillStyle=e.faction===0?'rgba(255,77,66,.13)':'rgba(255,224,151,.1)';ctx.beginPath();ctx.arc(p.x,p.y,r*.62,0,TAU);ctx.fill();if(e.faction===0)danger=Math.max(danger,1-t);
  }
  ctx.restore();if(danger<=0)return;ctx.save();const radius=Math.hypot(viewW,viewH)*.72,g=ctx.createRadialGradient(viewW*.5,viewH*.5,Math.min(viewW,viewH)*.32,viewW*.5,viewH*.5,radius);g.addColorStop(0,'rgba(120,12,10,0)');g.addColorStop(.72,`rgba(151,22,17,${danger*.025})`);g.addColorStop(1,`rgba(226,48,38,${danger*.14})`);ctx.fillStyle=g;ctx.fillRect(0,0,viewW,viewH);ctx.restore();
}

function drawTerrainCache(){
  if(!terrainCanvas?.width||!terrainCanvas?.height)return;ensureMedievalAtlas();if(medievalAtlasState===2&&(!medievalTerrainCache||medievalTerrainSource!==terrainCanvas||medievalTerrainMap!==terrain))buildMedievalTerrainCache();
  const source=medievalTerrainCache||terrainCanvas,z=renderZoom(),a=screenToWorld(0,0),b=screenToWorld(viewW,viewH),left=Math.min(a.x,b.x),top=Math.min(a.y,b.y),right=Math.max(a.x,b.x),bottom=Math.max(a.y,b.y),sx=clamp(left,0,source.width),sy=clamp(top,0,source.height),ex=clamp(right,0,source.width),ey=clamp(bottom,0,source.height);
  if(ex<=sx||ey<=sy)return;const dst=worldToScreen(sx,sy);ctx.imageSmoothingEnabled=true;ctx.imageSmoothingQuality='high';ctx.drawImage(source,sx,sy,ex-sx,ey-sy,dst.x,dst.y,(ex-sx)*z,(ey-sy)*z);
}

function render(alpha){
  if(!game||dom.game.classList.contains('hidden'))return;
  indexFrameActivity();ctx.setTransform(dpr,0,0,dpr,0,0);ctx.fillStyle='#071015';ctx.fillRect(0,0,viewW,viewH);drawTerrainCache();drawGroundLife();drawWaterHighlights();
  for(const m of game.markers)drawMarker(m);
  const scenery=[],units=[];
  for(const s of game.sites)if(exploredAt(s.x,s.y))scenery.push({e:s,kind:'site'});
  for(const n of game.nodes)if(!n.dead&&exploredAt(n.x,n.y))scenery.push({e:n,kind:'resource'});
  for(const e of game.entities){
    if(e.dead)continue;if(e.faction&&!visibleAt(e.x,e.y))continue;if(!e.faction&&!exploredAt(e.x,e.y))continue;
    const p=worldToScreen(e.x,e.y);if(!onScreen(p,e.kind==='building'?200:100))continue;(e.kind==='unit'?units:scenery).push({e,kind:e.kind});
  }
  frameVisibleUnits=units.length;
  scenery.sort((a,b)=>entityDepth(a.e)-entityDepth(b.e)||((a.e.id||0)-(b.e.id||0)));units.sort((a,b)=>entityDepth(a.e)-entityDepth(b.e)||((a.e.id||0)-(b.e.id||0)));
  const drawable=[...scenery,...units];for(const entry of drawable)drawEntityShadow(entry,alpha);for(const entry of drawable)drawSelectionBase(entry.e,alpha);
  for(const entry of scenery){if(entry.kind==='site')drawSite(entry.e);else if(entry.kind==='resource')drawNode(entry.e);else drawBuilding(entry.e)}
  /* Units always receive the final entity pass. Flat rooftops can therefore
   * never hide a unit, even where simulation footprints overlap. */
  for(const entry of units)drawUnit(entry.e,alpha);
  for(const p of game.projectiles)drawProjectile(p);for(const p of game.particles)drawParticle(p);drawAmbientMotes();
  drawNightLighting();drawFog();drawCombatFeedback(drawable,alpha);drawProgressOverlays(drawable,alpha);for(const entry of drawable)drawEntityHealth(entry.e,alpha);for(const entry of drawable)drawSelection(entry.e,alpha);drawStackHoverCue();
  if(mouse.down&&mouse.drag&&!mouse.pan){ctx.fillStyle='rgba(93,205,224,.13)';ctx.strokeStyle='#9ce8f1';ctx.lineWidth=1;const x=Math.min(mouse.startX,mouse.x),y=Math.min(mouse.startY,mouse.y),w=Math.abs(mouse.x-mouse.startX),h=Math.abs(mouse.y-mouse.startY);ctx.fillRect(x,y,w,h);ctx.strokeRect(x+.5,y+.5,w,h)}
  if(buildMode)drawBuildGhost();game.minimapTimer-=1/60;if(game.minimapTimer<=0){game.minimapTimer=.16;drawMinimap()}
}

function drawBuildGhost(){
  const d=BUILD[buildMode],p=screenToWorld(mouse.x,mouse.y),ok=validBuildAt(buildMode,p.x,p.y),screen=worldToScreen(p.x,p.y),z=renderZoom(),half=d.size;
  ctx.save();ctx.fillStyle=ok?'rgba(68,219,151,.27)':'rgba(239,81,76,.29)';ctx.strokeStyle=ok?'#9af0c2':'#ff9c96';ctx.lineWidth=2;worldRectPath(p.x,p.y,half,half*(buildMode==='wall'?.38:buildMode==='farm'?.7:1));ctx.fill();ctx.stroke();
  ctx.translate(screen.x,screen.y);ctx.globalAlpha=.58;ctx.scale(z,z);ctx.fillStyle=ok?'#78aa96':'#a9605d';if(buildMode==='tower'||buildMode==='wonder'){ctx.beginPath();ctx.arc(0,0,half*.72,0,TAU);ctx.fill()}else{roundedRect(ctx,-half*.7,-half*.56,half*1.4,half*1.12,7);ctx.fill()}ctx.restore();
  ctx.save();ctx.fillStyle='#fff';ctx.strokeStyle='rgba(3,8,11,.82)';ctx.lineWidth=3;ctx.font=`700 12px ${RENDER_FONT}`;ctx.textAlign='center';ctx.strokeText(d.name,screen.x,screen.y-half*z-15);ctx.fillText(d.name,screen.x,screen.y-half*z-15);ctx.restore();
}

function minimapProjectWorld(x,y,w,h){
  const scale=Math.min((w-8)/WORLD_W,(h-8)/WORLD_H),ox=(w-WORLD_W*scale)/2,oy=(h-WORLD_H*scale)/2;return{x:ox+x*scale,y:oy+y*scale,scale,ox,oy};
}

function drawMinimap(){
  const w=dom.minimap.clientWidth,h=dom.minimap.clientHeight;if(!w||!h)return;const pixelScale=Math.min(devicePixelRatio||1,2);
  if(dom.minimap.width!==Math.floor(w*pixelScale)||dom.minimap.height!==Math.floor(h*pixelScale)){dom.minimap.width=Math.floor(w*pixelScale);dom.minimap.height=Math.floor(h*pixelScale)}
  mctx.setTransform(pixelScale,0,0,pixelScale,0,0);mctx.fillStyle='#071014';mctx.fillRect(0,0,w,h);const fit=minimapProjectWorld(0,0,w,h),cellW=TILE*fit.scale+.25,cellH=TILE*fit.scale+.25;
  for(let y=0;y<MAP_H;y++)for(let x=0;x<MAP_W;x++){const fog=game.fog[y*MAP_W+x],p=minimapProjectWorld(x*TILE,y*TILE,w,h);mctx.fillStyle=!fog?'#04080b':terrain[y][x]===1?(fog===2?'#2e6870':'#172f34'):(fog===2?'#526d43':'#273625');mctx.fillRect(p.x,p.y,cellW,cellH)}
  for(const s of game.sites)if(exploredAt(s.x,s.y)){const p=minimapProjectWorld(s.x,s.y,w,h);mctx.fillStyle=factionColor(s.owner,'#e7c164');mctx.beginPath();mctx.arc(p.x,p.y,3,0,TAU);mctx.fill()}
  for(const e of game.entities){if(e.dead||(e.faction&&!visibleAt(e.x,e.y)))continue;const p=minimapProjectWorld(e.x,e.y,w,h),r=e.kind==='building'?3:1.8;mctx.fillStyle=teamColor(e);mctx.fillRect(p.x-r,p.y-r,r*2,r*2)}
  const tl=screenToWorld(0,0),br=screenToWorld(viewW,viewH),a=minimapProjectWorld(Math.min(tl.x,br.x),Math.min(tl.y,br.y),w,h),b=minimapProjectWorld(Math.max(tl.x,br.x),Math.max(tl.y,br.y),w,h);mctx.strokeStyle='#f6e4af';mctx.lineWidth=1;mctx.strokeRect(a.x+.5,a.y+.5,b.x-a.x,b.y-a.y);
}

function resize(){
  let center=null;if(game){try{center=screenToWorld(viewW/2,viewH/2)}catch{center=null}}
  viewW=innerWidth;viewH=innerHeight;dpr=Math.min(devicePixelRatio||1,2);dom.canvas.width=Math.floor(viewW*dpr);dom.canvas.height=Math.floor(viewH*dpr);dom.canvas.style.width=viewW+'px';dom.canvas.style.height=viewH+'px';if(game&&center)centerCamera(center.x,center.y);
}
