'use strict';

/*
 * Optional WebGL2 atmosphere pass. It never owns input and never replaces the
 * authoritative 2D renderer: unsupported browsers simply keep the canvas
 * transparent. The deliberately soft, low-resolution effects preserve unit
 * silhouettes and selection readability.
 */
(function initEmpireEffects(){
  const canQueryDocument=typeof document!=='undefined'&&typeof document.getElementById==='function';
  const canvas=canQueryDocument?document.getElementById('worldFx'):null;
  const gameScreen=canQueryDocument?document.getElementById('game'):null;
  const state={
    ready:false,enabled:true,destroyed:false,gl:null,program:null,vao:null,
    waterTexture:null,visibilityTexture:null,terrainRef:null,waterReady:0,visibilityReady:0,width:0,height:0,ratio:1,
    clock:0,lastFrame:performance.now(),gameRef:null,seenHits:new Map(),pulses:[]
  };

  const api={
    get available(){return state.ready},
    get enabled(){return state.enabled},
    setEnabled(value){state.enabled=!!value;if(!state.enabled)clearSurface()},
    pulse(x,y,intensity=1){addPulse(Number(x),Number(y),Number(intensity)||1)},
    resize(){resizeSurface(true)},
    destroy(){destroy()}
  };
  globalThis.EmpireFX=api;
  if(!canvas||!gameScreen)return;

  const vertexSource=`#version 300 es
    precision highp float;
    const vec2 VERTICES[3]=vec2[3](vec2(-1.0,-1.0),vec2(3.0,-1.0),vec2(-1.0,3.0));
    void main(){gl_Position=vec4(VERTICES[gl_VertexID],0.0,1.0);}`;

  const fragmentSource=`#version 300 es
    precision highp float;
    out vec4 outColor;
    uniform vec2 uResolution;
    uniform float uRatio;
    uniform float uTime;
    uniform float uGameTime;
    uniform float uMotion;
    uniform vec3 uCamera;
    uniform vec2 uWorldSize;
    uniform sampler2D uWater;
    uniform sampler2D uVisibility;
    uniform float uWaterReady;
    uniform float uVisibilityReady;
    uniform int uPulseCount;
    uniform vec4 uPulses[12];

    float hash21(vec2 p){
      p=fract(p*vec2(123.34,456.21));
      p+=dot(p,p+45.32);
      return fract(p.x*p.y);
    }

    float dustLayer(vec2 screen,float cell,float drift,float seed){
      vec2 grid=screen/cell;
      vec2 id=floor(grid);
      vec2 local=fract(grid);
      float n=hash21(id+seed);
      vec2 mote=vec2(hash21(id+17.7+seed),hash21(id+63.1-seed));
      mote.x=fract(mote.x+uTime*drift*(.35+n)*uMotion);
      mote.y=fract(mote.y-uTime*drift*.28*uMotion);
      float d=length(local-mote);
      return smoothstep(.052,.006,d)*(.38+.62*n);
    }

    void main(){
      vec2 frag=gl_FragCoord.xy;
      vec2 uv=frag/uResolution;
      vec2 screen=vec2(frag.x,uResolution.y-frag.y)/uRatio;
      vec2 world=uCamera.xy+screen/max(uCamera.z,.001);
      float day=.5+.5*sin(uGameTime/95.0*6.2831853-1.5707963);

      vec3 light=vec3(0.0);
      float lightAlpha=0.0;
      float vignette=smoothstep(.53,.98,length((uv-.5)*vec2(1.05,.88)));
      float edgeDark=vignette*.038;

      /* A restrained moving sun/moon wash makes the battlefield breathe. */
      float broadRay=.5+.5*sin((screen.x+screen.y*.42)*.0022-uTime*.055*uMotion);
      broadRay=pow(broadRay,5.0)*(1.0-vignette);
      vec3 sky=mix(vec3(.28,.48,.78),vec3(1.0,.72,.31),day);
      float skyAmount=.007+broadRay*(.012+.010*day);
      light+=sky*skyAmount;
      lightAlpha+=skyAmount;

      /* Procedural dust and pollen remain screen-soft and never mask units. */
      float dust=dustLayer(screen,92.0,.020,2.4)+dustLayer(screen+31.0,138.0,-.013,9.8);
      dust*=mix(.45,1.0,day)*(.010+.009*uMotion);
      light+=mix(vec3(.55,.73,.88),vec3(1.0,.82,.47),day)*dust;
      lightAlpha+=dust;

      /* Terrain-aware caustics: only water tiles receive animated highlights. */
      vec2 worldUv=world/max(uWorldSize,vec2(1.0));
      float inside=step(0.0,world.x)*step(0.0,world.y)*step(world.x,uWorldSize.x)*step(world.y,uWorldSize.y);
      float visibility=texture(uVisibility,clamp(worldUv,0.0,1.0)).r*uVisibilityReady;
      float water=texture(uWater,clamp(worldUv,0.0,1.0)).r*uWaterReady*inside*visibility;
      float wave=sin(world.x*.105+uTime*1.35*uMotion)+sin(world.y*.082-uTime*.94*uMotion)+sin((world.x+world.y)*.049+uTime*.68*uMotion);
      float caustic=pow(clamp(wave/3.0*.5+.5,0.0,1.0),8.0);
      float waterGlow=water*(.006+caustic*.035)*(.55+.45*uMotion);
      light+=mix(vec3(.22,.69,.88),vec3(.48,.86,.88),day)*waterGlow;
      lightAlpha+=waterGlow;

      /* Impacts, missiles and damaged structures become soft bloom emitters. */
      for(int i=0;i<12;i++){
        if(i>=uPulseCount)break;
        vec4 pulse=uPulses[i];
        vec2 delta=frag-pulse.xy;
        float d=length(delta)/max(uRatio,.001);
        float glow=0.0;
        float ring=0.0;
        vec3 tone=vec3(1.0,.49,.17);
        if(pulse.w<-.5){
          float missile=exp(-d*d/42.0)*pulse.z;
          glow=missile*.15;
          tone=vec3(1.0,.82,.38);
        }else{
          float age=clamp(pulse.w,0.0,1.0);
          float radius=mix(7.0,78.0,age)*(.75+.25*pulse.z);
          ring=exp(-pow((d-radius)/(3.2+age*9.0),2.0))*(1.0-age)*uMotion;
          glow=exp(-d/(20.0+35.0*pulse.z))*(1.0-age);
          float ripple=.5+.5*sin(d*.26-uTime*5.0*uMotion);
          ring*=.72+.28*ripple;
        }
        float battle=(glow*.13+ring*.11)*pulse.z;
        light+=tone*battle;
        lightAlpha+=battle;
      }

      float alpha=clamp(edgeDark+lightAlpha,0.0,.24);
      vec3 color=light/max(alpha,.0001);
      outColor=vec4(clamp(color,0.0,1.0),alpha);
    }`;

  function compile(gl,type,source){
    const shader=gl.createShader(type);gl.shaderSource(shader,source);gl.compileShader(shader);
    if(!gl.getShaderParameter(shader,gl.COMPILE_STATUS)){gl.deleteShader(shader);return null}
    return shader;
  }

  function makeProgram(gl){
    const vertex=compile(gl,gl.VERTEX_SHADER,vertexSource),fragment=compile(gl,gl.FRAGMENT_SHADER,fragmentSource);
    if(!vertex||!fragment){if(vertex)gl.deleteShader(vertex);if(fragment)gl.deleteShader(fragment);return null}
    const program=gl.createProgram();gl.attachShader(program,vertex);gl.attachShader(program,fragment);gl.linkProgram(program);
    gl.deleteShader(vertex);gl.deleteShader(fragment);
    if(!gl.getProgramParameter(program,gl.LINK_STATUS)){gl.deleteProgram(program);return null}
    return program;
  }

  function clearSurface(){
    if(!state.gl)return;
    state.gl.viewport(0,0,canvas.width,canvas.height);
    state.gl.clearColor(0,0,0,0);state.gl.clear(state.gl.COLOR_BUFFER_BIT);
  }

  function resizeSurface(force=false){
    if(!state.gl)return;
    const cssWidth=Math.max(1,gameScreen.clientWidth||innerWidth||1);
    const cssHeight=Math.max(1,gameScreen.clientHeight||innerHeight||1);
    const pixelBudgetRatio=Math.sqrt(2300000/(cssWidth*cssHeight));
    const ratio=Math.max(.55,Math.min(devicePixelRatio||1,1.25,pixelBudgetRatio));
    const width=Math.max(1,Math.round(cssWidth*ratio)),height=Math.max(1,Math.round(cssHeight*ratio));
    if(!force&&width===state.width&&height===state.height)return;
    state.width=canvas.width=width;state.height=canvas.height=height;state.ratio=ratio;
    canvas.style.width=cssWidth+'px';canvas.style.height=cssHeight+'px';
    state.gl.viewport(0,0,width,height);
  }

  function updateWaterTexture(){
    let map=null,mapWidth=0,mapHeight=0;
    try{
      if(typeof terrain!=='undefined'&&terrain?.length&&terrain[0]?.length){map=terrain;mapWidth=terrain[0].length;mapHeight=terrain.length}
    }catch{return}
    if(!map||map===state.terrainRef)return;
    const pixels=new Uint8Array(mapWidth*mapHeight);
    for(let y=0;y<mapHeight;y++)for(let x=0;x<mapWidth;x++)pixels[y*mapWidth+x]=map[y][x]===1?255:0;
    const gl=state.gl;gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,state.waterTexture);gl.pixelStorei(gl.UNPACK_ALIGNMENT,1);
    gl.texImage2D(gl.TEXTURE_2D,0,gl.R8,mapWidth,mapHeight,0,gl.RED,gl.UNSIGNED_BYTE,pixels);
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
    state.terrainRef=map;state.waterReady=1;
  }

  function updateVisibilityTexture(activeGame){
    let mapWidth=0,mapHeight=0,tileFog=activeGame?.fog;
    try{mapWidth=typeof MAP_W!=='undefined'?MAP_W:0;mapHeight=typeof MAP_H!=='undefined'?MAP_H:0}catch{}
    if(!tileFog||!mapWidth||!mapHeight||tileFog.length!==mapWidth*mapHeight){state.visibilityReady=0;return}
    const pixels=new Uint8Array(tileFog.length);for(let i=0;i<tileFog.length;i++)pixels[i]=tileFog[i]===2?255:0;
    const gl=state.gl;gl.activeTexture(gl.TEXTURE1);gl.bindTexture(gl.TEXTURE_2D,state.visibilityTexture);gl.pixelStorei(gl.UNPACK_ALIGNMENT,1);
    gl.texImage2D(gl.TEXTURE_2D,0,gl.R8,mapWidth,mapHeight,0,gl.RED,gl.UNSIGNED_BYTE,pixels);
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);state.visibilityReady=1;
  }

  function pointVisible(activeGame,x,y){
    try{
      const tileSize=typeof TILE!=='undefined'?TILE:48,mapWidth=typeof MAP_W!=='undefined'?MAP_W:0,mapHeight=typeof MAP_H!=='undefined'?MAP_H:0,tx=Math.floor(x/tileSize),ty=Math.floor(y/tileSize);
      return !!mapWidth&&!!mapHeight&&tx>=0&&ty>=0&&tx<mapWidth&&ty<mapHeight&&activeGame?.fog?.[ty*mapWidth+tx]===2;
    }catch{return false}
  }

  function addPulse(x,y,intensity=1){
    if(!Number.isFinite(x)||!Number.isFinite(y))return;
    state.pulses.push({x,y,intensity:Math.max(.25,Math.min(2.2,intensity)),start:state.clock,life:1.15});
    if(state.pulses.length>24)state.pulses.splice(0,state.pulses.length-24);
  }

  function scanCombat(activeGame){
    if(activeGame!==state.gameRef){state.gameRef=activeGame;state.seenHits.clear();state.pulses.length=0;state.terrainRef=null;state.waterReady=0}
    for(const entity of activeGame.entities||[]){
      if(!(entity.lastHit>0))continue;
      const previous=state.seenHits.get(entity.id);
      if(previous===entity.lastHit)continue;
      state.seenHits.set(entity.id,entity.lastHit);
      if(!pointVisible(activeGame,entity.x,entity.y))continue;
      addPulse(entity.x,entity.y,entity.kind==='building'?1.55:.85);
    }
    state.pulses=state.pulses.filter(p=>state.clock-p.start<p.life);
  }

  function cameraValues(activeGame,cssWidth,cssHeight){
    const camera=activeGame.camera||{},zoom=Math.max(.001,Number(camera.zoom)||1);
    return{
      left:Number.isFinite(camera.px)?camera.px:(Number(camera.x)||0)-cssWidth/(2*zoom),
      top:Number.isFinite(camera.py)?camera.py:(Number(camera.y)||0)-cssHeight/(2*zoom),zoom
    };
  }

  function gatherEmitters(activeGame,camera){
    const values=[];
    const add=(x,y,strength,age)=>{
      if(!pointVisible(activeGame,x,y))return;
      const sx=(x-camera.left)*camera.zoom,sy=(y-camera.top)*camera.zoom;
      if(sx<-110||sy<-110||sx>state.width/state.ratio+110||sy>state.height/state.ratio+110)return;
      values.push(sx*state.ratio,state.height-sy*state.ratio,strength,age);
    };
    const live=state.pulses.slice().sort((a,b)=>b.intensity-a.intensity);
    for(const p of live){if(values.length>=48)break;add(p.x,p.y,p.intensity,(state.clock-p.start)/p.life)}
    for(const p of activeGame.projectiles||[]){if(values.length>=48)break;if(!p.dead)add(p.x,p.y,p.siege?.5:.28,-1)}
    if(values.length<48){
      for(const b of activeGame.entities||[]){
        if(values.length>=48)break;
        if(b.dead||b.kind!=='building'||b.hp>=b.maxHp*.34)continue;
        add(b.x,b.y,.35,-1);
      }
    }
    return values;
  }

  function drawFrame(now){
    if(state.destroyed)return;
    const dt=Math.min(.1,Math.max(0,(now-state.lastFrame)/1000));state.lastFrame=now;
    let activeGame=null;
    try{if(typeof game!=='undefined')activeGame=game}catch{}
    const visible=activeGame&&state.enabled&&!gameScreen.classList.contains('hidden');
    if(!visible){clearSurface();requestAnimationFrame(drawFrame);return}
    state.clock+=dt*(activeGame.paused?0:(Number(activeGame.speed)||1));
    resizeSurface();updateWaterTexture();updateVisibilityTexture(activeGame);scanCombat(activeGame);
    const gl=state.gl,program=state.program,cssWidth=state.width/state.ratio,cssHeight=state.height/state.ratio;
    const camera=cameraValues(activeGame,cssWidth,cssHeight),emitters=gatherEmitters(activeGame,camera);
    let motion=1;try{if(typeof reducedMotion!=='undefined'&&reducedMotion)motion=.18}catch{}
    gl.viewport(0,0,state.width,state.height);gl.clearColor(0,0,0,0);gl.clear(gl.COLOR_BUFFER_BIT);gl.useProgram(program);gl.bindVertexArray(state.vao);
    gl.uniform2f(gl.getUniformLocation(program,'uResolution'),state.width,state.height);
    gl.uniform1f(gl.getUniformLocation(program,'uRatio'),state.ratio);
    gl.uniform1f(gl.getUniformLocation(program,'uTime'),state.clock);
    gl.uniform1f(gl.getUniformLocation(program,'uGameTime'),Number(activeGame.time)||0);
    gl.uniform1f(gl.getUniformLocation(program,'uMotion'),motion);
    gl.uniform3f(gl.getUniformLocation(program,'uCamera'),camera.left,camera.top,camera.zoom);
    let worldWidth=1,worldHeight=1;
    try{worldWidth=typeof WORLD_W!=='undefined'?WORLD_W:1;worldHeight=typeof WORLD_H!=='undefined'?WORLD_H:1}catch{}
    gl.uniform2f(gl.getUniformLocation(program,'uWorldSize'),worldWidth,worldHeight);
    gl.uniform1f(gl.getUniformLocation(program,'uWaterReady'),state.waterReady);
    gl.uniform1f(gl.getUniformLocation(program,'uVisibilityReady'),state.visibilityReady);
    gl.uniform1i(gl.getUniformLocation(program,'uWater'),0);
    gl.uniform1i(gl.getUniformLocation(program,'uVisibility'),1);
    gl.uniform1i(gl.getUniformLocation(program,'uPulseCount'),emitters.length/4);
    if(emitters.length){
      const padded=new Float32Array(48);padded.set(emitters.slice(0,48));
      gl.uniform4fv(gl.getUniformLocation(program,'uPulses[0]'),padded);
    }
    gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,state.waterTexture);gl.activeTexture(gl.TEXTURE1);gl.bindTexture(gl.TEXTURE_2D,state.visibilityTexture);gl.drawArrays(gl.TRIANGLES,0,3);
    requestAnimationFrame(drawFrame);
  }

  function destroy(){
    if(state.destroyed)return;state.destroyed=true;
    const gl=state.gl;if(gl){if(state.waterTexture)gl.deleteTexture(state.waterTexture);if(state.visibilityTexture)gl.deleteTexture(state.visibilityTexture);if(state.vao)gl.deleteVertexArray(state.vao);if(state.program)gl.deleteProgram(state.program)}
    state.ready=false;clearSurface();
  }

  try{
    const gl=canvas.getContext('webgl2',{alpha:true,antialias:false,depth:false,stencil:false,premultipliedAlpha:false,preserveDrawingBuffer:false,powerPreference:'high-performance'});
    if(!gl){canvas.hidden=true;return}
    const program=makeProgram(gl);if(!program){canvas.hidden=true;return}
    state.gl=gl;state.program=program;state.vao=gl.createVertexArray();state.waterTexture=gl.createTexture();state.visibilityTexture=gl.createTexture();
    gl.bindVertexArray(state.vao);gl.disable(gl.DEPTH_TEST);gl.disable(gl.CULL_FACE);gl.disable(gl.BLEND);
    gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,state.waterTexture);
    gl.texImage2D(gl.TEXTURE_2D,0,gl.R8,1,1,0,gl.RED,gl.UNSIGNED_BYTE,new Uint8Array([0]));
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR);
    gl.activeTexture(gl.TEXTURE1);gl.bindTexture(gl.TEXTURE_2D,state.visibilityTexture);
    gl.texImage2D(gl.TEXTURE_2D,0,gl.R8,1,1,0,gl.RED,gl.UNSIGNED_BYTE,new Uint8Array([0]));
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.NEAREST);
    resizeSurface(true);state.ready=true;requestAnimationFrame(drawFrame);
  }catch{canvas.hidden=true}
})();
