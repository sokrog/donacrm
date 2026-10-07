// Assemble generated lettering with byte-identical original brand PNGs.
// ImageGen draws lettering; native SVG preserves original symbols and fixes ink color.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {chromium}=require('C:/Users/Sokrog/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const root=path.resolve(__dirname,'../..'),ids=['bow','cherry','kiss','tulip','swan'];
const uri=file=>'data:image/png;base64,'+fs.readFileSync(file).toString('base64');
const sha=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage(),records=[];
  for(const id of ids){
   const symbolPath=path.join(root,'assets',id+'.png'),letterPath=path.join(__dirname,'lettering-v4',id+'.png');
   const symbol=uri(symbolPath),lettering=uri(letterPath);
   const meta=await page.evaluate(async sources=>{
    const result=[];
    for(const src of sources){
     const img=new Image();img.src=src;await img.decode();
     const canvas=document.createElement('canvas');canvas.width=img.width;canvas.height=img.height;
     const ctx=canvas.getContext('2d');ctx.drawImage(img,0,0);const data=ctx.getImageData(0,0,img.width,img.height).data;
     let left=img.width,top=img.height,right=0,bottom=0;
     for(let y=0;y<img.height;y++)for(let x=0;x<img.width;x++)if(data[(y*img.width+x)*4+3]>16){left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);}
     result.push({w:img.width,h:img.height,x:left,y:top,bw:right-left+1,bh:bottom-top+1});
    }return result;
   },[symbol,lettering]);
   const [s,l]=meta;
   if(l.bw/l.w>.99&&l.bh/l.h>.99)throw new Error(id+': lettering needs transparent background');
   // Coordinates refer to visible ink, keeping source image content unmodified.
   const side=id==='tulip';
   const word={x:side?760:260,y:side?650:820,w:side?2040:2480};
   const scale=word.w/l.bw;word.h=l.bh*scale;
   if(word.h>510){word.w*=510/word.h;word.h=510;word.x=side?760:(3000-word.w)/2;}
   const markBox=id==='kiss'?{x:2480,y:570,w:270,h:245}:id==='bow'?{x:300,y:185,w:580,h:565}:id==='cherry'?{x:1210,y:170,w:540,h:570}:id==='tulip'?{x:225,y:265,w:490,h:970}:{x:1120,y:160,w:750,h:600};
   const ms=Math.min(markBox.w/s.bw,markBox.h/s.bh),mx=markBox.x+(markBox.w-s.bw*ms)/2,my=markBox.y+(markBox.h-s.bh*ms)/2;
   const ls=word.w/l.bw;
   const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="3000" height="1500" viewBox="0 0 3000 1500"><title>DONAmour — ${id} — variant 4</title><rect width="3000" height="1500" fill="#FFE9EC"/><defs><filter id="solid-ink" color-interpolation-filters="sRGB" x="-1%" y="-1%" width="102%" height="102%"><feComponentTransfer in="SourceAlpha" result="ink-alpha"><feFuncA type="table" tableValues="0 0 1 1"/></feComponentTransfer><feFlood flood-color="#66021F"/><feComposite in2="ink-alpha" operator="in"/></filter></defs><image data-role="original-symbol" href="${symbol}" x="${mx-s.x*ms}" y="${my-s.y*ms}" width="${s.w*ms}" height="${s.h*ms}"/><image data-role="lettering" filter="url(#solid-ink)" href="${lettering}" x="${word.x-l.x*ls}" y="${word.y-l.y*ls}" width="${l.w*ls}" height="${l.h*ls}"/></svg>`;
   fs.writeFileSync(path.join(__dirname,`donamour-${id}-v4.svg`),svg);
   const rendered=await page.evaluate(async svg=>{
    const img=new Image();img.src='data:image/svg+xml;base64,'+btoa(unescape(encodeURIComponent(svg)));await img.decode();
    const c=document.createElement('canvas');c.width=3000;c.height=1500;const ctx=c.getContext('2d');ctx.drawImage(img,0,0);
    const png=c.toDataURL('image/png').split(',')[1];
    const small=document.createElement('canvas');small.width=1200;small.height=600;small.getContext('2d').drawImage(c,0,0,1200,600);
    return {png,preview:small.toDataURL('image/webp',.94).split(',')[1]};
   },svg);
   fs.writeFileSync(path.join(root,'assets',`donamour-${id}-v4.png`),Buffer.from(rendered.png,'base64'));
   fs.writeFileSync(path.join(root,'previews',`donamour-${id}-v4.webp`),Buffer.from(rendered.preview,'base64'));
   records.push({id,originalSymbol:`assets/${id}.png`,sha256:sha(symbolPath),lettering:`lettering-v4/${id}.png`,ink:'#66021F',background:'#FFE9EC',sourceSymbolEmbeddedUnchanged:true,word,markBox});
  }
  fs.writeFileSync(path.join(__dirname,'composition-v4.json'),JSON.stringify(records,null,2));
  console.log('Five variant-4 PNG/SVG compositions: unchanged original symbols, fixed solid ruby lettering.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
