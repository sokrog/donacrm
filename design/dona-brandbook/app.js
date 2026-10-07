(async () => {
  'use strict';
  const names = [
    ['DONA','ЛАКОНИЧНО','Короткое имя с выразительной модной типографикой.','ДОНА'],
    ['Dona Muse','ВДОХНОВЕНИЕ','Личный стиль, искусство сочетаний и мягкая уверенность.','ДОНА МЬЮЗ'],
    ['Dona Belle','ЖЕНСТВЕННОСТЬ','Мелодичное имя для женственного современного гардероба.','ДОНА БЕЛЬ'],
    ['Belle Dona','ЭЛЕГАНТНОСТЬ','Чуть более французская интонация и плавный ритм.','БЕЛЬ ДОНА']
  ];
  const symbols = {
    bow:{name:'Шёлковый бант',hint:'Живые складки шёлковой ленты, мягкий узел и свободные концы. Тёплый жест на упаковке.'},
    cherry:{name:'Вишня',hint:'Две ягоды на изящных плодоножках. Ботанический рисунок с небольшим игривым акцентом.'},
    kiss:{name:'Отпечаток помады',hint:'Фактурный след рубиновой помады: естественные пропуски пигмента и тонкий рельеф губ.'},
    tulip:{name:'Тюльпан',hint:'Ботаническая форма: чашевидные лепестки, пластичный стебель и вытянутые листья.'},
    swan:{name:'Лебедь',hint:'Длинная изогнутая шея, точные пропорции и мягкий рисунок перьев. Пластика женственного силуэта.'}
  };
  let symbolSequence=0;
  function symbolBody(id){
    const uid='ink-'+(++symbolSequence),src=window.DONA_MEDIA[id];
    return `<defs><filter id="${uid}-density"><feColorMatrix type="matrix" values="0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 -.2126 -.7152 -.0722 1 0"/></filter><mask id="${uid}" style="mask-type:alpha" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><image filter="url(#${uid}-density)" href="${src}" x="0" y="0" width="100" height="100" preserveAspectRatio="xMidYMid meet"/></mask></defs><rect width="100" height="100" fill="currentColor" stroke="none" mask="url(#${uid})"/>`;
  }
  function placementOn(w,h,placement=state.brandPlacement){
    const size=Math.min(w,h)*placement.size/100,r=placement.angle*Math.PI/180;
    const margin=size*(Math.abs(Math.cos(r))+Math.abs(Math.sin(r)))/2+Math.min(w,h)*.012;
    return {size,x:Math.max(margin,Math.min(w-margin,w*placement.x/100)),y:Math.max(margin,Math.min(h-margin,h*placement.y/100)),angle:placement.angle};
  }
  function placedSymbol(w,h,placement=state.brandPlacement,id=state.symbol){
    const a=placementOn(w,h,placement);
    return `<g data-placement-mark transform="translate(${a.x} ${a.y}) rotate(${a.angle}) scale(${a.size/100}) translate(-50 -50)" fill="none" stroke="currentColor" stroke-width="1.35" stroke-linecap="round" stroke-linejoin="round">${symbolBody(id)}</g>`;
  }

  // Symbol-only slots inherit rotation, never composition offsets or scale.
  function standaloneSymbol(id=state.symbol){
    const angle=state.brandPlacement?.angle||0,r=angle*Math.PI/180;
    const size=80/(Math.abs(Math.cos(r))+Math.abs(Math.sin(r)));
    return `<g data-standalone-symbol transform="translate(50 50) rotate(${angle}) scale(${size/100}) translate(-50 -50)">${symbolBody(id)}</g>`;
  }
  function proportionalLockup(ctx,label){
    const q=state.brandPlacement,formats={post:[1080,1350],story:[1080,1920],card:[1600,1000],avatar:[1080,1080]};
    const [fw,fh]=formats[q.format||'card'],w=1000,h=w*fh/fw,a=placementOn(w,h,q);
    const measured=ctx.measureText(state.name).width||100,size=Math.min(90,780/measured*100),textWidth=measured*size/100,baseline=h*.54;
    const r=a.angle*Math.PI/180,extent=a.size*(Math.abs(Math.cos(r))+Math.abs(Math.sin(r)))/2;
    const left=Math.min(a.x-extent,500-textWidth/2),right=Math.max(a.x+extent,500+textWidth/2);
    const top=Math.min(a.y-extent,baseline-size),bottom=Math.max(a.y+extent,baseline+size*.25);
    const scale=Math.min(900/(right-left),280/(bottom-top));
    return {viewBox:`${(1000-(right-left)*scale)/2-18} 12 ${(right-left)*scale+36} 316`,body:`<g data-proportional-lockup transform="translate(${(1000-(right-left)*scale)/2} ${(340-(bottom-top)*scale)/2}) scale(${scale}) translate(${-left} ${-top})">${placedSymbol(w,h,q)}${label(state.name,500,baseline,size)}</g>`};
  }

  const fonts = [
    {id:'cormorant',name:'Cormorant Garamond · couture',family:'"Cormorant Garamond", Georgia, serif',hint:'Скульптурные засечки и высокая пластика. Латиница и кириллица. Пара: Manrope.'},
    {id:'prata',name:'Prata · fashion editorial',family:'Prata, Georgia, serif',hint:'Выразительный контраст тонких и широких штрихов. Латиница и кириллица. Пара: Manrope.'},
    {id:'manrope',name:'Manrope · городской',family:'Manrope, Arial, sans-serif',hint:'Геометрия с мягким характером. Латиница и кириллица. Хорош для социальных сетей.'},
    {id:'greatvibes',name:'Great Vibes · каллиграфия',family:'"Great Vibes", "Marck Script", cursive',hint:'Тонкая каллиграфическая подпись. Латиница — Great Vibes, кириллица — Marck Script.'},
    {id:'marck',name:'Marck Script · личная подпись',family:'"Marck Script", cursive',hint:'Живая рукописная линия. Латиница и кириллица. Используйте в логотипе, а не в мелком тексте.'},
    {id:'editorial',name:'Georgia · редакционная',family:'Georgia, "Times New Roman", serif',hint:'Контрастная антиква. Спокойная элегантность и читаемая кириллица.'},
    {id:'classic',name:'Palatino · мягкая классика',family:'"Palatino Linotype", "Book Antiqua", Palatino, serif',hint:'Пластичные, широкие буквы. Мягкий характер ателье.'},
    {id:'modern',name:'Segoe UI · современная',family:'"Segoe UI", Arial, sans-serif',hint:'Чистый гротеск. Уверенный городской минимализм.'},
    {id:'literary',name:'Times New Roman · литературная',family:'"Times New Roman", Times, serif',hint:'Тонкая классическая форма. Личная, почти книжная интонация.'}
  ];
  const seasonalPalettes={
    summer:[['Ruby / Rose Ivory','#66021F','#FFE9EC','#E8BBC7'],['Cherry / Cream','#791A2D','#FFF3E8','#F1C5C9'],['Burgundy / Petal','#630625','#FCE6ED','#F5B7BD'],['Rosewood / Milk','#7C394A','#FFF4F1','#DEB8C0']],
    winter:[['Ruby / Porcelain','#51001A','#F6E7E9','#BE8B9D'],['Cherry / Cashmere','#581020','#F3E7DC','#C89B9F'],['Burgundy / Frost','#43041C','#F4E3EB','#C993AA'],['Rosewood / Pearl','#542B39','#F2E9E6','#B9949E']]
  };
  const concepts=[
    {name:'Рубиновая подпись',en:'RUBY SIGNATURE',description:'Антиква и живой след помады.',symbol:'kiss',brand:'DONA',font:'prata',caption:'Женственность с характером',tagline:'Ваш стиль. Ваша подпись.'},
    {name:'Шёлковое письмо',en:'SILK LETTER',description:'Лента, кремовая бумага и тёплое внимание.',symbol:'bow',brand:'Dona Muse',font:'cormorant',caption:'Для особенных обычных дней',tagline:'Нежность в каждой детали.'},
    {name:'Вишнёвый акцент',en:'CHERRY NOTE',description:'Глубокий ягодный цвет и лёгкая игра.',symbol:'cherry',brand:'Dona Belle',font:'prata',caption:'Одежда в вашем ритме',tagline:'Влюбиться в свой образ.'},
    {name:'Тихая романтика',en:'QUIET ROMANCE',description:'Ботаника, пластика и свободный силуэт.',symbol:'tulip',brand:'Belle Dona',font:'cormorant',caption:'Красиво быть собой',tagline:'Мягкие линии. Личный характер.'},
    {name:'Лебединая грация',en:'SWAN GRACE',description:'Изящный силуэт, плавные линии и сдержанная элегантность.',symbol:'swan',brand:'Dona Muse',font:'cormorant',caption:'Естественная грация',tagline:'Лёгкость в каждом движении.'}
  ].map(c=>({...c,...seasonalPalettes}));
  const conceptStories=[
    'Рубиновая антиква и фактурный отпечаток на розово-молочной основе. Выразительный акцент для карточки, обложки и фирменного пакета.',
    'Мягкий бант работает как знак заботы. Светлая упаковка, тактильная бумага и свободная типографика создают спокойное настроение.',
    'Вишня добавляет живости, а глубокий цвет и взрослые пропорции букв удерживают баланс. Подходит для повседневных капсул.',
    'Плавная ботаническая форма и лёгкий набор. Тюльпан подчёркивает движение ткани и женственность без излишнего декора.',
    'Лебедь с изогнутой шеей и тонким рисунком перьев сочетается с пластичной антиквой. Рубиновый цвет и розово-молочная основа поддерживают сдержанную элегантность. Для струящихся тканей, лаконичной упаковки и выразительных обложек.'
  ];
  const initial = {concept:0,name:'DONA',symbol:'kiss',font:'prata',season:'summer',theme:'light',layout:'classic',avecAmour:false,customName:'',choices:{},custom:{},symbolStyle:'filled',brandPlacement:null,editor:{format:'post',x:50,y:25,angle:0,size:25,transparent:false,showName:true}};
  let state = structuredClone(initial);
  const storageKey = 'dona-brandbook-client-v2';
  function validate(raw) {
    if(!raw || typeof raw!=='object') return;
    if(Number.isInteger(raw.concept)&&concepts[raw.concept])state.concept=raw.concept;
    if(typeof raw.name==='string'&&raw.name.trim())state.name=raw.name.trim().slice(0,32);
    if(typeof raw.customName==='string')state.customName=raw.customName.slice(0,32);
    if(['classic','side','signature','imagegen-0','imagegen-1','imagegen-2','imagegen-3','imagegen-4'].includes(raw.layout))state.layout=raw.layout;
    state.symbolStyle='filled';
    state.avecAmour=raw.avecAmour===true;
    if(raw.editor&&typeof raw.editor==='object'){
      const e=raw.editor;for(const [k,min,max]of [['x',0,100],['y',0,100],['angle',-180,180],['size',8,70]])if(typeof e[k]==='number'&&Number.isFinite(e[k]))state.editor[k]=Math.max(min,Math.min(max,e[k]));
      if(['post','story','card','avatar'].includes(e.format))state.editor.format=e.format;
      state.editor.transparent=e.transparent===true;state.editor.showName=e.showName!==false;
    }
    if(raw.brandPlacement&&typeof raw.brandPlacement==='object'){const q=raw.brandPlacement;if(['x','y','angle','size'].every(k=>typeof q[k]==='number'&&Number.isFinite(q[k])))state.brandPlacement={x:Math.max(0,Math.min(100,q.x)),y:Math.max(0,Math.min(100,q.y)),angle:Math.max(-180,Math.min(180,q.angle)),size:Math.max(8,Math.min(70,q.size)),format:['post','story','card','avatar'].includes(q.format)?q.format:state.editor.format};}
    if(Object.hasOwn(symbols,raw.symbol))state.symbol=raw.symbol;
    if(fonts.some(f=>f.id===raw.font))state.font=raw.font;
    if(['summer','winter'].includes(raw.season))state.season=raw.season;
    if(['light','dark'].includes(raw.theme))state.theme=raw.theme;
    for(let c=0;c<concepts.length;c++)for(const s of ['summer','winter']){
      const k=`${c}-${s}`;
      if([0,1,2,3].includes(raw.choices?.[k]))state.choices[k]=raw.choices[k];
      if(/^#[\da-f]{6}$/i.test(raw.custom?.[k]))state.custom[k]=raw.custom[k];
    }
  }
  function selectSymbol(id){state.symbol=id;}
  try {validate(window.DONA_SNAPSHOT || JSON.parse(localStorage.getItem(storageKey)));}catch{}
  const $ = s=>document.querySelector(s);
  const $$ = s=>document.querySelectorAll(s);
  function svg(id,positioned=false){return `<svg viewBox="0 0 100 100" fill="none" stroke="currentColor" stroke-width="1.35" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${positioned?standaloneSymbol(id):symbolBody(id)}</svg>`;}

  function esc(s){return s.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;');}
  function luminance(hex){const rgb=hex.match(/[a-f\d]{2}/ig).map(v=>parseInt(v,16)/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4);return .2126*rgb[0]+.7152*rgb[1]+.0722*rgb[2];}
  function contrastText(hex){return luminance(hex)>.179?'#201C1B':'#FFFFFF';}
  function palette(){const key=`${state.concept}-${state.season}`;const p=[...concepts[state.concept][state.season][state.choices[key]||0]];p[1]=state.custom[key]||p[1];return p;}
  $('#name').innerHTML=names.map(n=>`<option>${n[0]}</option>`).join('')+'<option value="__custom">Своё название…</option>';
  $('#font').innerHTML=fonts.map(f=>`<option value="${f.id}">${f.name}</option>`).join('');
  $('#symbols').innerHTML=Object.entries(symbols).map(([id,s])=>`<button class="symbol-button" data-symbol="${id}" aria-pressed="false">${svg(id)}<span>${s.name}</span></button>`).join('');
  $('#symbol-gallery').innerHTML=Object.entries(symbols).map(([id,s])=>`<button data-symbol="${id}" aria-pressed="false">${svg(id)}<span>${s.name}</span></button>`).join('');
  $('#concepts').innerHTML=concepts.map((c,i)=>`<button class="concept-card" data-concept="${i}" aria-pressed="false"><div class="concept-icon" style="color:${c.summer[0][1]}">${svg(c.symbol)}</div><div><span class="concept-number">НАПРАВЛЕНИЕ 0${i+1}</span><strong>${c.name}</strong><small>${c.description}</small></div><span class="selected-check" aria-hidden="true"></span></button>`).join('');
  $('#name-cards').innerHTML=names.map(n=>`<button class="name-card" data-name="${n[0]}" aria-pressed="false"><span>${n[1]}</span><strong>${n[0]}</strong><p>${n[2]}</p><span class="name-arrow" aria-hidden="true">↗</span></button>`).join('');
  function render(){
    if(!availableLayouts().includes(state.layout))state.layout=availableLayouts()[0];
    const c=concepts[state.concept],f=fonts.find(f=>f.id===state.font),p=palette(),key=`${state.concept}-${state.season}`;
    const vars={'--accent':p[1],'--base':p[2],'--soft':p[3],'--brand-font':f.family,'--on-accent':contrastText(p[1]),'--on-base':contrastText(p[2]),'--on-soft':contrastText(p[3])};
    for(const [k,v]of Object.entries(vars))$('#board').style.setProperty(k,v);
    $('#font-sample').style.fontFamily=f.family;
    $('#name').value=names.some(n=>n[0]===state.name)?state.name:'__custom';
    const manualName=$('#name').value==='__custom';$('#custom-name').hidden=!manualName;$('label[for=custom-name]').hidden=!manualName;
    $('#custom-name').value=state.customName;
    $('#custom-name-count').textContent=`${state.customName.length}/32`; $('#font').value=state.font;
    $('#name-hint').textContent=names.find(n=>n[0]===state.name)?.[2] || 'Ваше название — во всех носителях и экспортируемом логотипе.';
    $$('[data-style]').forEach(el=>el.setAttribute('aria-pressed',el.dataset.style===state.symbolStyle));
    for(const root of ['#symbols','#symbol-gallery'])$$(root+' [data-symbol]').forEach(el=>el.querySelector('svg').outerHTML=svg(el.dataset.symbol));
    $('#symbol-hint').textContent=symbols[state.symbol].hint;$('#font-hint').textContent=f.hint;
    $$('[data-brand]').forEach(el=>el.textContent=state.name);$$('[data-mark]').forEach(el=>el.innerHTML=svg(state.symbol,true));
    $$('[data-concept]').forEach(el=>{const active=Number(el.dataset.concept)===state.concept;el.setAttribute('aria-pressed',active);el.querySelector('.selected-check').textContent=active?'✓':'';});
    for(const [attr,value]of [['symbol',state.symbol],['season',state.season],['theme',state.theme],['name',state.name]])$$(`button[data-${attr}]`).forEach(el=>el.setAttribute('aria-pressed',el.dataset[attr]===value));
    $('#palettes').innerHTML=c[state.season].map((pal,i)=>`<button class="palette-choice" data-palette="${i}" aria-pressed="${(state.choices[key]||0)===i&&!state.custom[key]}"><span class="mini-swatches">${pal.slice(1).map(color=>`<i style="background:${color}"></i>`).join('')}</span>${pal[0]}</button>`).join('');
    $('#accent').value=p[1];$('#accent-hex').textContent=p[1].toUpperCase();
    $('#board-id').textContent=`${c.name} · ${state.season==='summer'?'Лето':'Зима'}`;$('#hero-number').textContent=`0${state.concept+1}—0${concepts.length}`;
    $('#brand-caption').textContent=c.caption;$('#mood-label').textContent=c.en;$('#hero-tagline').textContent=c.tagline;
    $('#collection-season').textContent=state.season==='summer'?'SUMMER EDIT / 26':'WINTER EDIT / 26';
    $('#collection-title').textContent=state.season==='summer'?'Легко быть собой.':'Тепло быть собой.';
    $('#collection-copy').textContent=state.season==='summer'?'Мягкий свет. Свободный силуэт. Ваш собственный ритм.':'Тактильные фактуры. Глубокие оттенки. Время замедлиться.';
    $('#swatches').innerHTML=p.slice(1).map((color,i)=>`<div class="swatch"><div class="swatch-color" style="background:${color}"></div><span>${['Акцент · 10%','Основа · 60%','Полутон · 30%'][i]}</span><code>${color.toUpperCase()}</code></div>`).join('');
    $('#type-name').textContent=f.name;$('#social-integrations').dataset.theme=state.theme;
    renderLogos();
    document.fonts.ready.then(renderLogos);
    $('#theme-label').textContent=state.theme==='light'?'СВЕТЛАЯ':'ТЁМНАЯ';
    $('#concept-rationale').textContent=conceptStories[state.concept];
    $('#route-selection').textContent=`${state.name} · ${symbols[state.symbol].name} · ${state.season==='summer'?'Лето':'Зима'}`;
    $('#summary').textContent=`${state.name} · ${symbols[state.symbol].name} · ${f.name.split(' · ')[0]} · ${state.season==='summer'?'Лето':'Зима'} / ${state.custom[key]?'Свой цвет':p[0]} · ${state.theme==='light'?'Светлая':'Тёмная'} тема`;
    try{localStorage.setItem(storageKey,JSON.stringify(state));}catch{}
  }
  const layouts={
    classic:['Редакционная антиква','Выразительное имя и один акцент. Для вывески и обложки.'],
    side:['Личная подпись','Контраст антиквы и рукописной линии. Для бирки и карточки.'],
    signature:['Дом бренда','Собранная центральная композиция. Для пакета и этикетки.']
  };
  const generatedBoards={
    'DONA':{id:'direction-dona',extraBoxes:[[75,165,885,545],[70,810,910,530]],boxes:[[140,80,810,330],[150,580,740,330],[155,960,705,450]]},
    'Dona Muse':{id:'direction-muse',extraBoxes:[[95,70,840,640],[95,790,840,640]],boxes:[[70,120,900,290],[70,520,910,365],[175,900,630,535]]},
    'Dona Belle':{id:'direction-belle',extraBoxes:[[90,140,855,530],[90,840,855,560]],boxes:[[65,120,910,275],[85,505,830,400],[270,935,580,535]]},
    'Belle Dona':{id:'direction-belledona',extraBoxes:[[100,95,830,590],[100,845,830,600]],boxes:[[70,120,910,300],[105,480,825,415],[175,935,640,505]]}
  };
  const generatedLabels=['Помадная подпись','Шёлковая каллиграфия','Ботаническая композиция','Вишнёвая гравюра','Лебединая пластика'];
  const generatedSymbols=['kiss','bow','tulip','cherry','swan'];
  function isFixed(){return state.layout.startsWith('imagegen-')&&!!generatedBoards[state.name];}
  function availableLayouts(){return ['classic','side','signature',...(generatedBoards[state.name]?['imagegen-0','imagegen-1','imagegen-2','imagegen-3','imagegen-4']:[])];}
  function generatedLogoSvg(layout){
    if(state.name==='DONA'&&layout==='imagegen-0'){
      const uid='signature-'+(++symbolSequence),h=state.avecAmour?570:430;
      const tagline=state.avecAmour?'<text x="795" y="707" text-anchor="middle" font-family="Great Vibes,Marck Script,cursive" font-size="112" fill="currentColor">avec amour</text>':'';
      return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="50 180 1880 ${h}" role="img" aria-label="DONA — Помадная подпись${state.avecAmour?' · avec amour':''}" style="color:${palette()[1]}"><title>DONA — Помадная подпись</title><defs><filter id="${uid}-ink" color-interpolation-filters="sRGB"><feColorMatrix type="matrix" values="0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 -.2126 -.7152 -.0722 1 0"/><feComposite in2="SourceAlpha" operator="in"/></filter><mask id="${uid}" maskUnits="userSpaceOnUse" x="50" y="180" width="1880" height="430" style="mask-type:alpha"><image href="${window.DONA_MEDIA['dona-kiss-signature']}" width="1942" height="809" filter="url(#${uid}-ink)"/></mask></defs><rect x="50" y="180" width="1880" height="430" fill="currentColor" mask="url(#${uid})"/>${tagline}</svg>`;
    }
    const board=generatedBoards[state.name],index=Number(layout.slice(-1)),[x,y,w,h]=index<3?board.boxes[index]:board.extraBoxes[index-3],source=index<3?board.id:board.id+'-cherry-swan',uid='sketch-'+(++symbolSequence);
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${x} ${y} ${w} ${h}" role="img" aria-label="${esc(state.name)} — ${generatedLabels[index]}" style="color:${palette()[1]}"><title>${esc(state.name)} — ImageGen · ${generatedLabels[index]}</title><defs><filter id="${uid}-ink" filterUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1536" color-interpolation-filters="sRGB"><feColorMatrix type="matrix" values="0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 -1.3 0 0 1.05"/><feComposite in2="SourceAlpha" operator="in"/></filter><mask id="${uid}" maskUnits="userSpaceOnUse" x="${x-16}" y="${y-16}" width="${w+32}" height="${h+32}" style="mask-type:alpha"><image href="${window.DONA_MEDIA[source]}" width="1024" height="1536" filter="url(#${uid}-ink)"/></mask></defs><rect x="${x}" y="${y}" width="${w}" height="${h}" fill="currentColor" stroke="none" mask="url(#${uid})"/></svg>`;
  }
  function logoSvg(layout,applyPlacement=true){
    if(layout.startsWith('imagegen-')&&generatedBoards[state.name])return generatedLogoSvg(layout);
    const f=fonts.find(f=>f.id===state.font),p=palette(),text=state.name;
    const ctx=document.createElement('canvas').getContext('2d');ctx.font=`100px ${f.family}`;
    const label=(value,x,y,size=100,anchor='middle')=>`<text x="${x}" y="${y}" text-anchor="${anchor}" font-family="${esc(f.family)}" font-size="${size}" fill="currentColor" stroke="none">${esc(value)}</text>`;
    const word=(value,x,y,maxWidth=700,maxSize=130)=>label(value,x,y,Math.min(maxSize,maxWidth/(ctx.measureText(value).width||100)*100));
    const mark=(x,y,size,angle=0)=>`<g transform="translate(${x} ${y}) rotate(${angle} ${size/2} ${size/2}) scale(${size/100})">${symbolBody(state.symbol)}</g>`;
    const script=(value,x,y,size=60)=>`<text x="${x}" y="${y}" text-anchor="middle" font-family="Great Vibes,Marck Script,cursive" font-size="${size}" fill="currentColor">${esc(value)}</text>`;
    const parts=text.split(' '),known=names.some(n=>n[0]===text),two=known&&parts.length===2;
    let body='';
    if(layout==='fascia'){
      const size=Math.min(150,780/(ctx.measureText(text).width||100)*100);
      const width=ctx.measureText(text).width*size/100;
      body=mark(20,135,110)+label(text,160+width/2,235,size);
    }
    if(layout==='classic'){
      const centered=['bow','swan'].includes(state.symbol);
      const actualWidth=Math.min(155,700/(ctx.measureText(text).width||100)*100)*(ctx.measureText(text).width||100)/100;
      body=word(text,centered?500:460,245,centered?820:700,155)+mark(centered?430:460+actualWidth/2-40,0,140,state.symbol==='kiss'?-18:0);
    }
    if(layout==='side'){
      body=mark(95,75,185,state.symbol==='kiss'?-12:0);
      if(two)body+=word(parts[0],565,185,580,150)+script(parts[1],625,260,82);
      else body+=word(text,590,205,580,138)+script(known?'avec amour':'collection',625,262,45);
    }
    if(layout==='signature'){
      body=mark(425,0,150);
      if(two&&state.symbol==='tulip')body=mark(155,40,270)+word(parts[0],610,161,560,112)+word(parts[1],610,267,560,112);
      else if(two)body+=word(parts[0],500,214,720,98)+word(parts[1],500,301,650,96);
      else body+=word(text,500,248,800,158)+`<text x="500" y="291" text-anchor="middle" font-family="Manrope,sans-serif" font-size="12" letter-spacing="6" fill="currentColor">LE VESTIAIRE FÉMININ</text>`;
    }
    let viewBox='0 0 1000 340';
    if(applyPlacement&&state.brandPlacement){const composed=proportionalLockup(ctx,label);body=composed.body;viewBox=composed.viewBox;}
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox}" role="img" aria-label="${esc(text)} — ${(layouts[layout]?.[0]||'Вывеска')}" style="color:${p[1]}"><title>${esc(text)} — ${(layouts[layout]?.[0]||'Вывеска')}</title>${body}</svg>`;
  }
  function renderLogos(){
    const allowed=availableLayouts();
    if(!allowed.includes(state.layout))state.layout=allowed[0];
    const logo=logoSvg(state.layout),focused=$('#layouts').contains(document.activeElement)?document.activeElement.closest('[data-layout]')?.dataset.layout:null;
    $('#hero-logo').innerHTML=logo;$$('[data-lockup]').forEach(el=>el.innerHTML=logoSvg(state.layout));
    $('#layouts').innerHTML=allowed.map(id=>{
      const fixed=id.startsWith('imagegen-'),title=fixed?generatedLabels[Number(id.slice(-1))]:layouts[id][0];
      const choice=`<button data-layout="${id}" aria-pressed="${id===state.layout&&!state.brandPlacement}">${logoSvg(id,false)}<strong>${title}</strong><span>${fixed?'ImageGen · фиксированный эскиз':layouts[id][1]}</span></button>`;
      return `<article class="logo-option">${choice}${fixed?`<div class="sketch-downloads" aria-label="Скачать ${title}"><button data-sketch="${id}" data-export="png">PNG · 4000 px</button><button data-sketch="${id}" data-export="svg">SVG</button><button data-sketch="${id}" data-export="kit">Комплект ZIP ↓</button><small>Прозрачный фон · ZIP: цветной, светлый, чёрный и белый. SVG содержит растровый рисунок.</small><span data-sketch-status role="status"></span></div>`:''}</article>`;
    }).join('');
    if(focused)$('#layouts').querySelector(`[data-layout="${focused}"]`)?.focus({preventScroll:true});
    $('#layout-note').textContent=isFixed()?'Выбран эскиз ImageGen. Рисунок, шрифт и размещение внутри логотипа зафиксированы. Палитру и носители можно менять.':'Три редактируемые конструкции'+(generatedBoards[state.name]?' и пять самостоятельных эскизов ImageGen для выбранного названия.':'. Для собственного названия доступны общие конструкции.');
    $('#avec-amour-setting').hidden=state.name!=='DONA';
    $('#avec-amour').checked=state.avecAmour;
    $('#font').disabled=isFixed();
    $$('#symbols button,#symbol-gallery button').forEach(el=>el.disabled=isFixed());
    $('#symbol-study').innerHTML=svg(state.symbol);$('#symbol-title').textContent=symbols[state.symbol].name;$('#symbol-description').textContent=symbols[state.symbol].hint;
    renderEditor();
    renderClient();
    refreshPlacementSurfaces();
    $('#small-avatar').innerHTML=svg(state.symbol,true);$('#tiny-avatar').innerHTML=svg(state.symbol,true);
    try{localStorage.setItem(storageKey,JSON.stringify(state));}catch{}
  }
  let toastTimer;
  function toast(text){$('#toast').textContent=text;$('#toast').classList.add('visible');clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('#toast').classList.remove('visible'),3500);}
  $('#name').addEventListener('change',e=>{if(e.target.value==='__custom'){state.name=state.customName.trim()||'Ваш бренд';}else state.name=e.target.value;render();if(e.target.value==='__custom')$('#custom-name').focus();});
  $('#custom-name').addEventListener('input',e=>{const start=e.target.selectionStart,end=e.target.selectionEnd;state.customName=e.target.value;state.name=state.customName.trim()||'Ваш бренд';render();e.target.setSelectionRange(start,end);});
  $('#layouts').addEventListener('click',e=>{const b=e.target.closest('[data-layout]');if(b){state.layout=b.dataset.layout;if(isFixed())state.symbol=generatedSymbols[Number(state.layout.slice(-1))];state.brandPlacement=null;render();}});
  $('#symbol-style').addEventListener('click',e=>{const b=e.target.closest('[data-style]');if(b){state.symbolStyle=b.dataset.style;render();}});
  $('#avec-amour').addEventListener('change',event=>{state.avecAmour=event.target.checked;state.layout='imagegen-0';state.symbol='kiss';render();});
  $('#font').addEventListener('change',e=>{state.font=e.target.value;render();document.fonts.load(`100px ${fonts.find(f=>f.id===state.font).family}`).then(renderLogos);});
  $('#accent').addEventListener('input',e=>{state.custom[`${state.concept}-${state.season}`]=e.target.value;render();});
  for(const id of ['#symbols','#symbol-gallery'])$(id).addEventListener('click',e=>{const b=e.target.closest('[data-symbol]');if(b){selectSymbol(b.dataset.symbol);render();}});
  $('#seasons').addEventListener('click',e=>{const b=e.target.closest('[data-season]');if(b){state.season=b.dataset.season;render();}});
  $('#themes').addEventListener('click',e=>{const b=e.target.closest('[data-theme]');if(b){state.theme=b.dataset.theme;render();}});
  $('#palettes').addEventListener('click',e=>{const b=e.target.closest('[data-palette]');if(b){const k=`${state.concept}-${state.season}`;state.choices[k]=Number(b.dataset.palette);delete state.custom[k];render();}});
  $('#concepts').addEventListener('click',e=>{const b=e.target.closest('[data-concept]');if(!b)return;state.concept=Number(b.dataset.concept);const c=concepts[state.concept];selectSymbol(c.symbol);state.font=c.font;if(isFixed())state.layout='imagegen-'+generatedSymbols.indexOf(c.symbol);render();toast(`Выбран концепт «${c.name}»`);});
  $('#name-cards').addEventListener('click',e=>{const b=e.target.closest('[data-name]');if(!b)return;state.name=b.dataset.name;render();$('#studio').scrollIntoView({behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'});toast(`Название: ${state.name}`);});
  $('#reset').addEventListener('click',()=>{const current=state.concept;state=structuredClone(initial);state.concept=current;const c=concepts[current];selectSymbol(c.symbol);state.font=c.font;if(isFixed())state.layout='imagegen-'+generatedSymbols.indexOf(c.symbol);render();toast('Восстановлены настройки концепта');});
  function download(content,type,filename){const url=URL.createObjectURL(new Blob([content],{type})),a=document.createElement('a');a.href=url;a.download=filename;a.click();setTimeout(()=>URL.revokeObjectURL(url),2000);}
  $('#download-logo').addEventListener('click',()=>{downloadSvg(logoSvg(state.layout),'dona-logo.svg');});
  $('#download-avatar').addEventListener('click',()=>{const p=palette();const body=standaloneSymbol();downloadSvg(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><rect width="100" height="100" fill="${p[2]}"/><g fill="none" stroke="${p[1]}" color="${p[1]}" stroke-width="1.35" stroke-linecap="round" stroke-linejoin="round">${body}</g></svg>`,'dona-avatar.svg');});

  async function exportBook(){
    try{
      toast('Подготавливаем полный брендбук с оригиналами…');
      const fullMedia=window.DONA_ORIGINALS?await window.DONA_ORIGINALS():window.DONA_MEDIA;
      await window.DONA_LOAD_LICENSES?.();
      let css,js;
      if(window.DONA_ASSETS){({css,js}=window.DONA_ASSETS);}else{
        const fetchVersioned=path=>fetch(path+'?v='+encodeURIComponent(window.DONA_BUILD||'dev'));
        const responses=await Promise.all(['styles.css','app.js','fonts.css','social.css','media.css'].map(fetchVersioned));
        if(responses.some(r=>!r.ok))throw new Error('assets');
        const texts=await Promise.all(responses.map(r=>r.text()));css=texts[2]+'\n'+texts[0]+'\n'+texts[3]+'\n'+texts[4]+'\n'+await (await fetchVersioned('client.css')).text();js=texts[1];
      }
      const clone=document.documentElement.cloneNode(true);
      clone.querySelectorAll('script,style,link[rel=stylesheet]').forEach(el=>el.remove());
      clone.querySelectorAll('svg').forEach(el=>el.remove());clone.querySelectorAll('img').forEach(el=>el.removeAttribute('src'));clone.querySelectorAll('[style]').forEach(el=>{if(el.style.backgroundImage)el.style.removeProperty('background-image');});
      clone.querySelector('#last-download').hidden=true;clone.querySelector('#last-download-link').removeAttribute('href');clone.querySelector('#toast').classList.remove('visible');clone.querySelector('#png-result').hidden=true;clone.querySelector('#png-result').removeAttribute('href');clone.querySelector('#png-preview').hidden=true;clone.querySelector('#png-preview').removeAttribute('src');
      const style=document.createElement('style');style.textContent=css;clone.querySelector('head').append(style);
      const snapshot=document.createElement('script');snapshot.textContent=`window.DONA_LICENSES=${JSON.stringify(window.DONA_LICENSES||{}).replaceAll('<','\\u003c')};window.DONA_MEDIA=${JSON.stringify(fullMedia).replaceAll('<','\\u003c')};window.DONA_SNAPSHOT=${JSON.stringify(state).replaceAll('<','\\u003c')};window.DONA_ASSETS=${JSON.stringify({css,js}).replaceAll('<','\\u003c')};`;
      clone.querySelector('body').append(snapshot);
      const script=document.createElement('script');script.textContent=js;clone.querySelector('body').append(script);
      download('<!doctype html>\n'+clone.outerHTML,'text/html;charset=utf-8','dona-brandbook.html');toast('Интерактивный брендбук сохранён для просмотра офлайн');
    }catch{toast('Для экспорта откройте страницу через локальный сервер. Инструкция — в README.');}
  }
  $('#export').addEventListener('click',exportBook);$('#save-bottom').addEventListener('click',exportBook);
  const artworkFormats={post:[1080,1350,'Публикация'],story:[1080,1920,'История'],card:[1600,1000,'Фирменная карточка'],avatar:[1080,1080,'Аватар']};
  const presets=[['tl','↖','Сверху слева',20,20],['tc','↑','Сверху',50,20],['tr','↗','Сверху справа',80,20],['ml','←','Слева',20,50],['mc','·','В центре',50,50],['mr','→','Справа',80,50],['bl','↙','Снизу слева',20,80],['bc','↓','Снизу',50,80],['br','↘','Снизу справа',80,80]];
  $('#artwork-symbol').innerHTML=Object.entries(symbols).map(([id,s])=>`<option value="${id}">${s.name}</option>`).join('');
  $('#artwork-symbol').addEventListener('change',e=>{selectSymbol(e.target.value);render();});
  $('#artwork-style').addEventListener('change',e=>{state.symbolStyle=e.target.value;render();});
  $('#position-presets').innerHTML=presets.map(([id,arrow,label])=>`<button data-position="${id}" aria-label="${label}" title="${label}"><b aria-hidden="true">${arrow}</b><span>${label}</span></button>`).join('');
  function persist(){try{localStorage.setItem(storageKey,JSON.stringify(state));}catch{}}
  function artworkSpec(){
    const e=state.editor,[w,h,title]=artworkFormats[e.format],size=Math.min(w,h)*e.size/100;
    // A rotated square containing the entire symbol remains within the artwork.
    const radians=e.angle*Math.PI/180,extent=size*(Math.abs(Math.cos(radians))+Math.abs(Math.sin(radians)))/2+4;
    const clamp=(n,lo,hi)=>Math.max(lo,Math.min(hi,n));
    e.x=clamp(e.x,extent/w*100,100-extent/w*100);e.y=clamp(e.y,extent/h*100,100-extent/h*100);
    const p=palette(),family=fonts.find(f=>f.id===state.font).family;
    const ctx=document.createElement('canvas').getContext('2d');ctx.font=`100px ${family}`;
    const fontSize=Math.min(w*.09,w*.78/(ctx.measureText(state.name).width||100)*100);
    return {w,h,title,size,x:e.x*w/100,y:e.y*h/100,angle:e.angle,ink:p[1],paper:p[2],family,fontSize,textY:h*.54,captionY:h*.60,showName:e.format!=='avatar'&&e.showName!==false,transparent:e.transparent};
  }
  function artworkMark(a){return `<g transform="translate(${a.x} ${a.y}) rotate(${a.angle}) scale(${a.size/100}) translate(-50 -50)" fill="none" stroke="currentColor" stroke-width="1.35" stroke-linecap="round" stroke-linejoin="round">${symbolBody(state.symbol)}</g>`;}
  function renderEditor(){
    const a=artworkSpec(),e=state.editor;
    const fixed=isFixed();
    $$('.artwork-controls input,.artwork-controls select,#position-presets button,#artwork-reset,#apply-brand-placement').forEach(el=>el.disabled=fixed);
    if(fixed){
      $('#artwork-canvas').innerHTML=logoSvg(state.layout);
      $('#artwork-canvas').style.aspectRatio='auto';
      $('#editor-symbol-name').textContent='Фиксированный эскиз ImageGen · размещение не редактируется';
      $('#artwork-size-label').textContent='Авторская композиция';
      $('#brand-placement-status').textContent='Чтобы редактировать размещение, выберите одну из трёх конструкций логотипа.';
      $('#clear-brand-placement').disabled=true;
      $('#artwork-canvas').setAttribute('aria-label','Фиксированный эскиз ImageGen');
      return;
    }
    $('#artwork-symbol').value=state.symbol;$('#artwork-style').value=state.symbolStyle;
    $('#artwork-format').value=e.format;$('#artwork-transparent').checked=e.transparent;$('#artwork-name').checked=e.showName!==false;$('#artwork-name').disabled=e.format==='avatar';
    for(const key of ['x','y','angle','size']){$('#artwork-'+key).value=e[key];$('#value-'+key).textContent=Math.round(e[key])+(key==='angle'?'°':'%');}
    updatePlacementStatus();
    $('#artwork-size-label').textContent=`${a.w} × ${a.h} px`;
    $('#editor-symbol-name').textContent=`${symbols[state.symbol].name} · ${state.symbolStyle==='filled'?'заливка':'контур'}`;
    $$('[data-position]').forEach(button=>{const p=presets.find(p=>p[0]===button.dataset.position);button.setAttribute('aria-pressed',Math.abs(e.x-p[3])<.6&&Math.abs(e.y-p[4])<.6);});
    const name=a.showName?`<text x="${a.w/2}" y="${a.textY}" text-anchor="middle" font-family="${esc(a.family)}" font-size="${a.fontSize}" fill="${a.ink}">${esc(state.name)}</text><text x="${a.w/2}" y="${a.captionY}" text-anchor="middle" font-family="Manrope,sans-serif" font-size="${a.w*.016}" fill="${a.ink}">ЖЕНСКАЯ ОДЕЖДА</text>`:'';
    $('#artwork-canvas').innerHTML=`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${a.w} ${a.h}" style="color:${a.ink}" aria-label="${esc(a.title)}: ${esc(state.name)}"><rect width="${a.w}" height="${a.h}" fill="${a.transparent?'transparent':a.paper}"/>${name}${artworkMark(a)}<g transform="translate(${a.x} ${a.y}) rotate(${a.angle})"><rect data-drag-symbol x="${-a.size/2}" y="${-a.size/2}" width="${a.size}" height="${a.size}" fill="transparent" stroke="${a.ink}" stroke-opacity=".3" stroke-width="1.5" stroke-dasharray="8 8"/></g></svg>`;
    $('#artwork-canvas').style.maxWidth=`min(100%, ${720*a.w/a.h}px)`;
    $('#artwork-canvas').style.aspectRatio=`${a.w}/${a.h}`;
    $('#png-result').textContent=$('#png-result').hidden?'':'Предыдущий PNG · скачать ↗';
    $('#artwork-canvas').setAttribute('aria-label',`Переместить символ. По горизонтали ${Math.round(e.x)}%, по вертикали ${Math.round(e.y)}%. Используйте стрелки клавиатуры.`);
  }
  $('#position-presets').addEventListener('click',event=>{const button=event.target.closest('[data-position]');if(!button)return;const preset=presets.find(p=>p[0]===button.dataset.position);state.editor.x=preset[3];state.editor.y=preset[4];renderEditor();persist();});
  for(const key of ['x','y','angle','size'])$('#artwork-'+key).addEventListener('input',event=>{state.editor[key]=Number(event.target.value);renderEditor();persist();});
  for(const [id,key] of [['artwork-format','format']])$('#'+id).addEventListener('change',event=>{state.editor[key]=event.target.value;renderEditor();persist();});
  for(const [id,key] of [['artwork-transparent','transparent'],['artwork-name','showName']])$('#'+id).addEventListener('change',event=>{state.editor[key]=event.target.checked;renderEditor();persist();});
  $('#artwork-reset').addEventListener('click',()=>{const format=state.editor.format;state.editor=structuredClone(initial.editor);state.editor.format=format;renderEditor();persist();});
  const stage=$('#artwork-canvas');let drag=null;
  stage.addEventListener('pointerdown',event=>{if(!event.target.closest('[data-drag-symbol]'))return;event.preventDefault();stage.focus({preventScroll:true});stage.setPointerCapture(event.pointerId);drag={id:event.pointerId,px:event.clientX,py:event.clientY,x:state.editor.x,y:state.editor.y,rect:stage.getBoundingClientRect()};stage.classList.add('is-dragging');});
  stage.addEventListener('pointermove',event=>{if(!drag||drag.id!==event.pointerId)return;state.editor.x=drag.x+(event.clientX-drag.px)/drag.rect.width*100;state.editor.y=drag.y+(event.clientY-drag.py)/drag.rect.height*100;renderEditor();});
  function finishDrag(){if(!drag)return;drag=null;stage.classList.remove('is-dragging');persist();}
  stage.addEventListener('pointerup',finishDrag);stage.addEventListener('pointercancel',finishDrag);stage.addEventListener('lostpointercapture',finishDrag);
  stage.addEventListener('keydown',event=>{if(isFixed())return;const delta={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]}[event.key];if(!delta)return;event.preventDefault();const step=event.shiftKey?5:1;state.editor.x+=delta[0]*step;state.editor.y+=delta[1]*step;renderEditor();persist();});

  function updatePlacementStatus(){
    const q=state.brandPlacement;
    $('#clear-brand-placement').disabled=!q;
    const same=q&&['x','y','angle','size'].every(k=>Math.abs(q[k]-state.editor[k])<.01)&&q.format===state.editor.format;
    $('#brand-placement-status').textContent=q?`В брендбуке: X ${Math.round(q.x)}%, Y ${Math.round(q.y)}%, угол ${q.angle}°, размер ${q.size}%. ${same?'Настройки применены. В отдельных знаках повторяется только поворот; знак с названием масштабируется как единая композиция.':'Изменения редактора ещё не применены.'}`:'В брендбуке используются исходные композиции. Знак с названием сохраняет пропорции композиции. Отдельные знаки остаются по центру своих областей и повторяют только поворот.';
  }
  $('#apply-brand-placement').addEventListener('click',()=>{if(isFixed())return;artworkSpec();state.brandPlacement=Object.fromEntries(['x','y','angle','size'].map(k=>[k,state.editor[k]]));state.brandPlacement.format=state.editor.format;render();toast('Расположение символа применено ко всему брендбуку');});
  $('#clear-brand-placement').addEventListener('click',()=>{state.brandPlacement=null;render();toast('Исходные композиции восстановлены');});
  function refreshPlacementSurfaces(){
    $('#board').classList.toggle('has-brand-placement',!!state.brandPlacement);
    document.querySelectorAll('.brand-placement-overlay').forEach(el=>el.remove());
    if(!state.brandPlacement)return;
    for(const host of document.querySelectorAll('.hangtag,.editorial,.tg-cover,.story-photo,.post-photo')){
      const w=host.clientWidth,h=host.clientHeight;if(!w||!h)continue;
      const overlay=document.createElement('div');overlay.className='brand-placement-overlay';overlay.setAttribute('aria-hidden','true');
      if(host.matches('.hangtag')){overlay.classList.add('tag-lockup-overlay');overlay.innerHTML=logoSvg(state.layout).replace(/style="color:[^"]*"/,'style="color:inherit"');host.append(overlay);continue;}
      overlay.innerHTML=`<svg viewBox="0 0 ${w} ${h}" fill="none" stroke="currentColor" stroke-width="1.35" stroke-linecap="round" stroke-linejoin="round">${placedSymbol(w,h)}</svg>`;host.append(overlay);
    }
  }
  let resizeFrame;window.addEventListener('resize',()=>{cancelAnimationFrame(resizeFrame);resizeFrame=requestAnimationFrame(refreshPlacementSurfaces);});

  let pngUrl=null;
  $('#download-artwork').addEventListener('click',async()=>{
    const button=$('#download-artwork');button.disabled=true;button.textContent='Подготавливаем PNG…';
    try{
      if(isFixed()){saveBlob(await pngBlob(logoSvg(state.layout)), 'dona-imagegen.png');return;}
      // Capture both vector artwork and text before awaiting; later UI edits do not alter the export.
      const a=artworkSpec(),name=state.name,format=state.editor.format;
      const vector=await portableSvg(`<svg xmlns="http://www.w3.org/2000/svg" width="${a.w}" height="${a.h}" viewBox="0 0 ${a.w} ${a.h}" style="color:${a.ink}">${artworkMark(a)}</svg>`);
      await document.fonts.load(`${a.fontSize}px ${a.family}`,name);await document.fonts.load('20px Manrope','ЖЕНСКАЯ ОДЕЖДА');
      const url=URL.createObjectURL(new Blob([vector],{type:'image/svg+xml'})),img=new Image();
      try{await new Promise((resolve,reject)=>{img.onload=resolve;img.onerror=reject;img.src=url;});}finally{URL.revokeObjectURL(url);}
      const canvas=document.createElement('canvas');canvas.width=a.w;canvas.height=a.h;const ctx=canvas.getContext('2d');
      if(!a.transparent){ctx.fillStyle=a.paper;ctx.fillRect(0,0,a.w,a.h);}
      if(a.showName){ctx.textAlign='center';ctx.fillStyle=a.ink;ctx.font=`${a.fontSize}px ${a.family}`;ctx.fillText(name,a.w/2,a.textY);ctx.font=`${a.w*.016}px Manrope,sans-serif`;ctx.fillText('ЖЕНСКАЯ ОДЕЖДА',a.w/2,a.captionY);}
      ctx.drawImage(img,0,0);
      const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/png'));if(!blob)throw new Error('PNG encoding failed');
      if(pngUrl)URL.revokeObjectURL(pngUrl);pngUrl=URL.createObjectURL(blob);
      const link=$('#png-result');link.href=pngUrl;link.download=`dona-${format}-${a.w}x${a.h}.png`;link.hidden=false;link.textContent=`PNG готов · ${a.w} × ${a.h} · скачать ещё раз ↗`;link.click();
      $('#png-preview').src=pngUrl;$('#png-preview').hidden=false;toast('PNG готов. Рамка редактирования в изображение не входит.');
    }catch(error){toast('Не удалось подготовить PNG. Попробуйте ещё раз.');console.error('Artwork export:',error);}
    finally{button.disabled=false;button.textContent='Сохранить макет PNG ↗';}
  });


  // Separated silhouettes and inset details keep small covers readable.
  const themedIcons={
    new:['Новинки','<path fill="url(#tone)" d="M36 20l-17 13 10 19 8-5-3 34h32l-3-34 8 5 10-19-17-13h-7q-7 8-14 0Z"/><path d="M43 20l-3 9 6 6 4-7 4 7 6-6-3-9M50 28v46M40 59l-1 15m21-15 1 15"/><circle cx="54" cy="43" r="1" fill="currentColor" stroke="none"/><circle cx="54" cy="52" r="1" fill="currentColor" stroke="none"/>'],
    looks:['Образы','<path d="M44 25a7 7 0 1 1 12 5l-6 6v8"/><path fill="url(#tone)" d="M50 44L18 67q-5 5 2 7h60q7-2 2-7Z"/><path d="M30 66h40"/>'],
    delivery:['Доставка','<path fill="url(#tone)" d="M20 33l29-14 31 14v39L50 86 20 72Z"/><path d="M22 34l28 14 28-14M50 49v34M36 27l29 14v14l-10 5V46"/><path d="M28 61l12 6m-12 0 8 4"/>'],
    love:['Отзывы','<path fill="url(#tone)" d="M19 32h62v47H19Z"/><path d="M20 34l30 25 30-25M21 77l18-17m40 17L61 60"/><path fill="currentColor" stroke="none" d="M50 41c-3-3-12-9-12-15 0-7 9-8 12-2 3-6 12-5 12 2 0 6-9 12-12 15Z"/>'],
    fitting:['Примерка','<rect x="28" y="15" width="44" height="65" rx="21" fill="url(#tone)"/><rect x="33" y="20" width="34" height="54" rx="16"/><path d="M38 45l16-16m-16 26 22-22M42 82l-4 6m20-6 4 6M34 89h32"/>'],
    care:['Уход','<path fill="url(#tone)" d="M23 44l7 35h40l7-35q-8 5-14 0t-13 0-13 0-14 0Z"/><path d="M24 48q7 5 13 0t13 0 13 0 13 0M37 59l3 13m20 0 3-13M37 21c-8 8 7 10 0 17m13-20c-8 8 7 10 0 17m13-14c-8 8 7 10 0 17"/>'],
    gift:['Подарки','<path fill="url(#tone)" d="M24 47h52v36H24Z"/><path fill="url(#tone)" d="M20 37h60v10H20Z"/><path d="M45 49v32m10-32v32M45 39v6m10-6v6"/><path fill="url(#tone)" d="M48 35C18 34 25 13 36 20q8 6 12 15Zm4 0c30-1 23-22 12-15q-8 6-12 15Z"/>'],
    address:['Бутик','<path fill="url(#tone)" d="M22 42h56v41H22Z"/><path fill="url(#tone)" d="M22 23h56l6 16q-5 10-13 3-7 8-14 0-7 8-14 0-7 8-14 0-8 7-13-3Z"/><path d="M34 25l-3 12m13-12-1 12m13-12 1 12m9-12 3 12M30 54h15v20H30Zm25 29V53h15v30"/><circle cx="66" cy="68" r="1" fill="currentColor" stroke="none"/>']
  };
  function iconSvg(id,color=palette()[1]){
    const uid='icon-'+(++symbolSequence);
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" role="img" aria-label="${themedIcons[id][0]}" style="color:${color}"><defs><linearGradient id="${uid}" x1="0" y1="0" x2="1" y2="1"><stop stop-color="currentColor" stop-opacity=".06"/><stop offset=".55" stop-color="currentColor" stop-opacity=".26"/><stop offset="1" stop-color="currentColor" stop-opacity=".10"/></linearGradient></defs><g fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round">${themedIcons[id][1].replaceAll('url(#tone)',`url(#${uid})`)}</g></svg>`;
  }
  // SVG references are document-wide, even across different inline SVG roots.
  // A hidden page must never own another visible logo's mask or filter.
  function uniqueSvgIds(source){
    const suffix='-instance-'+(++symbolSequence),ids=[...source.matchAll(/\bid="([^"]+)"/g)].map(match=>match[1]);
    for(const id of ids){source=source.replaceAll(`id="${id}"`,`id="${id}${suffix}"`).replaceAll(`url(#${id})`,`url(#${id}${suffix})`);}
    return source;
  }
  let carrierLogos=new Map();
  function nestedLogo(x,y,w,h,color,angle=0,layout=state.layout){
    if(!carrierLogos.has(layout)){
      // Measure the actual artwork, including custom placement, rather than its artboard.
      const host=document.createElement('div');
      host.style.cssText='position:fixed;left:-10000px;top:0;visibility:hidden;pointer-events:none';
      host.setAttribute('aria-hidden','true');
      host.innerHTML=logoSvg(layout);
      document.body.append(host);
      const node=host.firstElementChild;
      const bounds=node.getBBox();
      if(bounds.width>0&&bounds.height>0){
        const pad=5;
        node.setAttribute('viewBox',`${bounds.x-pad} ${bounds.y-pad} ${bounds.width+pad*2} ${bounds.height+pad*2}`);
      }
      node.setAttribute('preserveAspectRatio','xMidYMid meet');
      carrierLogos.set(layout,node.outerHTML);
      host.remove();
    }
    const artwork=uniqueSvgIds(carrierLogos.get(layout)).replace('<svg ',`<svg x="${x}" y="${y}" width="${w}" height="${h}" `).replace(/style="color:[^"]*"/,`style="color:${color}"`);
    return angle?`<g transform="rotate(${angle} ${x+w/2} ${y+h/2})">${artwork}</g>`:artwork;
  }
  function mockupSvg(id){
    const p=palette();let layers='';
    // Print areas follow each photographed surface. Tags read along their long edge.
    if(id==='packaging')layers=`<g transform="matrix(1 -.075 0 1 0 34.5)">${nestedLogo(255,425,410,155,p[2])}</g>`+nestedLogo(1170,695,270,135,p[1],9)+`<g transform="translate(918 819) matrix(.43 .90 -.98 .20 0 0)">${nestedLogo(-80,-31,160,62,p[1])}</g>`;
    if(id==='apparel')layers=`<g opacity=".88">${nestedLogo(910,275,175,65,p[1],4)}</g>`;
    if(id==='garment')layers=nestedLogo(540,326,450,158,p[1])+`<g transform="translate(1277 648) matrix(.25 .97 -.97 .26 0 0)">${nestedLogo(-110,-47.5,220,95,p[1])}</g>`;
    if(id.startsWith('store-'))layers=`<g ${id==='store-night'?'filter="url(#sign-glow)"':''}>${nestedLogo(425,145,685,78,p[2],0,isFixed()?state.layout:'fascia')}</g>`;
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1536 1024"><defs><filter id="sign-glow" x="-30%" y="-50%" width="160%" height="200%"><feDropShadow dx="0" dy="0" stdDeviation="3" flood-color="#FFD9AA" flood-opacity=".9"/></filter></defs><image href="${window.DONA_MEDIA[id]}" width="1536" height="1024"/>${layers}</svg>`;
  }
  function renderClient(){
    carrierLogos.clear();
    if(!$('#applications').hidden){
    for(const [container,items] of [['#physical-grid',[['packaging','Пакет, коробка и письмо','Матовая бумага, репсовая ручка и небольшая карточка с благодарностью.'],['garment','Внутренняя этикетка и бирка','Мягкий тканый ярлык у горловины и подвесная карточка.'],['apparel','Деликатный акцент на одежде','Небольшое нанесение на хлопковом лонгсливе. Основной акцент остаётся на крое и материале.']]],['#storefront-grid',[['store-day','Дневная вывеска','Светлые буквы на матовой рубиновой панели.'],['store-night','Вечерняя вывеска','Тёплая подсветка и мягкое свечение букв.']]]]){
      $(container).innerHTML=items.map(([id,title,caption])=>`<article class="physical-card"><div class="mockup">${mockupSvg(id)}</div><div class="physical-caption"><h4>${title}</h4><p>${caption}</p><button class="text-button" data-download-mockup="${id}">Скачать макет PNG ↗</button></div></article>`).join('');
    }
    }
    if(!$('#files').hidden){
    $('#generated-directions').innerHTML=names.flatMap(([name])=>[generatedBoards[name].id,generatedBoards[name].id+'-cherry-swan'].map(id=>`<figure><img loading="lazy" src="${window.DONA_MEDIA[id]}" alt="Эскизы логотипа ${name}"><figcaption>${name} · ${id.endsWith('swan')?'вишня и лебедь':'поцелуй, бант, тюльпан'} <button data-download-media="${id}" class="text-button">PNG ↗</button></figcaption></figure>`)).join('');
    }
    $('#social-highlights').innerHTML=Object.entries(themedIcons).slice(0,5).map(([id,[label]])=>`<span><i>${iconSvg(id,'inherit')}</i>${label}</span>`).join('');
    $('#icon-downloads').innerHTML=Object.entries(themedIcons).map(([id,[label]])=>`<article>${iconSvg(id)}<strong>${label}</strong><div><button data-download-icon="${id}" data-format="svg">SVG</button><button data-download-icon="${id}" data-format="png">PNG</button><button data-download-icon="${id}" data-format="png" data-tone="light">PNG для тёмного фона</button></div></article>`).join('');
    $('#symbol-downloads').innerHTML=Object.entries(symbols).map(([id,item])=>`<article><div class="symbol-download-preview">${svg(id)}</div><strong>${item.name}</strong><button data-download-media="${id}">Исходник PNG</button></article>`).join('');
    if(!$('#applications').hidden){
    $('#app-previews').innerHTML=['light','dark'].map(theme=>`<article class="app-phone ${theme}" aria-label="${theme==='light'?'Светлая':'Тёмная'} тема приложения"><div class="app-top"><span>9:41</span><span>● ▰</span></div><div class="app-name">${esc(state.name)} <span>♡</span></div><div class="app-content"><small>НОВАЯ КОЛЛЕКЦИЯ</small><h4>Ваш новый<br>любимый образ.</h4><img src="${window.DONA_MEDIA.campaign}" alt="Рубиновая блуза и светлые брюки"><div class="product-caption"><span>Шёлковая блуза<br><small>Ruby · XS–XL</small></span><span>♡</span></div><button type="button" data-theme="${theme}">${state.theme===theme?'Тема выбрана':'Выбрать тему'}</button></div><div class="app-nav">Каталог　　Избранное　　Корзина</div><p class="app-theme-label">${theme==='light'?'Светлая':'Тёмная'} тема ${state.theme===theme?'· выбрана':''}</p></article>`).join('');
    $$('.campaign-photo,.post-photo,.tg-photo,.story-photo').forEach(el=>{el.style.backgroundImage=`${el.matches('.tg-photo,.story-photo')?'linear-gradient(0deg,rgba(35,8,20,.74),transparent 78%),':''}url("${el.classList.contains('photo-detail')?window.DONA_MEDIA.garment:window.DONA_MEDIA.campaign}")`;el.style.backgroundSize='cover';el.style.backgroundPosition='center';});
  }
  }
  function fontDefinitions(){const css=[];for(const sheet of document.styleSheets){try{for(const rule of sheet.cssRules)if(rule.type===CSSRule.FONT_FACE_RULE)css.push(rule.cssText);}catch{}}return `<defs><style>${esc(css.join('\n')+'\n'+Object.values(window.DONA_LICENSES||{}).map(text=>'/* '+text.replaceAll('*/','')+' */').join('\n'))}</style></defs>`;}
  function exportSvg(source){return source.replace('><',`>${fontDefinitions()}<`);}
  async function portableSvg(source){
    const used=Object.entries(window.DONA_MEDIA).filter(([,url])=>source.includes(url));
    const originals=await Promise.all(used.map(async([id,url])=>[url,window.DONA_ORIGINAL?await window.DONA_ORIGINAL(id):url]));
    for(const [url,original] of originals)source=source.replaceAll(url,original);
    await window.DONA_LOAD_LICENSES?.();
    return exportSvg(source);
  }
  async function downloadSvg(source,filename){
    toast('Подготавливаем оригинал для скачивания…');
    try{saveBlob(new Blob([await portableSvg(source)],{type:'image/svg+xml'}),filename);toast('SVG готов');}
    catch(error){console.error(error);toast('Не удалось загрузить оригинал. Повторите скачивание.');}
  }
  async function pngBlob(source,width=1800,height=612){
    await document.fonts.ready;
    source=(await portableSvg(source)).replace('<svg ',`<svg width="${width}" height="${height}" `);
    const url=URL.createObjectURL(new Blob([source],{type:'image/svg+xml'})),img=new Image();
    try{await new Promise((resolve,reject)=>{img.onload=resolve;img.onerror=reject;img.src=url;});const canvas=document.createElement('canvas');canvas.width=width;canvas.height=height;canvas.getContext('2d').drawImage(img,0,0,width,height);return await new Promise((resolve,reject)=>canvas.toBlob(b=>b?resolve(b):reject(new Error('PNG')),'image/png'));}finally{URL.revokeObjectURL(url);}
  }
  function sketchDimensions(source,longSide=4000){
    const values=source.match(/viewBox="([^"]+)"/)[1].split(/\s+/).map(Number),w=values[2],h=values[3];
    return [Math.round(longSide*w/Math.max(w,h)),Math.round(longSide*h/Math.max(w,h))];
  }
  $('#layouts').addEventListener('click',async event=>{
    const button=event.target.closest('[data-sketch]');if(!button)return;
    const layout=button.dataset.sketch,index=Number(layout.slice(-1)),name=state.name;
    const original=generatedLogoSvg(layout),dimensions=sketchDimensions(original);
    const basename=name.replaceAll(' ','-')+'-'+generatedSymbols[index]+'-imagegen';
    const p=palette(),status=button.closest('.sketch-downloads').querySelector('[data-sketch-status]');
    button.disabled=true;status.textContent='Готовим файл…';
    try{
      if(button.dataset.export==='svg')saveBlob(new Blob([await portableSvg(original)],{type:'image/svg+xml'}),basename+'.svg');
      else if(button.dataset.export==='png')saveBlob(await pngBlob(original,...dimensions),basename+'-4000.png');
      else{
        const files=[];
        for(const [tone,color] of [['brand',p[1]],['ivory',p[2]],['black','#000000'],['white','#FFFFFF']]){
          status.textContent=`Готовим версию ${tone}…`;
          const source=original.replace(/style="color:[^"]*"/,`style="color:${color}"`);
          files.push([`${basename}-${tone}.svg`,await portableSvg(source)],[`${basename}-${tone}-4000.png`,await pngBlob(source,...dimensions)]);
        }
        const board=generatedBoards[name],box=name==='DONA'&&index===0?[50,180,1880,430]:index<3?board.boxes[index]:board.extraBoxes[index-3];
        files.push(['README.txt',`${name} — ${generatedLabels[index]}\nPNG: ${dimensions.join(' × ')} px, transparent background, sRGB.\nVariants: brand ${p[1]}, ivory ${p[2]}, black, white.\nSource artwork region: ${box[2]} × ${box[3]} px. A 4000 px export is scaled from this raster original; it does not add new detail. SVG embeds the original raster artwork and a transparency mask, not vector outlines.\nFor web, social media, presentations and small-format print. For embroidery, cutting, engraving or large-format production, commission vector tracing and obtain a proof from the supplier.\nComposition is fixed; retain its aspect ratio. White and ivory variants are intended for dark backgrounds.\n`]);
        saveBlob(await zipFiles(files),basename+'-kit.zip');
      }
      status.textContent='Файл готов. Ссылка также доступна в разделе материалов.';
    }catch(error){console.error(error);status.textContent='Не удалось сохранить. Попробуйте ещё раз.';}
    finally{button.disabled=false;}
  });
  let lastDownloadUrl=null;
  function saveBlob(blob,filename){if(lastDownloadUrl)URL.revokeObjectURL(lastDownloadUrl);lastDownloadUrl=URL.createObjectURL(blob);const a=$('#last-download-link');a.href=lastDownloadUrl;a.download=filename;a.textContent=`Скачать ${filename} · ${(blob.size/1024/1024).toFixed(1)} МБ`;$('#last-download').hidden=false;const preview=$('#last-download-preview');preview.hidden=blob.type!=='image/png';if(!preview.hidden)preview.src=lastDownloadUrl;else preview.removeAttribute('src');a.click();}
  async function mediaBlob(id){return await (await fetch(window.DONA_ORIGINAL?await window.DONA_ORIGINAL(id):window.DONA_MEDIA[id])).blob();}
  document.addEventListener('click',async event=>{
    const media=event.target.closest('[data-download-media]'),icon=event.target.closest('[data-download-icon]'),mockup=event.target.closest('[data-download-mockup]'),theme=event.target.closest('#app-previews [data-theme]');
    if(theme){state.theme=theme.dataset.theme;render();return;}
    if(!media&&!icon&&!mockup)return;
    const button=media||icon||mockup;button.disabled=true;
    try{if(media)saveBlob(await mediaBlob(media.dataset.downloadMedia),media.dataset.downloadMedia+'.png');
      if(icon){const id=icon.dataset.downloadIcon,source=iconSvg(id,icon.dataset.tone==='light'?palette()[2]:palette()[1]);if(icon.dataset.format==='png')saveBlob(await pngBlob(source,1080,1080),id+(icon.dataset.tone==='light'?'-light':'')+'.png');else download(source,'image/svg+xml',id+'.svg');}
      if(mockup)saveBlob(await pngBlob(mockupSvg(mockup.dataset.downloadMockup),1536,1024),mockup.dataset.downloadMockup+'.png');
      toast('Файл подготовлен для скачивания');
    }catch(error){console.error(error);toast('Не удалось сохранить файл. Попробуйте ещё раз.');}finally{button.disabled=false;}
  });
  $('#download-current-svg').addEventListener('click',()=>downloadSvg(logoSvg(state.layout),'dona-logo.svg'));
  $('#download-current-png').addEventListener('click',async()=>{const button=$('#download-current-png');button.disabled=true;try{const source=logoSvg(state.layout);saveBlob(await pngBlob(source,...(isFixed()?sketchDimensions(source):[1800,612])),'dona-logo.png');}catch(e){console.error(e);toast('Не удалось сохранить PNG');}finally{button.disabled=false;}});
  // Standard ZIP container. Compression is native; stored entries are the fallback.
  async function zipFiles(files){
    const enc=new TextEncoder(),local=[],central=[];let offset=0;
    const table=Array.from({length:256},(_,n)=>{for(let k=0;k<8;k++)n=n&1?0xedb88320^(n>>>1):n>>>1;return n>>>0;});
    const crc=bytes=>{let c=0xffffffff;for(const b of bytes)c=table[(c^b)&255]^(c>>>8);return (c^0xffffffff)>>>0;};
    for(const [name,value] of files){const raw=new Uint8Array(await new Blob([value]).arrayBuffer()),filename=enc.encode(name);let data=raw,method=0;
      try{data=new Uint8Array(await new Response(new Blob([raw]).stream().pipeThrough(new CompressionStream('deflate-raw'))).arrayBuffer());method=8;}catch{}
      const checksum=crc(raw),header=new Uint8Array(30+filename.length),v=new DataView(header.buffer);v.setUint32(0,0x04034b50,true);v.setUint16(4,20,true);v.setUint16(6,0x800,true);v.setUint16(8,method,true);v.setUint16(12,33,true);v.setUint32(14,checksum,true);v.setUint32(18,data.length,true);v.setUint32(22,raw.length,true);v.setUint16(26,filename.length,true);header.set(filename,30);local.push(header,data);
      const entry=new Uint8Array(46+filename.length),d=new DataView(entry.buffer);d.setUint32(0,0x02014b50,true);d.setUint16(4,20,true);d.setUint16(6,20,true);d.setUint16(8,0x800,true);d.setUint16(10,method,true);d.setUint16(14,33,true);d.setUint32(16,checksum,true);d.setUint32(20,data.length,true);d.setUint32(24,raw.length,true);d.setUint16(28,filename.length,true);d.setUint32(42,offset,true);entry.set(filename,46);central.push(entry);offset+=header.length+data.length;
    }
    const end=new Uint8Array(22),e=new DataView(end.buffer);e.setUint32(0,0x06054b50,true);e.setUint16(8,files.length,true);e.setUint16(10,files.length,true);e.setUint32(12,central.reduce((n,b)=>n+b.length,0),true);e.setUint32(16,offset,true);return new Blob([...local,...central,end],{type:'application/zip'});
  }
  $('#download-all').addEventListener('click',async()=>{
    const button=$('#download-all'),status=$('#download-progress'),saved=structuredClone(state),files=[];button.disabled=true;
    try{
      // Build all compositions synchronously against one captured palette/font choice.
      for(const [name] of names)for(const symbol of Object.keys(symbols))for(const layout of ['classic','side','signature']){
        state={...saved,name,symbol,layout,brandPlacement:null};files.push([`logos/${name.replaceAll(' ','-')}/${symbol}-${layout}.svg`,logoSvg(layout,false)]);
      }
      for(const [name] of names)for(let i=0;i<5;i++){state={...saved,name,layout:`imagegen-${i}`,brandPlacement:null};files.push([`logos/${name.replaceAll(' ','-')}/imagegen-${i+1}.svg`,logoSvg(state.layout)]);}
      state=saved;files.push(['current-selection.svg',logoSvg(state.layout)],['palettes.json',JSON.stringify(seasonalPalettes,null,2)],['selection.json',JSON.stringify(saved,null,2)],['README.txt','Dona Brand Studio — 60 editable compositions + 20 fixed ImageGen sketches. SVG files contain embedded raster ImageGen symbols and editable text, not fully vector marks. Fonts are embedded under OFL. PNG symbols are original transparent assets. Icons are vector. Review final artwork and minimum detail size with your printer.']);
      status.textContent='Загружаем оригиналы для архива…';
      for(const file of files)if(file[0].endsWith('.svg'))file[1]=await portableSvg(file[1]);
      for(const [family,license] of Object.entries(window.DONA_LICENSES||{}))files.push([`licenses/${family}-OFL.txt`,license]);
      for(const id of Object.keys(symbols))files.push([`symbols/${id}.png`,await mediaBlob(id)]);
      for(const id of Object.keys(themedIcons)){const source=iconSvg(id);files.push([`icons/${id}-light.svg`,iconSvg(id,palette()[2])],[`icons/${id}-light.png`,await pngBlob(iconSvg(id,palette()[2]),1080,1080)]);files.push([`icons/${id}.svg`,source],[`icons/${id}.png`,await pngBlob(source,1080,1080)]);status.textContent=`Готовим иконки: ${themedIcons[id][0]}…`;}
      status.textContent='Упаковываем 80 логотипов и материалы…';saveBlob(await zipFiles(files),'dona-brand-assets.zip');status.textContent='Архив готов: 80 логотипов, текущий вариант, пять знаков, восемь иконок в SVG и PNG, палитры.';
    }catch(error){console.error(error);status.textContent='Не удалось собрать архив. Отдельные материалы можно скачать кнопками выше.';}finally{button.disabled=false;}
  });

  function showBookPage(focus=false){
    const id=decodeURIComponent(location.hash.slice(1)||'identity');
    const target=document.getElementById(id),page=target?.closest('[data-page]')||$('#identity');
    $$('[data-page]').forEach(el=>el.hidden=el!==page);
    $$('[data-route]').forEach(link=>{const active=link.dataset.route===page.id;link.classList.toggle('active',active);if(active)link.setAttribute('aria-current','page');else link.removeAttribute('aria-current');});
    document.title=`${page.querySelector('h1').textContent} — Dona Brand Studio`;
    renderClient();
    requestAnimationFrame(()=>{
      refreshPlacementSurfaces();
      (target&&page.contains(target)?target:page).scrollIntoView({block:'start',behavior:'instant'});
      if(focus){const heading=page.querySelector('h1');heading.focus({preventScroll:true});}
    });
  }
  window.addEventListener('hashchange',()=>showBookPage(true));
  render();
  showBookPage();
  $('main').inert=false;
  $('#loading-state').hidden=true;
  document.fonts.ready.then(renderLogos);
})().catch(error=>{
  console.error('Brandbook startup:',error);
  const el=document.querySelector('#loading-state');if(el){el.hidden=false;el.textContent='Не удалось запустить брендбук. Обновите страницу, чтобы загрузить актуальную версию.';}
});
