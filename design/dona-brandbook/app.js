(() => {
  'use strict';
  const names = [
    ['Dona Atelier','АТЕЛЬЕ','Камерный бренд с вниманием к крою и деталям.','ДОНА АТЕЛЬЕ'],
    ['Dona Muse','ВДОХНОВЕНИЕ','Современная женственность, искусство быть собой.','ДОНА МЬЮЗ'],
    ['Durdona Studio','ЛИЧНАЯ ИСТОРИЯ','Полное имя основательницы и авторский подход.','ДУРДОНА СТУДИО'],
    ['Dear Dona','ТЁПЛОЕ ОБРАЩЕНИЕ','Как личное письмо: близко, бережно, с характером.','ДИР ДОНА'],
    ['Dona','ЧИСТАЯ ОСНОВА','Коротко, ясно и без привязки к одному настроению.','ДОНА'],
    ['DŌNA','ГРАФИЧНОСТЬ','Знакомое имя с выразительным акцентом над O.','ДОНА'],
    ['Maison Dona','ДОМ БРЕНДА','Камерный мир одежды и аксессуаров.','МЕЗОН ДОНА'],
    ['Dona Belle','МЯГКАЯ РОМАНТИКА','Изящное, мелодичное имя для женственного гардероба.','ДОНА БЕЛЬ'],
    ['Dona Cherry','ЛЁГКАЯ ИГРА','Ягодный акцент без чрезмерной сладости.','ДОНА ЧЕРРИ'],
    ['Dona Bloom','РАСЦВЕТ','Естественность, движение и мягкие цветочные детали.','ДОНА БЛУМ'],
    ['Dona & Co.','СВОЙ КРУГ','Дружелюбная городская марка на каждый день.','ДОНА ЭНД КО'],
    ['Durdona','ИМЯ КАК ПОДПИСЬ','Самый личный вариант с сохранением полного имени.','ДУРДОНА']
  ];
  const symbols = {
  "bow": {
    "name": "Шёлковый бант",
    "hint": "Мягко собранная лента: объёмные петли и свободные концы. Для ярлыков, вкладышей и упаковки.",
    "path": "<path d=\"M44 43C35 31 19 22 15 30C10 40 18 56 29 53L44 47M56 43C65 31 81 22 85 30C90 40 82 56 71 53L56 47M43 51C40 62 32 70 24 76L35 75 38 85C45 74 48 62 48 53M57 51C60 62 68 70 76 76L65 75 62 85C55 74 52 62 52 53\"/><path d=\"M45 40C48 39 52 39 55 40L56 51C52 54 48 54 44 51Z\"/><path d=\"M22 36C29 38 35 42 40 45M78 36C71 38 65 42 60 45\" stroke-width=\".8\"/>"
  },
  "pearl": {
    "name": "Жемчужина",
    "hint": "Круглая жемчужина в тонкой разомкнутой оправе. Спокойный ювелирный акцент для повседневного гардероба.",
    "path": "<path d=\"M69 22C46 7 19 24 19 50C19 74 43 89 64 79C74 74 80 65 81 56\"/><path d=\"M72 47C74 62 64 72 51 73C37 74 27 63 28 49C28 35 37 26 49 26C61 25 71 33 72 47Z\"/><path d=\"M36 43C38 37 43 34 49 34\"/><circle cx=\"79\" cy=\"30\" r=\"3.5\" fill=\"currentColor\" stroke=\"none\"/>"
  },
  "cherry": {
    "name": "Вишня",
    "hint": "Две ягоды, тонкие плодоножки и вытянутый лист. Небольшая игривая деталь на спокойном фоне.",
    "path": "<path d=\"M32 61C44 50 54 31 57 17M70 64C71 46 65 28 57 17M56 20C44 12 31 15 24 26C35 31 49 29 56 20Z\"/><path fill=\"currentColor\" fill-rule=\"evenodd\" stroke=\"none\" d=\"M32 58C22 52 12 60 13 71C14 84 25 91 35 86C47 82 51 68 43 61C40 58 36 57 32 58ZM22 65C19 68 19 74 22 77C20 72 22 68 25 66ZM69 61C59 55 50 63 52 75C54 88 66 93 76 87C88 81 91 67 82 62C78 59 73 59 69 61ZM61 67C58 70 59 76 62 79C60 74 61 70 64 68Z\"/>"
  },
  "petal": {
    "name": "Камелия",
    "hint": "Ботанический рисунок: широкие лепестки раскрываются вокруг плотной сердцевины. Листья дополняют цветок на упаковке; в аватаре остаётся только венчик.",
    "path": "<path d=\"M32 72C21 69 12 77 10 87C21 90 33 84 37 76M64 73C76 68 86 72 91 82C80 89 70 85 64 78M15 84L30 77M71 78L85 81\"/><path d=\"M36 25C34 13 47 9 55 16C64 9 75 18 72 29C88 25 96 40 85 50C96 62 84 78 70 73C65 87 47 87 41 76C27 83 12 73 16 59C4 48 13 30 28 33C26 27 29 23 36 25Z\"/><path d=\"M36 25C44 24 48 29 50 34M72 29C70 36 65 39 59 40M85 50C78 55 70 53 65 50M70 73C65 66 61 61 60 57M41 76C44 69 45 64 46 60M16 59C27 60 34 56 38 52M28 33C29 42 33 46 38 47\"/><path d=\"M38 42C33 32 43 26 50 34C57 27 67 34 62 43C73 43 75 55 64 59C63 70 50 70 47 61C36 67 29 56 38 49C32 46 33 41 38 42Z\"/><path d=\"M45 43C46 36 57 37 57 45C65 47 61 56 54 54C50 61 42 54 46 49C39 47 40 42 45 43Z\"/><path d=\"M49 46L53 49M52 43L54 46M49 51L52 51\"/>"
  },
  "monogram": {
    "name": "D · шёлковый росчерк",
    "hint": "Авторская каллиграфическая D: наклонный штрих, широкий просвет и один плавный росчерк. Как подпись основательницы на ярлыке одежды.",
    "path": "<path fill=\"currentColor\" stroke=\"none\" d=\"M43 21C45 20 49 20 52 21C46 37 40 56 35 71C32 80 26 85 20 84C15 83 14 78 17 76C19 74 22 76 22 78C22 81 25 81 27 77C32 66 38 37 43 21Z\"/><path fill=\"currentColor\" stroke=\"none\" d=\"M22 29C13 14 35 8 58 13C82 18 92 34 86 53C81 71 66 82 46 82C33 82 23 75 26 68C29 60 44 64 54 70C65 77 77 86 87 78C84 88 69 83 56 75C44 67 30 61 28 69C26 75 35 80 46 80C64 80 76 65 79 49C83 32 74 19 56 15C36 11 18 16 22 29Z\"/>"
  },
  "kiss": {
    "name": "Отпечаток помады",
    "hint": "Мягкий отпечаток с прозрачными пробелами помады. Для благодарственной карточки и небольших акцентов в историях.",
    "path": "<path fill=\"currentColor\" fill-rule=\"evenodd\" stroke=\"none\" d=\"M10 47C20 44 26 29 36 30C42 30 46 36 50 37C55 37 58 30 65 30C75 30 82 44 90 47C75 54 64 45 51 48C37 44 26 54 10 47ZM14 55C27 59 39 51 50 54C63 51 75 59 87 54C79 66 66 76 51 77C35 77 21 68 14 55ZM28 38L31 47 33 46 30 37ZM41 37L43 44 45 44 43 38ZM64 36L61 44 63 44 66 36ZM76 43L73 48 75 49 78 44ZM28 61L33 68 35 68 31 61ZM42 60L44 72 46 72 45 60ZM56 60L55 71 57 71 59 60ZM69 61L65 69 67 68 72 61Z\"/>"
  },
  "swan": {
    "name": "Лебедь",
    "hint": "Небольшая голова, длинная S-образная шея и приподнятое крыло. Плавный силуэт поддерживает тему движения и пластики ткани.",
    "path": "<path d=\"M13 67C24 64 29 49 40 43C42 51 50 60 61 62C67 59 72 53 71 47C70 39 60 34 58 25C55 16 60 8 68 9C74 9 77 14 75 18C73 21 68 20 66 22C62 28 72 34 77 40C88 54 83 72 69 80C53 89 25 85 13 67Z\"/><path fill=\"currentColor\" stroke=\"none\" d=\"M75 14L84 20 74 19Z\"/><circle cx=\"70.5\" cy=\"14\" r=\"1\" fill=\"currentColor\" stroke=\"none\"/><path d=\"M40 43C41 55 50 67 65 69C54 81 33 81 21 70\"/><path d=\"M29 70C35 66 38 60 40 55M34 73C41 68 44 65 45 62M41 75C47 72 50 69 51 67\" stroke-width=\".9\"/>"
  },
  "shell": {
    "name": "Ракушка",
    "hint": "Симметричный веер с мягкими фестонами. Природный мотив для льняных капсул и летней упаковки.",
    "path": "<path d=\"M43 83C33 76 19 64 13 52C8 42 12 32 21 31C19 20 29 14 38 19C41 7 59 7 62 19C71 14 81 20 79 31C88 32 92 42 87 52C81 64 67 76 57 83L59 89H41Z\"/><path d=\"M46 79C36 59 28 43 21 31M49 79C45 53 40 30 38 19M51 79C55 53 60 30 62 19M54 79C64 59 72 43 79 31M43 79C30 64 20 54 14 46M57 79C70 64 80 54 86 46M50 75V21\"/>"
  },
  "tulip": {
    "name": "Тюльпан · ботаника",
    "hint": "Чашевидный цветок с перекрывающимися лепестками, изогнутым стеблем и длинным листом. Рисунок для сезонной капсулы и вкладышей.",
    "path": "<path d=\"M51 50C37 48 28 35 30 17C43 20 51 31 51 50M51 50C63 49 73 35 72 16C60 19 51 31 51 50M38 20C39 14 43 10 47 8C53 11 58 16 60 22M53 19C55 14 58 11 61 10L66 18M51 50C55 63 51 79 43 93M49 79C33 75 21 57 22 44C31 50 33 64 49 79ZM51 68C65 61 72 50 77 40C79 58 68 75 47 86\"/><path d=\"M35 25C36 35 41 43 47 46M66 25C65 35 60 43 56 46M27 53C31 64 37 71 44 76M72 51C67 66 57 75 50 79\" stroke-width=\".7\"/>"
  }
};

  // Closed silhouettes and separate interior cuts keep every version transparent.
  const flower='M36 27C31 17 43 11 51 19C59 10 72 17 68 29C82 24 91 37 80 47C92 56 83 71 70 66C68 80 51 84 44 72C33 82 19 72 24 60C10 57 11 40 25 37C20 27 29 20 36 27Z';
  const flowerInner='M40 38C36 29 47 26 52 34C61 27 69 36 63 43C74 47 69 58 60 56C57 66 45 65 43 56C32 59 28 48 37 43';
  const leaves='M29 75C21 72 15 78 14 85C23 87 31 82 33 76ZM68 77C74 72 84 74 87 81C81 87 72 85 68 77Z';
  symbols.petal.path=`<path d="${flower}"/><path d="${flowerInner}"/><path d="M46 43C46 37 56 36 57 44C64 46 60 55 54 53C49 59 42 52 46 48C40 48 40 41 46 43Z"/><path d="${leaves}M19 82L28 78M74 79L82 81"/>`;
  symbols.petal.hint='Камелия с компактными листьями ниже венчика. Контуры листьев замкнуты и отделены от лепестков; оба исполнения сохраняют один рисунок.';
  const pathsOf=html=>[...html.matchAll(/<path\b[^>]*\bd="([^"]+)"/g)].map(m=>m[1]);
  const cherryFruit=pathsOf(symbols.cherry.path)[1].split('ZM22')[0]+'Z M69 61C59 55 50 63 52 75C54 88 66 93 76 87C88 81 91 67 82 62C78 59 73 59 69 61Z';
  const lips=pathsOf(symbols.kiss.path)[0].split('ZM28')[0]+'Z';
  const solid={
    bow:['M44 43C28 23 12 26 17 43C21 56 33 51 44 47ZM56 43C72 23 88 26 83 43C79 56 67 51 56 47ZM45 53L29 78 39 75 43 82 49 53ZM55 53L71 78 61 75 57 82 51 53ZM45 40H55V52H45Z','M22 36L38 44M78 36L62 44'],
    pearl:['M72 47C74 62 64 72 51 73C37 74 27 63 28 49C28 35 37 26 49 26C61 25 71 33 72 47Z','M36 43Q40 33 49 34'],
    cherry:[cherryFruit,'M22 66Q19 70 22 75M61 68Q59 73 62 77'],
    petal:[flower+' '+leaves,flowerInner+'M46 44Q50 37 57 44Q64 51 55 54Q46 59 44 50M19 82L28 78M74 79L82 81'],
    swan:[pathsOf(symbols.swan.path)[0].replace('C74 9 77 14 75 18C73 21 68 20 66 22','C72 9 75 12 75 14L84 20L74 19C71 21 68 20 66 22'),'M30 69Q45 81 62 69Q45 64 41 53'],
    shell:[pathsOf(symbols.shell.path)[0],pathsOf(symbols.shell.path)[1]],
    tulip:['M50 50C35 47 28 33 30 17C42 20 49 31 50 50ZM53 50C53 31 62 20 72 17C73 33 66 47 53 50ZM38 18Q40 12 47 8Q55 12 58 21L51 29ZM25 49C38 58 36 68 49 78C33 76 24 62 25 49ZM52 68Q70 56 77 42Q78 69 48 85Z','M33 63L44 75M60 72L70 57']
  };
  let symbolSequence=0;
  function symbolBody(id,style=state.symbolStyle){
    const art=symbols[id],uid='ink-'+(++symbolSequence);
    if(id==='monogram'){
      if(style==='filled')return art.path;
      // Mask the filled union out of its wider stroke: only a clean outer border remains.
      const shapes=pathsOf(art.path).map(d=>`<path d="${d}"/>`).join('');
      return `<defs><mask id="${uid}" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><rect width="100" height="100" fill="white" stroke="none"/><g fill="black" stroke="none">${shapes}</g></mask></defs><g mask="url(#${uid})" fill="none" stroke="currentColor" stroke-width="2.7" stroke-linecap="round" stroke-linejoin="round">${shapes}</g>`;
    }
    let shape,cuts;
    if(id==='kiss'){
      shape=lips;cuts='M29 38L32 46M42 38L44 44M65 36L62 44M76 43L74 48M30 61L34 68M43 60L45 72M58 60L56 71M71 61L66 69';
    }else [shape,cuts]=solid[id];
    let extras='';
    if(id==='pearl')extras='<path d="M69 22C46 7 19 24 19 50C19 74 43 89 64 79C74 74 80 65 81 56"/><circle cx="79" cy="30" r="3" fill="currentColor" stroke="none"/>';
    if(id==='cherry')extras='<path d="M32 57C43 46 53 30 57 17M70 59C70 43 64 28 57 17M56 20C44 12 31 15 24 26C35 31 49 29 56 20Z"/>';
    if(id==='tulip')extras='<path d="M51 51Q56 69 43 93"/>';
    const shared=`<path d="${shape}"/>`;
    if(style==='outline')return `<defs><clipPath id="${uid}">${shared}</clipPath></defs><g fill="none" stroke="currentColor" stroke-width="1.35">${shared}<path d="${cuts}" clip-path="url(#${uid})"/>${extras}</g>`;
    return `<defs><mask id="${uid}" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><rect width="100" height="100" fill="white" stroke="none"/><path d="${cuts}" fill="none" stroke="black" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></mask></defs><path d="${shape}" fill="currentColor" stroke="none" mask="url(#${uid})"/>${extras}`;
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
    {id:'marck',name:'Marck Script · личная подпись',family:'"Marck Script", cursive',hint:'Живая рукописная линия. Латиница и кириллица. Используйте в логотипе, а не в мелком тексте.'},
    {id:'editorial',name:'Georgia · редакционная',family:'Georgia, "Times New Roman", serif',hint:'Контрастная антиква. Спокойная элегантность и читаемая кириллица.'},
    {id:'classic',name:'Palatino · мягкая классика',family:'"Palatino Linotype", "Book Antiqua", Palatino, serif',hint:'Пластичные, широкие буквы. Мягкий характер ателье.'},
    {id:'modern',name:'Segoe UI · современная',family:'"Segoe UI", Arial, sans-serif',hint:'Чистый гротеск. Уверенный городской минимализм.'},
    {id:'literary',name:'Times New Roman · литературная',family:'"Times New Roman", Times, serif',hint:'Тонкая классическая форма. Личная, почти книжная интонация.'}
  ];
  const concepts = [
    {name:'Жемчужное ателье',en:'PEARL ATELIER',description:'Тонкая лента. Тёплый свет.',symbol:'bow',brand:'Dona Atelier',font:'cormorant',caption:'Каждый день — немного особенный',tagline:'Нежность — это характер.',summer:[['Пудра','#743747','#F7EEE7','#E5CBD0'],['Персик','#86503E','#FBF2E5','#EAD0BC']],winter:[['Бордо','#612C3E','#F1E9DF','#CDB9C0'],['Какао','#62483D','#F2EDE6','#D4C3B5']]},
    {name:'Вишнёвое свидание',en:'CHERRY RENDEZVOUS',description:'Ягодный акцент. Лёгкая улыбка.',symbol:'cherry',brand:'Dear Dona',font:'prata',caption:'Маленькие радости вашего гардероба',tagline:'Влюбиться в обычный день.',summer:[['Вишня','#8B3546','#FFF4E5','#EBC3B7'],['Роза','#874958','#FCF2ED','#EDCDCB']],winter:[['Мерло','#58283E','#F4EBDD','#C9B7BE'],['Слива','#624563','#F0EAF0','#CBBACD']]},
    {name:'Тихая муза',en:'QUIET MUSE',description:'Чистые линии. Свобода быть собой.',symbol:'pearl',brand:'Dona Muse',font:'manrope',caption:'Меньше случайного. Больше вашего.',tagline:'Красиво быть собой.',summer:[['Шалфей','#4B6255','#F2F1E7','#D1D9C5'],['Небо','#466375','#F1F0E9','#CDDDE1']],winter:[['Хвоя','#344F49','#ECEAE1','#BBCBC3'],['Графит','#484B59','#F0ECE6','#C7C8D0']]},
    {name:'Сад камелий',en:'CAMELLIA GARDEN',description:'Ботаническая графика и чайная роза.',symbol:'petal',brand:'Dona Bloom',font:'cormorant',caption:'Одежда, которую выбирают с чувством',tagline:'Своя история в каждом образе.',summer:[['Розовый сад','#87565E','#FBF5EF','#DCCBBE'],['Листва','#506048','#F5F2E7','#CBD1B8']],winter:[['Сухая роза','#70464D','#F0E7E1','#C9ACAE'],['Олива','#4D5240','#EEE9DD','#BCBBA5']]},
    {name:'Балетная линия',en:'BALLET LINE',description:'Лебедь, плавный силуэт и пыльная сирень.',symbol:'swan',brand:'Dona Belle',font:'prata',caption:'Лёгкость, которую чувствуешь',tagline:'В движении — ваш характер.',summer:[['Пуанты','#746071','#FAF3F1','#DCCDD9'],['Дымка','#586773','#F4F2EF','#CDD4DC']],winter:[['Сумерки','#4C3C56','#EEE8EF','#BCACCA'],['Пепел','#56525A','#ECE8E4','#C8C1C9']]},
    {name:'Утро у моря',en:'RIVIERA MORNING',description:'Лён, ракушка и спокойный синий.',symbol:'shell',brand:'Maison Dona',font:'cormorant',caption:'Простые вещи. Красивые дни.',tagline:'Больше воздуха в каждом дне.',summer:[['Ривьера','#426478','#FBF5E9','#D9D0BB'],['Терракота','#975F49','#FCF2E4','#E2C4AE']],winter:[['Глубина','#2D475B','#ECE8DF','#B9C8CC'],['Песчаник','#715546','#EEE7DC','#C8B8A4']]},
    {name:'Личная подпись',en:'PERSONAL SIGNATURE',description:'Читаемая D, сливочный и шоколадный.',symbol:'monogram',brand:'Durdona Studio',font:'prata',caption:'Гардероб с личным почерком',tagline:'Вещи, которые остаются с вами.',summer:[['Ваниль','#584336','#FAF3E3','#DAC9B1'],['Чай','#6C5145','#F8F0E8','#D7BEB1']],winter:[['Эспрессо','#392C2B','#EFE5DA','#BBA59C'],['Чернила','#303643','#F2EEE5','#B9BFCA']]}

  ];
  const conceptStories=[
    'Камерное ателье: кремовая бумага, тонкая лента и выразительная антиква. Подходит для женственных капсул и внимательной работы с деталями.',
    'Тёплая и общительная марка. Вишнёвый акцент делает упаковку узнаваемой, а спокойная типографика сохраняет взрослую интонацию.',
    'Современный повседневный гардероб. Чистый набор, прохладные полутона и небольшой ювелирный знак оставляют главное место одежде.',
    'Романтика ботанического альбома. Камелия раскрывается на вкладышах и упаковке, а в публикациях соседствует с фотографиями тканей.',
    'Пластика танца без театральности. Лебедь, вытянутые буквы и мягкая сирень задают ощущение движения и лёгкости.',
    'Неспешный ритм южного утра. Природные фактуры, льняная основа и прохладный синий работают для расслабленного городского гардероба.',
    'Личный бренд с характером основательницы. Каллиграфическая D и глубокий шоколадный оттенок уместны в тиснении, на ярлыках и фирменной бумаге.'
  ];
  const initial = {concept:0,name:'Dona Atelier',symbol:'bow',font:'cormorant',season:'summer',theme:'light',layout:'classic',customName:'',choices:{},custom:{},symbolStyle:'outline',brandPlacement:null,placementProfiles:{},editor:{format:'post',x:50,y:25,angle:0,size:25,transparent:false,showName:true}};
  let state = structuredClone(initial);
  const storageKey = 'dona-brandbook-v1';
  function validate(raw) {
    if(!raw || typeof raw!=='object') return;
    if(Number.isInteger(raw.concept)&&concepts[raw.concept])state.concept=raw.concept;
    if(typeof raw.name==='string'&&raw.name.trim())state.name=raw.name.trim().slice(0,32);
    if(typeof raw.customName==='string')state.customName=raw.customName.slice(0,32);
    if(['classic','side','botanical','cameo','ribbon','seal','initial','signature','pearl'].includes(raw.layout))state.layout=raw.layout;
    if(['outline','filled'].includes(raw.symbolStyle))state.symbolStyle=raw.symbolStyle;
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
      if([0,1].includes(raw.choices?.[k]))state.choices[k]=raw.choices[k];
      if(/^#[\da-f]{6}$/i.test(raw.custom?.[k]))state.custom[k]=raw.custom[k];
    }
  }
  function placementProfile(raw){
    if(!raw||typeof raw!=='object'||!raw.editor)return null;
    const clean=e=>{
      if(!e||!['x','y','angle','size'].every(k=>typeof e[k]==='number'&&Number.isFinite(e[k])))return null;
      return {x:Math.max(0,Math.min(100,e.x)),y:Math.max(0,Math.min(100,e.y)),angle:Math.max(-180,Math.min(180,e.angle)),size:Math.max(8,Math.min(70,e.size)),format:['post','story','card','avatar'].includes(e.format)?e.format:'post'};
    };
    const editor=clean(raw.editor);if(!editor)return null;
    return {editor:{...editor,transparent:raw.editor.transparent===true,showName:raw.editor.showName!==false},brandPlacement:clean(raw.brandPlacement)};
  }
  function rememberPlacement(){
    state.placementProfiles[state.symbol==='monogram'?'monogram':'shared']=structuredClone({editor:state.editor,brandPlacement:state.brandPlacement});
  }
  function selectSymbol(id){
    const previous=state.symbol==='monogram'?'monogram':'shared',next=id==='monogram'?'monogram':'shared';
    if(previous!==next){
      rememberPlacement();
      const saved=state.placementProfiles[next];
      if(saved){state.editor=structuredClone(saved.editor);state.brandPlacement=structuredClone(saved.brandPlacement);}
      else if(next==='monogram'){
        state.editor={...state.editor,x:50,y:20,angle:0};
        state.brandPlacement={x:50,y:20,angle:0,size:state.editor.size,format:state.editor.format};
      }
    }
    state.symbol=id;
  }
  try {
    const raw=window.DONA_SNAPSHOT || JSON.parse(localStorage.getItem(storageKey));validate(raw);
    for(const key of ['shared','monogram']){const profile=placementProfile(raw?.placementProfiles?.[key]);if(profile)state.placementProfiles[key]=profile;}
    // Upgrade an already selected D without retaining another symbol's placement.
    if(state.symbol==='monogram'&&!state.placementProfiles.monogram){state.symbol='bow';selectSymbol('monogram');}
  }catch{}
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
    $('#board-id').textContent=`CONCEPT 0${state.concept+1} / ${state.season.toUpperCase()}`;$('#hero-number').textContent=`0${state.concept+1}—0${concepts.length}`;
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
    $('#summary').textContent=`${state.name} · ${symbols[state.symbol].name} · ${f.name.split(' · ')[0]} · ${state.season==='summer'?'Лето':'Зима'} / ${state.custom[key]?'Свой цвет':p[0]} · ${state.theme==='light'?'Светлая':'Тёмная'} тема`;
    try{localStorage.setItem(storageKey,JSON.stringify(state));}catch{}
  }
  const layouts={
    classic:['Знак над именем','Основной логотип для упаковки, сайта и обложек.'],
    side:['Горизонтальная подпись','Компактная композиция для шапки профиля и этикетки.'],
    botanical:['Ботаническая виньетка','Цветок завершает набор, оставляя имя целым.'],
    cameo:['Камея','Иллюстрация в медальоне и спокойная подпись под ней.'],
    ribbon:['Завязанная лента','Бант связывает две тонкие линии над названием.'],
    seal:['Фирменная печать','Круглый знак для наклейки, вкладыша и упаковочной бумаги.'],
    initial:['Авторский инициал','Рисованная D открывает слово; остальные буквы сохраняют ритм.'],
    signature:['Личная открытка','Небольшой отпечаток рядом с подписью, как финальный жест.'],
    pearl:['Жемчужная подвеска','Жемчужина завершает тонкую линию под полным названием.']
  };
  function availableLayouts(){
    const map={bow:['classic','ribbon','side','seal'],pearl:['classic','pearl','side','cameo'],cherry:['classic','side','seal'],petal:['classic','botanical','side','cameo','seal'],monogram:['classic','side','seal'],kiss:['signature','side','classic'],swan:['classic','cameo','side'],shell:['classic','cameo','side','seal'],tulip:['classic','botanical','side']};
    const list=[...map[state.symbol]];
    if(state.symbol==='monogram'&&/^D/i.test(state.name)&&['cormorant','prata','editorial','classic','literary'].includes(state.font))list.splice(1,0,'initial');
    return list;
  }
  function logoSvg(layout,applyPlacement=true){
    const f=fonts.find(f=>f.id===state.font),p=palette(),text=state.name;
    const ctx=document.createElement('canvas').getContext('2d');ctx.font=`100px ${f.family}`;
    const width=ctx.measureText(text).width||100;
    const label=(value,px,y,size=100,anchor='middle')=>`<text x="${px}" y="${y}" text-anchor="${anchor}" font-family="${esc(f.family)}" font-size="${size}" fill="currentColor" stroke="none">${esc(value)}</text>`;
    const word=(cx,baseline,maxWidth=760,maxSize=100,value=text)=>{const w=ctx.measureText(value).width||100;return label(value,cx,baseline,Math.min(maxSize,maxWidth/w*100));};
    const mark=(px,y,size)=>`<g transform="translate(${px} ${y}) scale(${size/100})" fill="none" stroke="currentColor" stroke-width="1.35" stroke-linecap="round" stroke-linejoin="round">${symbolBody(state.symbol)}</g>`;
    const fine=(d)=>`<path d="${d}" fill="none" stroke="currentColor" stroke-width="1"/>`;
    let body='';
    if(layout==='classic')body=mark(444,18,112)+word(500,238);
    if(layout==='side')body=mark(100,97,140)+fine('M275 118v108')+word(600,205,550,88);
    if(layout==='botanical')body=word(437,194,620,86)+mark(770,100,145)+fine('M120 219h570');
    if(layout==='cameo')body=`<ellipse cx="500" cy="100" rx="88" ry="86" fill="none" stroke="currentColor" stroke-width="1"/>`+mark(433,30,134)+word(500,277,750,80);
    if(layout==='ribbon')body=fine('M240 94C320 79 386 109 454 87M546 87c70 24 133-8 214 7')+mark(451,34,98)+word(500,239);
    if(layout==='seal')body=`<circle cx="500" cy="165" r="145" fill="none" stroke="currentColor" stroke-width="1.5"/><circle cx="500" cy="165" r="136" fill="none" stroke="currentColor" stroke-width=".6"/>`+mark(454,48,92)+word(500,202,230,55)+`<text x="500" y="245" text-anchor="middle" font-family="Manrope,sans-serif" font-size="9" letter-spacing="3">ЖЕНСКАЯ ОДЕЖДА</text>`;
    if(layout==='initial'){
      const tail=text.slice(1),tw=ctx.measureText(tail).width,scale=Math.min(1,780/(tw+126)),left=(1000-(tw+126)*scale)/2;
      body=`<g transform="translate(${left} 95) scale(${scale})">${mark(-5,3,132)}${label(tail,124,109,100,'start')}</g>`;
    }
    if(layout==='signature')body=word(475,184,680,94)+`<g transform="rotate(-14 820 232)">${mark(774,190,100)}</g>`+`<text x="470" y="232" text-anchor="middle" font-family="Manrope,sans-serif" font-size="11" letter-spacing="3">С ЛЮБОВЬЮ К ДЕТАЛЯМ</text>`;
    if(layout==='pearl')body=word(500,172)+fine('M300 212h160m80 0h160')+mark(470,189,60);
    let viewBox='0 0 1000 340';
    if(applyPlacement&&state.brandPlacement){const composed=proportionalLockup(ctx,label);body=composed.body;viewBox=composed.viewBox;}
    const layoutTitle=applyPlacement&&state.brandPlacement?'Свободное размещение':layouts[layout][0];
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox}" role="img" aria-label="${esc(text)} — ${layoutTitle}" style="color:${p[1]}"><title>${esc(text)} — ${layoutTitle}</title>${body}</svg>`;
  }
  function renderLogos(){
    const allowed=availableLayouts();
    if(!allowed.includes(state.layout))state.layout=allowed[0];
    const logo=logoSvg(state.layout),focused=$('#layouts').contains(document.activeElement)?document.activeElement.closest('[data-layout]')?.dataset.layout:null;
    $('#hero-logo').innerHTML=logo;$$('[data-lockup]').forEach(el=>el.innerHTML=logo);
    $('#layouts').innerHTML=allowed.map(id=>`<button data-layout="${id}" aria-pressed="${id===state.layout&&!state.brandPlacement}">${logoSvg(id,false)}<strong>${layouts[id][0]}</strong><span>${layouts[id][1]}</span></button>`).join('');
    if(focused)$('#layouts').querySelector(`[data-layout="${focused}"]`)?.focus({preventScroll:true});
    $('#layout-note').textContent=`${symbols[state.symbol].name}: ${allowed.length} ${allowed.length===3||allowed.length===4?'композиции':'композиций'}, подобранных под форму и характер знака.`;
    $('#symbol-study').innerHTML=svg(state.symbol);$('#symbol-title').textContent=symbols[state.symbol].name;$('#symbol-description').textContent=symbols[state.symbol].hint;
    renderEditor();
    refreshPlacementSurfaces();
    $('#small-avatar').innerHTML=svg(state.symbol,true);$('#tiny-avatar').innerHTML=svg(state.symbol,true);
    try{localStorage.setItem(storageKey,JSON.stringify(state));}catch{}
  }
  let toastTimer;
  function toast(text){$('#toast').textContent=text;$('#toast').classList.add('visible');clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('#toast').classList.remove('visible'),3500);}
  $('#name').addEventListener('change',e=>{if(e.target.value==='__custom'){state.name=state.customName.trim()||'Ваш бренд';$('#custom-name').focus();}else state.name=e.target.value;render();});
  $('#custom-name').addEventListener('input',e=>{const start=e.target.selectionStart,end=e.target.selectionEnd;state.customName=e.target.value;state.name=state.customName.trim()||'Ваш бренд';render();e.target.setSelectionRange(start,end);});
  $('#layouts').addEventListener('click',e=>{const b=e.target.closest('[data-layout]');if(b){state.layout=b.dataset.layout;state.brandPlacement=null;render();}});
  $('#symbol-style').addEventListener('click',e=>{const b=e.target.closest('[data-style]');if(b){state.symbolStyle=b.dataset.style;render();}});
  $('#font').addEventListener('change',e=>{state.font=e.target.value;render();document.fonts.load(`100px ${fonts.find(f=>f.id===state.font).family}`).then(renderLogos);});
  $('#accent').addEventListener('input',e=>{state.custom[`${state.concept}-${state.season}`]=e.target.value;render();});
  for(const id of ['#symbols','#symbol-gallery'])$(id).addEventListener('click',e=>{const b=e.target.closest('[data-symbol]');if(b){selectSymbol(b.dataset.symbol);render();}});
  $('#seasons').addEventListener('click',e=>{const b=e.target.closest('[data-season]');if(b){state.season=b.dataset.season;render();}});
  $('#themes').addEventListener('click',e=>{const b=e.target.closest('[data-theme]');if(b){state.theme=b.dataset.theme;render();}});
  $('#palettes').addEventListener('click',e=>{const b=e.target.closest('[data-palette]');if(b){const k=`${state.concept}-${state.season}`;state.choices[k]=Number(b.dataset.palette);delete state.custom[k];render();}});
  $('#concepts').addEventListener('click',e=>{const b=e.target.closest('[data-concept]');if(!b)return;state.concept=Number(b.dataset.concept);const c=concepts[state.concept];state.name=c.brand;selectSymbol(c.symbol);state.font=c.font;render();toast(`Выбран концепт «${c.name}»`);});
  $('#name-cards').addEventListener('click',e=>{const b=e.target.closest('[data-name]');if(!b)return;state.name=b.dataset.name;render();$('#studio').scrollIntoView({behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'});toast(`Название: ${state.name}`);});
  $('#reset').addEventListener('click',()=>{const current=state.concept;state=structuredClone(initial);state.concept=current;const c=concepts[current];state.name=c.brand;selectSymbol(c.symbol);state.font=c.font;render();toast('Восстановлены настройки концепта');});
  function download(content,type,filename){const url=URL.createObjectURL(new Blob([content],{type})),a=document.createElement('a');a.href=url;a.download=filename;a.click();setTimeout(()=>URL.revokeObjectURL(url),2000);}
  $('#download-logo').addEventListener('click',()=>{
    const family=fonts.find(f=>f.id===state.font).family.split(',')[0].replaceAll('"','').trim();
    const embedded=[];
    for(const sheet of document.styleSheets){try{for(const rule of sheet.cssRules){if(rule.type===CSSRule.FONT_FACE_RULE&&rule.style.fontFamily.replaceAll('"','').replaceAll("'",'')===family)embedded.push(rule.cssText);}}catch{}}
    const logo=logoSvg(state.layout).replace('<title>',`<defs><style>${esc(embedded.join('\n'))}</style></defs><title>`);
    download(logo,'image/svg+xml',`dona-logo-${state.layout}.svg`);toast('Композиция логотипа сохранена в SVG');
  });
  $('#download-avatar').addEventListener('click',()=>{const p=palette();const body=standaloneSymbol();download(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><rect width="100" height="100" fill="${p[2]}"/><g fill="none" stroke="${p[1]}" color="${p[1]}" stroke-width="1.35" stroke-linecap="round" stroke-linejoin="round">${body}</g></svg>`,'image/svg+xml','dona-avatar.svg');toast('Аватар SVG сохранён');});

  async function exportBook(){
    try{
      let css,js;
      if(window.DONA_ASSETS){({css,js}=window.DONA_ASSETS);}else{
        const responses=await Promise.all([fetch('styles.css'),fetch('app.js'),fetch('fonts.css'),fetch('social.css'),fetch('media.css')]);
        if(responses.some(r=>!r.ok))throw new Error('assets');
        const texts=await Promise.all(responses.map(r=>r.text()));css=texts[2]+'\n'+texts[0]+'\n'+texts[3]+'\n'+texts[4];js=texts[1];
      }
      const clone=document.documentElement.cloneNode(true);
      clone.querySelectorAll('script,style,link[rel=stylesheet]').forEach(el=>el.remove());
      clone.querySelector('#toast').classList.remove('visible');clone.querySelector('#png-result').hidden=true;clone.querySelector('#png-result').removeAttribute('href');clone.querySelector('#png-preview').hidden=true;clone.querySelector('#png-preview').removeAttribute('src');
      const style=document.createElement('style');style.textContent=css;clone.querySelector('head').append(style);
      const snapshot=document.createElement('script');snapshot.textContent=`window.DONA_SNAPSHOT=${JSON.stringify(state).replaceAll('<','\\u003c')};window.DONA_ASSETS=${JSON.stringify({css,js}).replaceAll('<','\\u003c')};`;
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
  function persist(){rememberPlacement();try{localStorage.setItem(storageKey,JSON.stringify(state));}catch{}}
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
  $('#artwork-reset').addEventListener('click',()=>{const format=state.editor.format;state.editor=structuredClone(initial.editor);state.editor.format=format;if(state.symbol==='monogram')state.editor.y=20;renderEditor();persist();});
  const stage=$('#artwork-canvas');let drag=null;
  stage.addEventListener('pointerdown',event=>{if(!event.target.closest('[data-drag-symbol]'))return;event.preventDefault();stage.focus({preventScroll:true});stage.setPointerCapture(event.pointerId);drag={id:event.pointerId,px:event.clientX,py:event.clientY,x:state.editor.x,y:state.editor.y,rect:stage.getBoundingClientRect()};stage.classList.add('is-dragging');});
  stage.addEventListener('pointermove',event=>{if(!drag||drag.id!==event.pointerId)return;state.editor.x=drag.x+(event.clientX-drag.px)/drag.rect.width*100;state.editor.y=drag.y+(event.clientY-drag.py)/drag.rect.height*100;renderEditor();});
  function finishDrag(){if(!drag)return;drag=null;stage.classList.remove('is-dragging');persist();}
  stage.addEventListener('pointerup',finishDrag);stage.addEventListener('pointercancel',finishDrag);stage.addEventListener('lostpointercapture',finishDrag);
  stage.addEventListener('keydown',event=>{const delta={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]}[event.key];if(!delta)return;event.preventDefault();const step=event.shiftKey?5:1;state.editor.x+=delta[0]*step;state.editor.y+=delta[1]*step;renderEditor();persist();});

  function updatePlacementStatus(){
    const q=state.brandPlacement;
    $('#clear-brand-placement').disabled=!q;
    const same=q&&['x','y','angle','size'].every(k=>Math.abs(q[k]-state.editor[k])<.01)&&q.format===state.editor.format;
    $('#brand-placement-status').textContent=q?`В брендбуке: X ${Math.round(q.x)}%, Y ${Math.round(q.y)}%, угол ${q.angle}°, размер ${q.size}%. ${same?'Настройки применены. В отдельных знаках повторяется только поворот; знак с названием масштабируется как единая композиция.':'Изменения редактора ещё не применены.'}`:'В брендбуке используются исходные композиции. Знак с названием сохраняет пропорции композиции. Отдельные знаки остаются по центру своих областей и повторяют только поворот.';
  }
  $('#apply-brand-placement').addEventListener('click',()=>{artworkSpec();state.brandPlacement=Object.fromEntries(['x','y','angle','size'].map(k=>[k,state.editor[k]]));state.brandPlacement.format=state.editor.format;render();toast('Расположение символа применено ко всему брендбуку');});
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
      // Capture both vector artwork and text before awaiting; later UI edits do not alter the export.
      const a=artworkSpec(),name=state.name,format=state.editor.format;
      const vector=`<svg xmlns="http://www.w3.org/2000/svg" width="${a.w}" height="${a.h}" viewBox="0 0 ${a.w} ${a.h}" style="color:${a.ink}">${artworkMark(a)}</svg>`;
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

  render();
  document.fonts.ready.then(renderLogos);
})();
