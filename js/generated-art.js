'use strict';

/*
 * Offline loader for the Imagegen sprite atlases.  Nothing in this module needs
 * the DOM: when Image is unavailable (for example in the Node smoke suite),
 * getUnitSprite simply returns null and the renderer keeps its vector fallback.
 */
(function installGeneratedArt(root){
  const ATLASES={
    common:{src:'assets/generated/units-common.png',cols:3,rows:3,image:null,state:'idle'},
    uniqueA:{src:'assets/generated/units-unique-a.png',cols:4,rows:2,image:null,state:'idle'},
    uniqueB:{src:'assets/generated/units-unique-b.png',cols:3,rows:2,image:null,state:'idle'},
    buildingsCommon:{src:'assets/generated/buildings-common.png',cols:4,rows:2,image:null,state:'idle'},
    buildingsAdvanced:{src:'assets/generated/buildings-advanced.png',cols:3,rows:2,image:null,state:'idle'},
    environment:{src:'assets/generated/environment.png',cols:4,rows:2,image:null,state:'idle'},
    effectsUi:{src:'assets/generated/effects-ui.png',cols:4,rows:4,image:null,state:'idle'}
  };

  const UNIT_SPRITES=Object.freeze({
    villager:{atlas:'common',col:0,row:0,height:70},
    scout:{atlas:'common',col:1,row:0,height:84},
    swordsman:{atlas:'common',col:2,row:0,height:70},
    spear:{atlas:'common',col:0,row:1,height:74},
    archer:{atlas:'common',col:1,row:1,height:72},
    cavalry:{atlas:'common',col:2,row:1,height:86},
    crossbow:{atlas:'common',col:0,row:2,height:72},
    ram:{atlas:'common',col:1,row:2,height:82},
    catapult:{atlas:'common',col:2,row:2,height:82},

    longbowman:{atlas:'uniqueA',col:0,row:0,height:88},
    cataphract:{atlas:'uniqueA',col:1,row:0,height:94},
    woadRaider:{atlas:'uniqueA',col:2,row:0,height:84},
    chuKoNu:{atlas:'uniqueA',col:3,row:0,height:86},
    throwingAxeman:{atlas:'uniqueA',col:0,row:1,height:84},
    huskarl:{atlas:'uniqueA',col:1,row:1,height:84},
    samurai:{atlas:'uniqueA',col:2,row:1,height:88},

    mangudai:{atlas:'uniqueB',col:0,row:0,height:96},
    warElephant:{atlas:'uniqueB',col:1,row:0,height:112},
    mameluke:{atlas:'uniqueB',col:2,row:0,height:98},
    teutonicKnight:{atlas:'uniqueB',col:0,row:1,height:90},
    janissary:{atlas:'uniqueB',col:1,row:1,height:90},
    berserk:{atlas:'uniqueB',col:2,row:1,height:88}
  });

  const BUILDING_SPRITES=Object.freeze({
    town:{atlas:'buildingsCommon',col:0,row:0,height:144},
    house:{atlas:'buildingsCommon',col:1,row:0,height:110},
    mill:{atlas:'buildingsCommon',col:2,row:0,height:110},
    lumber:{atlas:'buildingsCommon',col:3,row:0,height:98},
    farm:{atlas:'buildingsCommon',col:0,row:1,height:88},
    barracks:{atlas:'buildingsCommon',col:1,row:1,height:116},
    blacksmith:{atlas:'buildingsCommon',col:2,row:1,height:112},
    range:{atlas:'buildingsCommon',col:3,row:1,height:102},
    stable:{atlas:'buildingsAdvanced',col:0,row:0,height:112},
    tower:{atlas:'buildingsAdvanced',col:1,row:0,height:120},
    wall:{atlas:'buildingsAdvanced',col:2,row:0,height:76},
    castle:{atlas:'buildingsAdvanced',col:0,row:1,height:152},
    workshop:{atlas:'buildingsAdvanced',col:1,row:1,height:130},
    wonder:{atlas:'buildingsAdvanced',col:2,row:1,height:160}
  });

  const ENVIRONMENT_SPRITES=Object.freeze({
    oak:{atlas:'environment',col:0,row:0,height:96},
    pine:{atlas:'environment',col:1,row:0,height:100},
    food:{atlas:'environment',col:2,row:0,height:70},
    gold:{atlas:'environment',col:3,row:0,height:76},
    stone:{atlas:'environment',col:0,row:1,height:76},
    site:{atlas:'environment',col:1,row:1,height:92},
    construction:{atlas:'environment',col:2,row:1,height:112},
    campfire:{atlas:'environment',col:3,row:1,height:74}
  });

  const EFFECT_SPRITES=Object.freeze({
    swordSlash:{atlas:'effectsUi',col:0,row:0,height:72},
    arrowImpact:{atlas:'effectsUi',col:1,row:0,height:72},
    dust:{atlas:'effectsUi',col:2,row:0,height:72},
    siegeExplosion:{atlas:'effectsUi',col:3,row:0,height:84},
    embers:{atlas:'effectsUi',col:0,row:1,height:72},
    healAura:{atlas:'effectsUi',col:1,row:1,height:72},
    selectionRing:{atlas:'effectsUi',col:2,row:1,height:72},
    waterRipple:{atlas:'effectsUi',col:3,row:1,height:72},
    foodIcon:{atlas:'effectsUi',col:0,row:2,height:48},
    woodIcon:{atlas:'effectsUi',col:1,row:2,height:48},
    goldIcon:{atlas:'effectsUi',col:2,row:2,height:48},
    stoneIcon:{atlas:'effectsUi',col:3,row:2,height:48},
    houseIcon:{atlas:'effectsUi',col:0,row:3,height:48},
    castleIcon:{atlas:'effectsUi',col:1,row:3,height:48},
    ageIcon:{atlas:'effectsUi',col:2,row:3,height:48},
    powerIcon:{atlas:'effectsUi',col:3,row:3,height:48}
  });

  function loadAtlas(atlas){
    if(atlas.state!=='idle'||typeof root.Image!=='function')return;
    atlas.state='loading';
    try{
      const image=new root.Image();
      atlas.image=image;
      image.decoding='async';
      image.addEventListener('load',()=>{atlas.state='ready'},{once:true});
      image.addEventListener('error',()=>{atlas.state='error';atlas.image=null},{once:true});
      image.src=atlas.src;
    }catch{
      atlas.state='error';atlas.image=null;
    }
  }

  function preload(){
    if(typeof root.Image!=='function')return false;
    Object.values(ATLASES).forEach(loadAtlas);
    return true;
  }

  function getMappedSprite(table,type){
    const mapping=table[type],atlas=mapping&&ATLASES[mapping.atlas];
    if(!mapping||!atlas||atlas.state!=='ready'||!atlas.image)return null;
    const image=atlas.image,width=image.naturalWidth||image.width,height=image.naturalHeight||image.height;
    if(!width||!height)return null;

    /* Rounded proportional boundaries also support the 1402 px four-column
       atlas.  A two-pixel inset prevents bilinear sampling from a neighbour. */
    const x0=Math.round(mapping.col*width/atlas.cols),x1=Math.round((mapping.col+1)*width/atlas.cols);
    const y0=Math.round(mapping.row*height/atlas.rows),y1=Math.round((mapping.row+1)*height/atlas.rows);
    const inset=2,sx=x0+inset,sy=y0+inset,sw=Math.max(1,x1-x0-inset*2),sh=Math.max(1,y1-y0-inset*2);
    return{image,sx,sy,sw,sh,width:mapping.height*sw/sh,height:mapping.height};
  }

  function getUnitSprite(type){return getMappedSprite(UNIT_SPRITES,type)}
  function getBuildingSprite(type){return getMappedSprite(BUILDING_SPRITES,type)}
  function getEnvironmentSprite(type){return getMappedSprite(ENVIRONMENT_SPRITES,type)}
  function getEffectSprite(type){return getMappedSprite(EFFECT_SPRITES,type)}

  function atlasState(){
    return Object.fromEntries(Object.entries(ATLASES).map(([key,value])=>[key,value.state]));
  }

  root.GeneratedArt=Object.freeze({
    getUnitSprite,getBuildingSprite,getEnvironmentSprite,getEffectSprite,preload,atlasState,
    unitMapping:UNIT_SPRITES,buildingMapping:BUILDING_SPRITES,environmentMapping:ENVIRONMENT_SPRITES,effectMapping:EFFECT_SPRITES
  });
  preload();
})(typeof globalThis!=='undefined'?globalThis:this);
