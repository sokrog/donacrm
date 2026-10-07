/* A self-contained Instagram kit. Artwork uses a pinned, approved identity. */
window.createDonaInstagram = function(api) {
  'use strict';
  const root=document.getElementById('instagram'), $=s=>root.querySelector(s);
  const ink='#66021F',paper='#FBE6ED',cream='#FFF9F4',muted='#775563';
  const defaults={handle:'dona.avec.amour',name:'DONA | Женская одежда',bio:'Женственность в вашем ритме.\nОбразы, которые хочется носить.\nПодбор размера и заказ — в Direct.',link:'',avatar:'avatar-kiss',edits:{}};
  let draft=structuredClone(defaults),photos={},ready=false,activeId=null,returnFocus=null,downloadUrl=null,busy=false;
  try {const saved=window.DONA_IG_SNAPSHOT||JSON.parse(localStorage.getItem('dona-instagram-v1'));if(saved){for(const k of ['handle','name','bio','link','avatar'])if(typeof saved[k]==='string')draft[k]=saved[k];if(saved.edits&&typeof saved.edits==='object')draft.edits=saved.edits;if(window.DONA_IG_SNAPSHOT?.photos)photos=window.DONA_IG_SNAPSHOT.photos;}}catch{}
  const esc=value=>String(value).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;');
  const items=[
    {id:'logo',group:'identity',label:'Логотип · avec amour',kind:'logo',w:1900,h:940},
    {id:'avatar-kiss',group:'identity',label:'Аватар · поцелуй',kind:'avatar',w:1080,h:1080,note:'Рекомендуем для маленького круга'},
    {id:'avatar-logo',group:'identity',label:'Аватар · DONA',kind:'avatar',w:1080,h:1080,note:'Название без мелкой подписи'},
    ...[['new','Новинки'],['looks','Образы'],['delivery','Доставка'],['love','Отзывы'],['fitting','Размеры'],['care','Уход'],['gift','Подарки'],['address','О нас']].map(([icon,label])=>({id:'highlight-'+icon,group:'highlights',label,kind:'highlight',icon,w:1080,h:1080})),
    ...[
      ['welcome','Знакомство','Одежда с вашим\nхарактером.','DONA · AVEC AMOUR','brand',null,'Знакомьтесь, DONA. Мы собираем образы, в которых легко оставаться собой. Здесь — новые сочетания, детали и вдохновение на каждый день. Расскажите, какой образ вы ищете.'],
      ['collection','Коллекция','Ваш новый\nлюбимый образ.','КОЛЛЕКЦИЯ DONA','photo','campaign','Познакомьтесь с нашей коллекцией. Напишите в Direct, какой образ вам понравился: уточним наличие, состав, размер и стоимость. Перед публикацией добавьте реальные данные товара.'],
      ['order','Как заказать','Ваш выбор.\nНаша забота.','НАПИШИТЕ НАМ В DIRECT','steps',null,'Как заказать: 1. Отправьте фото понравившейся вещи в Direct. 2. Уточните размер, стоимость и наличие. 3. Согласуйте оплату и доставку до подтверждения заказа.'],
      ['detail','Детали','Красота\nв деталях.','ПРИСМОТРИТЕСЬ','photo','garment','Фактура, посадка, отделка — детали создают настроение. Добавьте сюда состав ткани и особенности именно этой вещи. Фото в макете замените снимком реального товара.'],
      ['mood','Настроение','Ближе\nк себе.','ВАШ СТИЛЬ. ВАША ПОДПИСЬ.','quote',null,'Любимый образ — тот, в котором вы узнаёте себя. Что сегодня ближе: спокойная классика или один выразительный акцент?'],
      ['looks','Сочетания','Один образ.\nВаши истории.','ВДОХНОВЕНИЕ DONA','photo','campaign','Одна вещь может звучать по-разному. Покажите два реальных сочетания из коллекции и предложите подписчицам выбрать любимое.'],
      ['fit','Подбор размера','Хорошая посадка\nначинается с вас.','ПОМОЖЕМ С ВЫБОРОМ','type',null,'Не уверены в размере? Напишите нам модель, ваш рост и привычный размер. Уточним необходимые мерки и сравним с замерами вещи.'],
      ['care','Забота о вещах','Любимые вещи\nнадолго.','СОХРАНИТЕ НА ПАМЯТЬ','type',null,'Перед уходом загляните на вшивную этикетку: рекомендации зависят от состава и отделки. Сохраните этот пост и напишите нам, если нужна информация по вашей модели.'],
      ['signature','Подпись бренда','avec amour','DONA','seal',null,'С любовью к деталям. С вниманием к вам. DONA, avec amour.']
    ].map(([id,label,title,subtitle,design,photo,caption],i)=>({id:'post-'+id,group:'posts',kind:'post',label,title,subtitle,design,photo,caption,w:1080,h:1350,pin:i<3})),
    ...[
      ['new','Новинки','Встречайте\nновые образы.','УЗНАТЬ БОЛЬШЕ В DIRECT','campaign'],
      ['size','Подбор размера','Найдём\nвашу посадку.','НАПИШИТЕ НАМ',''],
      ['question','Диалог','Какое настроение\nвыбираете сегодня?','ДОБАВЬТЕ ОПРОС В INSTAGRAM',''],
      ['thanks','Благодарность','Спасибо,\nчто вы с DONA.','AVEC AMOUR','']
    ].map(([id,label,title,subtitle,photo])=>({id:'story-'+id,group:'stories',kind:'story',label,title,subtitle,photo,w:1080,h:1920})),
    ...[['looks','Образ дня','Образ\nв движении.','campaign'],['details','Детали','Ближе\nк деталям.','garment'],['styling','Стилизация','Носить\nпо-своему.','campaign']].map(([id,label,title,photo])=>({id:'reel-'+id,group:'reels',kind:'reel',label,title,subtitle:'DONA · AVEC AMOUR',photo,w:1080,h:1920}))
  ];
  const find=id=>items.find(x=>x.id===id);
  const sourceItem=item=>({...item,...draft.edits[item.id]});
  const nested=(svg,x,y,w,h)=>svg.replace('<svg ',`<svg x="${x}" y="${y}" width="${w}" height="${h}" overflow="hidden" `);
  const logo=(x,y,w,h,color=ink)=>nested(api.logo().replace(/style="color:[^"]+"/,`style="color:${color}"`),x,y,w,h);
  const mark=(x,y,size,color=ink)=>`<g style="color:${color}" transform="translate(${x} ${y}) scale(${size/100})">${api.symbol('kiss')}</g>`;
  const label=(value,x,y,size=26,color=ink,anchor='middle',family='Manrope')=>`<text x="${x}" y="${y}" font-family="${family},sans-serif" font-size="${size}" fill="${color}" text-anchor="${anchor}">${esc(value)}</text>`;
  function lines(value,x,y,size=84,color=ink){return String(value).split('\n').slice(0,3).map((line,i)=>label(line,x,y+i*size*1.02,Math.min(size,850/(Math.max(1,line.length)*.53)),color,'middle','Cormorant Garamond')).join('');}
  function artwork(original){
    const item=sourceItem(original),{w,h}=item;
    if(item.kind==='logo')return api.logo();
    const dark=item.id==='post-mood'||item.id==='post-order'||item.id==='story-thanks',fg=dark?paper:ink,bg=dark?ink:paper;
    let body=`<rect width="${w}" height="${h}" fill="${bg}"/>`;
    const photo=photos[item.id]||window.DONA_MEDIA[item.photo];
    if(item.kind==='avatar'){
      if(item.id==='avatar-kiss')body+=mark(280,280,520);
      else body+=nested(api.logo().replace(/<text x="795" y="707"[\s\S]*?<\/text>/,''),150,325,780,385);
    }else if(item.kind==='highlight'){
      body+=`<circle cx="540" cy="540" r="290" fill="${cream}"/>`+nested(api.icon(item.icon,ink),340,340,400,400);
    }else if(item.kind==='post'){
      if(item.design==='photo'){
        body=`<rect width="1080" height="1350" fill="${cream}"/><image href="${esc(photo)}" x="0" y="0" width="1080" height="930" preserveAspectRatio="xMidYMid slice"/>`;
        body+=lines(item.title,540,1055,72)+label(item.subtitle,540,1260,24);
      }else if(item.design==='brand'){
        body+=logo(145,235,790,390)+lines(item.title,540,835,92)+label(item.subtitle,540,1160,24);
      }else if(item.design==='seal'){
        body+=mark(395,200,290)+logo(150,660,780,386)+label('ЖЕНСТВЕННОСТЬ В ВАШЕМ РИТМЕ',540,1150,24);
      }else{
        body+=label(item.subtitle,540,200,24,fg)+lines(item.title,540,565,96,fg);
        if(item.design==='steps')body+=label('01 ВЫБРАТЬ  ·  02 УТОЧНИТЬ  ·  03 ЗАКАЗАТЬ',540,865,23,fg);
        else if(item.id==='post-fit')body+=nested(api.icon('fitting',fg),430,820,220,220);
        else if(item.id==='post-care')body+=nested(api.icon('care',fg),430,820,220,220);
        else body+=mark(425,840,230,fg);
        body+=logo(365,1100,350,173,fg);
      }
    }else{
      if(photo)body=`<image href="${esc(photo)}" width="1080" height="1920" preserveAspectRatio="xMidYMid slice"/><rect width="1080" height="1920" fill="${ink}" opacity=".15"/><rect x="90" y="430" width="900" height="780" rx="4" fill="${paper}" opacity=".96"/>`;
      body+=logo(310,460,460,228,fg)+lines(item.title,540,825,86,fg);
      if(item.id==='story-question')body+=label('КЛАССИКА  /  АКЦЕНТ',540,1080,27,fg);
      else body+=label(item.subtitle,540,1090,24,fg);
    }
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${w} ${h}" role="img" aria-label="${esc(item.label)}"><title>${esc(item.label)}</title>${body}</svg>`;
  }
  const sources=[
    ['WeLoveSMM · оформление профиля','https://welovesmm.com.ua/blog/instagram-design/'],
    ['Quasa · модели заработка','https://quasa.io/ru/media/kak-zarabotat-dengi-v-instagram-16-sposobov-nachat-zarabatyvat'],
    ['Buffer · размеры и кадрирование, 2026','https://buffer.com/resources/instagram-image-size/'],
    ['Meta Blueprint · бизнес-аккаунт','https://www.facebookblueprint.com/student/collection/507784/path/461641/activity/241903'],
    ['Meta · коммерческая музыка','https://www.facebook.com/help/instagram/402084904469945']
  ];
  const steps=[
    ['Включите бизнес-профиль','Откройте Instagram на телефоне → свой профиль → ☰ → «Тип аккаунта и инструменты» → «Переключиться на профессиональный аккаунт». Выберите подходящую категорию магазина одежды и тип «Бизнес». Если уже выбран профессиональный аккаунт, ищите «Бизнес-инструменты» / «Изменить тип аккаунта». Названия пунктов зависят от версии приложения.'],
    ['Заполните шапку','Профиль → «Редактировать профиль»: укажите имя, вставьте текст «О себе», затем «Ссылки» → внешняя ссылка. Ссылку на каталог или Telegram добавляйте отдельно от био. В «Способах связи» укажите только рабочие контакты и реальный адрес. Никнеймы здесь — предложения, их доступность нужно проверить.'],
    ['Установите аватар','В разделе «Аватар и логотип» скачайте avatar-kiss.png. На телефоне сохраните файл из «Загрузок» / «Файлов» в галерею. Instagram → «Редактировать профиль» → «Изменить фото» → выбрать из галереи. Центрируйте знак внутри круга, не приближайте до краёв. Полный логотип с avec amour лучше оставить для крупных карточек.'],
    ['Соберите «Актуальное»','Сначала опубликуйте полезные истории по каждой теме. В профиле нажмите «Новое» / «+» в блоке актуального либо добавьте историю в актуальное через её меню. Для существующей подборки: удержать → «Редактировать актуальное» → «Редактировать обложку» → изображение из галереи. Возьмите highlight-*.png, оставьте иконку по центру. Название напишите в Instagram, его нет на самой обложке. Если галерея недоступна в вашей версии, добавьте обложку как историю и выберите её из подборки.'],
    ['Подготовьте первые публикации','В карточках с фото нажмите «⋯» → «Изменить текст / фото» и загрузите снимки настоящей коллекции. Добавьте в подпись реальные состав, размеры, цену и наличие. Скачайте post-*.png или JPG. Instagram → «+» → «Публикация» → выбрать файл → проверить кадрирование → вставить подпись. Для карусели используйте одинаковый формат 4:5. Каждая карточка самостоятельна, разрезать коллаж не нужно.'],
    ['Закрепите три главных поста','Опубликуйте «Знакомство», «Коллекция» и «Как заказать», затем откройте каждый → «⋯» → «Закрепить в профиле» (иногда в разделе «Управление»). Девять карточек — план стартового наполнения, не обещание автоматического расположения сетки. Проверяйте порядок после закрепления.'],
    ['Добавьте Stories и Reels','История: «+» → «История» → story-*.png. Опрос и кликабельную ссылку добавьте штатными стикерами — нарисованный текст не является кнопкой. Reels: снимите своё видео → «+» → Reel → перед публикацией «Редактировать обложку» → «Добавить из галереи» → reel-*.png. Эти файлы — обложки, а не готовые видео. Проверьте и полный экран, и обрезку в сетке.'],
    ['Проверьте перед запуском','Посмотрите профиль с другого аккаунта: читается ли аватар, понятен ли заказ, работают ли ссылка и контакты. В настройках найдите «Качество медиа» и включите загрузку в высоком качестве, если пункт доступен. Музыку для бизнеса подбирайте с разрешением на коммерческое использование; у Meta есть Sound Collection. Ответы, наличие и условия доставки поддерживайте актуальными.']
  ];
  function guide(){return 'DONA · Instagram / инструкция\nПодготовлено 07.10.2026\n\n'+steps.map(([title,body],i)=>`${i+1}. ${title}\n${body}`).join('\n\n')+'\n\nФОРМАТЫ\nPNG — готовые изображения для Instagram. JPG — компактная копия без прозрачности. SVG — исходник композиции для дизайнера, напрямую в Instagram не загружается; шрифты и растровый поцелуй встроены, это не полностью векторный логотип.\nАватар и актуальное: 1080×1080. Пост: 1080×1350. История и обложка Reel: 1080×1920. Наша рабочая зона для ключевого текста историй: x=90…990, y=300…1200. Это запас для интерфейса, не универсальная гарантия кадрирования.\nФото с пометкой ДЕМО созданы для брендбука. Перед публикацией товара замените их реальными. Отзывы, цены, адрес и сроки не выдумывайте.\n\nИСТОЧНИКИ\n'+sources.map(([t,u])=>t+'\n'+u).join('\n\n');}
  function caption(item){return sourceItem(item).caption||`${item.label} · DONA, avec amour.`;}
  function profileText(){return `Никнейм (проверьте доступность): ${draft.handle}\nИмя: ${draft.name}\n\nО себе:\n${draft.bio}\n\nСсылка: ${draft.link||'Добавьте свою ссылку в редакторе профиля Instagram'}\n\nКатегория: магазин одежды\nКонтакты: укажите рабочие телефон, email и адрес в Instagram.`;}
  function saveDraft(){try{localStorage.setItem('dona-instagram-v1',JSON.stringify(draft));}catch{}}
  function card(item){return `<article class="ig-card"><button class="ig-art ${item.kind==='avatar'||item.kind==='highlight'?'ig-round':''}" data-ig-item="${item.id}" aria-haspopup="menu" aria-label="${esc(item.label)} — открыть меню скачивания">${api.unique(artwork(item))}</button><div class="ig-card-title"><div><strong>${esc(item.label)}</strong><small>${item.w} × ${item.h} · ${item.kind==='logo'?'прозрачный фон':'PNG / JPG'}</small></div><button class="ig-more" data-ig-item="${item.id}" aria-haspopup="menu" aria-label="Меню: ${esc(item.label)}">⋯</button></div>${item.photo&&!photos[item.id]?'<span class="ig-demo">ДЕМО-ФОТО · замените перед публикацией</span>':''}${item.note?`<small class="ig-note">${esc(item.note)}</small>`:''}${item.pin?'<span class="ig-pin">Для закрепления</span>':''}</article>`;}
  function renderGallery(){for(const group of ['identity','highlights','posts','stories','reels'])$('#ig-'+group+'-grid').innerHTML=items.filter(x=>x.group===group).map(card).join('');renderProfile();}
  function renderProfile(){
    $('#ig-preview-handle').textContent=draft.handle||'Ваш никнейм';$('#ig-preview-name').textContent=draft.name;$('#ig-preview-bio').textContent=draft.bio;$('#ig-preview-link').textContent=draft.link||'Ссылка на ваш каталог';
    $('#ig-preview-avatar').dataset.igItem=draft.avatar;
    $('#ig-preview-avatar').innerHTML=api.unique(artwork(find(draft.avatar)||find('avatar-kiss')));
    $('#ig-profile-highlights').innerHTML=items.filter(i=>i.kind==='highlight').slice(0,6).map(i=>`<button data-ig-item="${i.id}" aria-haspopup="menu">${api.unique(artwork(i))}<span>${i.label}</span></button>`).join('');
    $('#ig-profile-feed').innerHTML=items.filter(i=>i.kind==='post').map(i=>`<button data-ig-item="${i.id}" aria-haspopup="menu" aria-label="${esc(i.label)} — скачать">${api.unique(artwork(i))}</button>`).join('');
    $('#ig-bio-count').textContent=[...draft.bio].length+' / 150';
  }
  function render(){
    if(ready)return;
    ready=true;
    root.innerHTML=`<header class="page-heading ig-heading"><div><span>INSTAGRAM / DONA AVEC AMOUR</span><h1 id="instagram-title" tabindex="-1">Ваш бутик. В новой ленте.</h1><p>Всё для старта бизнес-профиля: от первой строки до последней обложки. Утверждённый логотип, рубиновый цвет и мягкий розовый.</p></div><button id="ig-download-kit" class="primary-button">Скачать комплект · ZIP ↓</button></header>
      <nav class="ig-nav" aria-label="Разделы Instagram"><a href="#ig-profile">01 Профиль</a><a href="#ig-materials">02 Материалы</a><a href="#ig-guide">03 Как оформить</a></nav>
      <p id="ig-status" class="ig-status" role="status" aria-live="polite">27 макетов · PNG, JPG и SVG · тексты и инструкция</p><a id="ig-last-download" hidden></a>
      <section id="ig-profile" class="ig-section"><div class="ig-section-head"><div><span class="ig-eyebrow">01 / ПЕРВОЕ ВПЕЧАТЛЕНИЕ</span><h2>Профиль, в котором всё на месте.</h2></div><p>Это пример оформления. Никнейм, контакты и условия магазина задайте перед запуском.</p></div><div class="ig-profile-layout"><form id="ig-profile-form" class="ig-settings"><label>Имя пользователя<input name="handle" maxlength="30" pattern="[A-Za-z0-9_.]+" value="${esc(draft.handle)}" autocomplete="off"></label><small>Предложение: dona.avec.amour. Доступность не проверена.</small><label>Имя профиля<input name="name" maxlength="64" value="${esc(draft.name)}"></label><label>О себе <span id="ig-bio-count"></span><textarea name="bio" maxlength="150" rows="5">${esc(draft.bio)}</textarea></label><label>Ссылка на каталог / Telegram<input name="link" type="url" placeholder="https://…" value="${esc(draft.link)}"></label><label>Аватар<select name="avatar"><option value="avatar-kiss">Поцелуй · читается в малом размере</option><option value="avatar-logo">DONA · без мелкой подписи</option></select></label><div class="ig-actions"><button type="button" id="ig-copy-bio" class="quiet-button">Скопировать био</button><button type="button" id="ig-save-profile" class="quiet-button">Скачать тексты TXT</button></div><p class="ig-note">Изменения сохраняются в этом браузере. Этот комплект закреплён за DONA и не меняется при переключении других концепций брендбука.</p></form><div class="ig-profile-mock"><div class="ig-profile-top"><strong id="ig-preview-handle"></strong><span aria-hidden="true">☰</span></div><div class="ig-profile-summary"><button id="ig-preview-avatar" data-ig-item="avatar-kiss" aria-label="Скачать аватар" aria-haspopup="menu"></button><div><b id="ig-preview-name"></b><small>Магазин женской одежды</small><p id="ig-preview-bio"></p><span id="ig-preview-link"></span></div></div><div class="ig-profile-buttons" aria-hidden="true"><span>Подписаться</span><span>Написать</span></div><div id="ig-profile-highlights"></div><div id="ig-profile-feed"></div><p class="ig-note">Предпросмотр сетки 3:4. Файлы постов сохраняются полностью в 4:5. Нажмите на любой элемент, чтобы скачать.</p></div></div></section>
      <section id="ig-materials" class="ig-section"><div class="ig-section-head"><div><span class="ig-eyebrow">02 / ВАША БИБЛИОТЕКА</span><h2>Создано, чтобы сочетаться.</h2></div><p>Нажмите на макет или «⋯». Правая кнопка мыши открывает то же меню. PNG — сразу в Instagram; SVG — для дальнейшего редактирования.</p></div>
      ${[['identity','Аватар и логотип','Крупный знак для круга, полная подпись — для карточек.'],['highlights','Обложки «Актуального»','Восемь разделов. Названия добавляются в Instagram под обложками.'],['posts','Первые девять публикаций','Знакомство, коллекция, заказ. Затем детали, образы и полезные советы.'],['stories','Истории для живого общения','Место для текста и стикеров, без мелких деталей по краям.'],['reels','Обложки Reels','Готовая обложка для вашего видео: заголовок остаётся в центральной зоне.']].map(([id,title,note])=>`<div class="ig-collection"><h3>${title}</h3><p>${note}</p><div id="ig-${id}-grid" class="ig-grid ig-grid-${id}"></div></div>`).join('')}</section>
      <section id="ig-guide" class="ig-section"><div class="ig-section-head"><div><span class="ig-eyebrow">03 / БЕЗ ОПЫТА — ТОЖЕ ПОЛУЧИТСЯ</span><h2>От скачивания до первого поста.</h2></div><button id="ig-guide-download" class="quiet-button">Скачать инструкцию TXT ↓</button></div><div class="ig-format-grid"><article><b>PNG</b><p>Аватар, обложки, графика. Основной выбор для загрузки. Сохраняйте файл, а не скриншот макета.</p></article><article><b>JPG</b><p>Компактная версия с фоном. Подходит для фотографий и публикаций. Прозрачность заменяется розовым фоном.</p></article><article><b>SVG</b><p>Исходник для дизайнера. В Instagram напрямую не загружается. Поцелуй и фото внутри — растровые, шрифты встроены.</p></article><article><b>ZIP + TXT</b><p>Комплект PNG и исходников SVG, тексты, план публикаций и инструкция. Распакуйте ZIP на телефоне или компьютере.</p></article></div><div class="ig-steps">${steps.map(([t,b],i)=>`<details ${i===0?'open':''}><summary><span>${String(i+1).padStart(2,'0')}</span>${t}</summary><p>${b}</p></details>`).join('')}</div><div class="ig-guide-note"><h3>Размеры и кадрирование</h3><p>Аватары и обложки актуального — 1080 × 1080. Посты — 1080 × 1350 (4:5). Истории и обложки Reels — 1080 × 1920 (9:16). Instagram также поддерживает посты 3:4; для этого комплекта выбран 4:5. В сетке профиль может показывать обрезанное превью, поэтому важное остаётся в центре.</p><p>В вертикальных макетах ключевой текст находится в зоне x: 90–990, y: 300–1200. Это наш запас под интерфейс; расположение кнопок и стикеров зависит от экрана. Перед публикацией проверьте обрезку в приложении.</p><p>Демонстрационные фото не подтверждают наличие товара. Загружайте реальные снимки коллекции, честные цены и отзывы. Для доставки, возвратов и оплаты укажите свои действительные условия.</p></div><details class="ig-sources"><summary>Источники и актуальность · 07.10.2026</summary><p>Ваши статьи использованы для структуры профиля и визуального единства. Размеры дополнительно сверены с руководством Buffer 2026. Часть справки Meta требует входа, поэтому названия пунктов настроек могут отличаться. Советы о подписке на хештеги и обязательной мультиссылке из старых статей здесь не используются.</p><ul>${sources.map(([t,u])=>`<li><a href="${u}" target="_blank" rel="noopener noreferrer">${t} ↗</a></li>`).join('')}</ul></details></section>
      <div id="ig-context-menu" class="ig-context-menu" role="menu" aria-label="Действия с материалом" hidden><strong id="ig-menu-name"></strong><button role="menuitem" data-ig-action="png">Скачать PNG · для Instagram</button><button role="menuitem" data-ig-action="jpg">Скачать JPG · компактный</button><button role="menuitem" data-ig-action="svg">Скачать SVG · исходник</button><button role="menuitem" data-ig-action="caption">Скачать подпись TXT</button><button role="menuitem" data-ig-action="edit">Изменить текст / фото</button></div>
      <dialog id="ig-edit-dialog" aria-labelledby="ig-edit-title"><form method="dialog"><div class="ig-dialog-heading"><h2 id="ig-edit-title">Настроить карточку</h2><button value="cancel" aria-label="Закрыть">×</button></div><label>Заголовок · до трёх строк<textarea id="ig-edit-headline" maxlength="75" rows="3"></textarea></label><label>Короткая подпись<input id="ig-edit-subtitle" maxlength="48"></label><label>Текст публикации<textarea id="ig-edit-caption" rows="6" maxlength="2200"></textarea></label><div id="ig-photo-options"><label>Ваше фото · PNG, JPG или WebP<input id="ig-edit-photo" type="file" accept="image/png,image/jpeg,image/webp"></label><p id="ig-photo-status" role="status">Фото хранится до закрытия страницы. Скачайте результат после замены.</p></div><p class="ig-note">Длинные заголовки разбивайте переносом строки. Проверяйте превью перед скачиванием.</p><div class="ig-actions"><button type="button" id="ig-edit-save" class="primary-button">Применить</button><button value="cancel" class="quiet-button">Отмена</button></div></form></dialog>`;
    $('#ig-profile-form select').value=draft.avatar;
    $('#ig-profile-form').addEventListener('submit',e=>e.preventDefault());
    $('#ig-profile-form').addEventListener('input',e=>{if(Object.hasOwn(defaults,e.target.name)){draft[e.target.name]=e.target.value;saveDraft();renderProfile();$('#ig-preview-avatar').dataset.igItem=draft.avatar;}});
    root.addEventListener('click',onClick);root.addEventListener('contextmenu',e=>{const b=e.target.closest('[data-ig-item]');if(b){e.preventDefault();openMenu(b,e.clientX,e.clientY);}});
    document.addEventListener('pointerdown',e=>{if(!e.target.closest('#ig-context-menu,[data-ig-item]'))closeMenu(false);});
    document.addEventListener('keydown',e=>{if(e.key==='Escape')closeMenu(true);});
    window.addEventListener('scroll',()=>closeMenu(false),true);window.addEventListener('resize',()=>closeMenu(false));
    window.addEventListener('hashchange',()=>closeMenu(false));
    $('#ig-context-menu').addEventListener('keydown',e=>{const buttons=[...$('#ig-context-menu').querySelectorAll('button:not([hidden])')];if(['ArrowDown','ArrowUp','Home','End'].includes(e.key)){e.preventDefault();const i=buttons.indexOf(document.activeElement);buttons[e.key==='Home'?0:e.key==='End'?buttons.length-1:(i+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length].focus();}if(e.key==='Tab')closeMenu(false);});
    $('#ig-edit-save').addEventListener('click',applyEdit);
    $('#ig-edit-photo').addEventListener('change',loadPhoto);
    $('#ig-download-kit').addEventListener('click',downloadKit);
    $('#ig-guide-download').addEventListener('click',()=>saveFile(new Blob([guide()],{type:'text/plain;charset=utf-8'}),'DONA-instagram-guide.txt'));
    $('#ig-save-profile').addEventListener('click',()=>saveFile(new Blob([profileText()],{type:'text/plain;charset=utf-8'}),'DONA-profile.txt'));
    $('#ig-copy-bio').addEventListener('click',async()=>{try{await navigator.clipboard.writeText(draft.bio);status('Био скопировано. Вставьте в поле «О себе».');}catch{const textarea=$('[name="bio"]');textarea.focus();textarea.select();status('Текст выделен. Нажмите Ctrl+C или выберите «Копировать».');}});
    renderGallery();document.fonts.ready.then(renderGallery);
  }
  function status(message){$('#ig-status').textContent=message;}
  function openMenu(button,x,y){
    activeId=button.dataset.igItem;returnFocus=button;const item=find(activeId),menu=$('#ig-context-menu'),rect=button.getBoundingClientRect();
    $('#ig-menu-name').textContent=item.label;menu.querySelector('[data-ig-action="edit"]').hidden=!item.title||item.design==='seal';menu.querySelector('[data-ig-action="caption"]').hidden=!item.caption;
    menu.hidden=false;menu.style.left=Math.max(8,Math.min(x??rect.left,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,Math.min(y??rect.bottom,innerHeight-menu.offsetHeight-8))+'px';menu.querySelector('button').focus();
  }
  function closeMenu(restore){const menu=$('#ig-context-menu');if(!menu||menu.hidden)return;menu.hidden=true;if(restore&&returnFocus?.isConnected)returnFocus.focus({preventScroll:true});}
  async function onClick(e){
    const item=e.target.closest('[data-ig-item]');if(item){openMenu(item);return;}
    const action=e.target.closest('[data-ig-action]');if(!action)return;
    const target=find(activeId),format=action.dataset.igAction;closeMenu(true);
    if(format==='edit'){const current=sourceItem(target);$('#ig-edit-title').textContent=target.label;$('#ig-edit-headline').value=current.title||'';$('#ig-edit-subtitle').value=current.subtitle||'';$('#ig-edit-caption').value=current.caption||'';$('#ig-photo-options').hidden=!target.photo;$('#ig-edit-photo').value='';pendingPhoto=null;$('#ig-photo-status').textContent='Фото хранится до закрытия страницы. Скачайте результат после замены.';$('#ig-edit-dialog').showModal();return;}
    if(busy){status('Дождитесь завершения текущего скачивания.');return;}
    busy=true;status('Готовим '+target.label+'…');
    try{
      const extension=format==='caption'?'txt':format,name=`DONA-${target.id}.${extension}`;
      // Open while the menu click still has user activation, before rendering PNG.
      const handle=await chooseFile(name,extension);
      await document.fonts.ready;const src=artwork(target);let blob;
      if(format==='caption')blob=new Blob([caption(target)],{type:'text/plain;charset=utf-8'});else blob=await makeFile(src,target,format);
      if(handle)await writeFile(handle,blob);
      saveFile(blob,name,!handle);status('Готово: '+target.label+'. Файл доступен по ссылке выше.');
    }catch(error){if(error.name==='AbortError')status('Скачивание отменено.');else{console.error(error);status('Не удалось подготовить файл. Проверьте загрузку изображений и повторите.');}}finally{busy=false;}
  }
  function checkedSvg(source){const doc=new DOMParser().parseFromString(source,'image/svg+xml');if(doc.querySelector('parsererror'))throw new Error('Invalid export SVG');return source;}
  async function makeFile(source,item,format){
    if(format==='svg')return new Blob([checkedSvg(await api.portable(source))],{type:'image/svg+xml'});
    checkedSvg(source);const png=await api.png(source,item.w,item.h);if(format==='png')return png;
    const url=URL.createObjectURL(png);try{const image=new Image();image.src=url;await image.decode();const canvas=document.createElement('canvas');canvas.width=item.w;canvas.height=item.h;const ctx=canvas.getContext('2d');ctx.fillStyle=paper;ctx.fillRect(0,0,item.w,item.h);ctx.drawImage(image,0,0);const jpg=await new Promise(resolve=>canvas.toBlob(resolve,'image/jpeg',.95));if(!jpg)throw new Error('JPG export');return jpg;}finally{URL.revokeObjectURL(url);}
  }
  async function chooseFile(name,extension){
    if(typeof window.showSaveFilePicker!=='function')return null;
    const mime={png:'image/png',jpg:'image/jpeg',svg:'image/svg+xml',txt:'text/plain',zip:'application/zip'}[extension];
    try{return await window.showSaveFilePicker({suggestedName:name,types:[{description:extension.toUpperCase(),accept:{[mime]:['.'+extension]}}]});}
    catch(error){if(error.name==='SecurityError'||error.name==='NotAllowedError')return null;throw error;}
  }
  async function writeFile(handle,blob){
    const stream=await handle.createWritable();
    try{await stream.write(blob);await stream.close();}catch(error){try{await stream.abort();}catch{}throw error;}
  }
  function saveFile(blob,name,automatic=true){
    if(downloadUrl)URL.revokeObjectURL(downloadUrl);
    downloadUrl=URL.createObjectURL(new File([blob],name,{type:blob.type}));
    const a=$('#ig-last-download');a.href=downloadUrl;a.download=name;a.hidden=false;a.textContent='Скачать ещё раз: '+name;
    a.onclick=event=>{
      if(!event.isTrusted||typeof window.showSaveFilePicker!=='function')return;
      event.preventDefault();
      chooseFile(name,name.split('.').pop()).then(async handle=>{if(handle)await writeFile(handle,blob);else a.click();}).catch(error=>{if(error.name!=='AbortError'){console.error(error);status('Не удалось сохранить файл. Повторите скачивание.');}});
    };
    if(automatic)a.click();
  }
  let pendingPhoto=null;
  async function loadPhoto(){
    const file=$('#ig-edit-photo').files[0];if(!file)return;const save=$('#ig-edit-save');save.disabled=true;
    try{if(!['image/png','image/jpeg','image/webp'].includes(file.type)||file.size>25*1024*1024)throw new Error('Выберите PNG, JPG или WebP до 25 МБ.');
      const url=URL.createObjectURL(file);try{const img=new Image();img.src=url;await img.decode();const ratio=Math.min(1,2400/Math.max(img.naturalWidth,img.naturalHeight));const c=document.createElement('canvas');c.width=Math.round(img.naturalWidth*ratio);c.height=Math.round(img.naturalHeight*ratio);const ctx=c.getContext('2d');ctx.fillStyle=cream;ctx.fillRect(0,0,c.width,c.height);ctx.drawImage(img,0,0,c.width,c.height);pendingPhoto=c.toDataURL('image/jpeg',.95);$('#ig-photo-status').textContent='Фото готово. Нажмите «Применить» и проверьте кадрирование.';}finally{URL.revokeObjectURL(url);}
    }catch(e){pendingPhoto=null;$('#ig-photo-status').textContent=e.message;}finally{save.disabled=false;}
  }
  function applyEdit(){draft.edits[activeId]={title:$('#ig-edit-headline').value.split('\n').slice(0,3).join('\n'),subtitle:$('#ig-edit-subtitle').value,caption:$('#ig-edit-caption').value};if(pendingPhoto)photos[activeId]=pendingPhoto;saveDraft();$('#ig-edit-dialog').close();renderGallery();status('Карточка обновлена. Можно скачать через её меню.');}
  async function downloadKit(){
    if(busy)return;busy=true;const button=$('#ig-download-kit');button.disabled=true;
    try{await document.fonts.ready;const batch=items.map(item=>({item,source:artwork(item)})),files=[['START-HERE.txt',guide()],['profile.txt',profileText()],['captions.txt',items.filter(i=>i.caption).map((i,n)=>`${n+1}. ${i.label}${i.pin?' · ЗАКРЕПИТЬ':''}\n${caption(i)}${i.photo&&!photos[i.id]?'\nДЕМО-ФОТО: замените реальным перед публикацией.':''}`).join('\n\n')],['selection.json',JSON.stringify({brand:'DONA',ink,paper,profile:draft},null,2)]];
      for(let i=0;i<batch.length;i++){const {item,source}=batch[i];status(`Готовим комплект: ${i+1} / ${batch.length} · ${item.label}`);const prefix=`${item.group}/${item.id}`;files.push([prefix+'.png',await makeFile(source,item,'png')],['sources/'+prefix+'.svg',await makeFile(source,item,'svg')]);}
      for(const [name,text] of Object.entries(window.DONA_LICENSES||{}))files.push(['licenses/'+name+'.txt',text]);
      saveFile(await api.zip(files),'DONA-instagram-kit.zip');status('Комплект готов: 27 PNG, 27 SVG, тексты, инструкция и лицензии.');
    }catch(e){console.error(e);status('Комплект не собрался. Повторите попытку или скачайте нужные карточки по отдельности.');}finally{busy=false;button.disabled=false;}
  }
  return {render,snapshot:()=>({...draft,photos})};
};
