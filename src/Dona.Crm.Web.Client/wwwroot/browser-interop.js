window.donaGoogle = {
    authorize(clientId) {
        return new Promise((resolve, reject) => {
            if (!window.google?.accounts?.oauth2) {
                reject(new Error('Google Identity Services ещё не загрузился. Повторите через несколько секунд.'));
                return;
            }
            const client = google.accounts.oauth2.initTokenClient({
                client_id: clientId,
                scope: 'openid email profile https://www.googleapis.com/auth/drive.appdata https://www.googleapis.com/auth/drive.file',
                callback: response => response?.access_token ? resolve(response.access_token) : reject(new Error(response?.error_description || response?.error || 'Google не вернул токен доступа.')),
                error_callback: error => reject(new Error(error?.message || error?.type || 'Окно авторизации Google было закрыто.'))
            });
            client.requestAccessToken({ prompt: 'consent' });
        });
    },
    revoke(token) {
        return new Promise(resolve => google?.accounts?.oauth2?.revoke(token, resolve) ?? resolve());
    }
};

// Хранилище строк в IndexedDB (в localStorage лимит ~5 МБ, а фотографии хранятся data URL).
// При недоступном IndexedDB используется localStorage с тем же API.
window.donaStore = (() => {
    const quotaMessage = 'Хранилище браузера заполнено. Освободите место или подключите Google Drive для фотографий.';
    let dbPromise = null;

    function isQuota(error) {
        return error?.name === 'QuotaExceededError' || error?.name === 'NS_ERROR_DOM_QUOTA_REACHED' || error?.code === 22;
    }
    function mapError(error) {
        return isQuota(error) ? new Error(quotaMessage) : error;
    }
    function openDb() {
        if (dbPromise) return dbPromise;
        dbPromise = new Promise(resolve => {
            try {
                if (!window.indexedDB) { resolve(null); return; }
                const request = indexedDB.open('dona-crm', 1);
                request.onupgradeneeded = () => request.result.createObjectStore('kv');
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => resolve(null);
                request.onblocked = () => resolve(null);
            } catch { resolve(null); }
        });
        return dbPromise;
    }
    // Результат возвращается только после oncomplete транзакции, т.е. когда данные реально записаны.
    function run(db, mode, action) {
        return new Promise((resolve, reject) => {
            let result;
            const transaction = db.transaction('kv', mode);
            transaction.oncomplete = () => resolve(result);
            transaction.onerror = () => reject(transaction.error ?? new Error('Ошибка хранилища браузера.'));
            transaction.onabort = () => reject(transaction.error ?? new Error('Транзакция хранилища браузера отменена.'));
            const request = action(transaction.objectStore('kv'));
            request.onsuccess = () => { result = request.result; };
        });
    }
    function local(action) {
        try { return action(); } catch (error) { throw mapError(error); }
    }

    return {
        async get(key) {
            const db = await openDb();
            if (!db) return local(() => localStorage.getItem(key));
            try {
                const value = await run(db, 'readonly', store => store.get(key));
                if (typeof value === 'string') return value;
                // Одноразовая миграция из localStorage: удаляем старое значение только после успешной записи в IndexedDB.
                let legacy = null;
                try { legacy = localStorage.getItem(key); } catch { }
                if (legacy === null) return null;
                await run(db, 'readwrite', store => store.put(legacy, key));
                try { localStorage.removeItem(key); } catch { }
                return legacy;
            } catch (error) { throw mapError(error); }
        },
        async set(key, value) {
            const db = await openDb();
            if (!db) { local(() => localStorage.setItem(key, value)); return; }
            try { await run(db, 'readwrite', store => store.put(value, key)); }
            catch (error) { throw mapError(error); }
        },
        async remove(key) {
            const db = await openDb();
            if (!db) { local(() => localStorage.removeItem(key)); return; }
            try { await run(db, 'readwrite', store => store.delete(key)); }
            catch (error) { throw mapError(error); }
            try { localStorage.removeItem(key); } catch { }
        },
        // Ключи с заданным префиксом: IndexedDB и localStorage (значения, ещё не перенесённые, либо IndexedDB недоступна).
        async keys(prefix) {
            const found = new Set();
            const db = await openDb();
            if (db) {
                try {
                    const all = await run(db, 'readonly', store => store.getAllKeys());
                    for (const key of all) if (typeof key === 'string' && key.startsWith(prefix)) found.add(key);
                } catch (error) { throw mapError(error); }
            }
            try {
                for (let index = 0; index < localStorage.length; index++) {
                    const key = localStorage.key(index);
                    if (key && key.startsWith(prefix)) found.add(key);
                }
            } catch { }
            return Array.from(found);
        },
        async requestPersistence() {
            try { await navigator.storage?.persist?.(); } catch { }
        }
    };
})();

// Открывает диалог выбора файла. Отмена диалога завершает промис значением null.
function pickFile(accept, handle) {
    return new Promise((resolve, reject) => {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = accept;
        let settled = false;
        let timer = 0;
        const cleanup = () => {
            clearTimeout(timer);
            window.removeEventListener('focus', onFocus);
            input.onchange = null;
            input.oncancel = null;
            input.remove();
        };
        const finish = action => { if (settled) return; settled = true; cleanup(); action(); };
        // Запасной вариант для браузеров без события cancel: после возврата фокуса даём время сработать change.
        const onFocus = () => {
            clearTimeout(timer);
            timer = setTimeout(() => { if (!input.files?.length) finish(() => resolve(null)); }, 500);
        };
        input.oncancel = () => finish(() => resolve(null));
        input.onchange = () => {
            const file = input.files?.[0];
            if (!file) { finish(() => resolve(null)); return; }
            if (settled) return;
            settled = true;
            cleanup();
            Promise.resolve().then(() => handle(file)).then(resolve, reject);
        };
        window.addEventListener('focus', onFocus, { once: true });
        input.click();
    });
}

window.donaBrowser = {
    pickBackup() {
        return pickFile('.zip,application/zip', async file => new Uint8Array(await file.arrayBuffer()));
    },
    download(name, bytes) {
        const url = URL.createObjectURL(new Blob([bytes], { type: 'application/zip' }));
        const link = document.createElement('a'); link.href = url; link.download = name; link.click();
        setTimeout(() => URL.revokeObjectURL(url), 0);
    },
    pickImage(maxBytes) {
        return pickFile('image/*', file => new Promise((resolve, reject) => {
            const image = new Image();
            const objectUrl = URL.createObjectURL(file);
            image.onerror = () => { URL.revokeObjectURL(objectUrl); reject(new Error('Не удалось прочитать изображение.')); };
            image.onload = async () => {
                try {
                    const scale = Math.min(1, 1600 / Math.max(image.naturalWidth, image.naturalHeight));
                    const canvas = document.createElement('canvas');
                    canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
                    canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
                    canvas.getContext('2d', { alpha: false }).drawImage(image, 0, 0, canvas.width, canvas.height);
                    let quality = .84;
                    let blob;
                    do {
                        blob = await new Promise(done => canvas.toBlob(done, 'image/jpeg', quality));
                        quality -= .09;
                    } while (blob && blob.size > maxBytes && quality >= .48);
                    if (!blob || blob.size > maxBytes) throw new Error('Не удалось ужать фотографию до безопасного размера.');
                    const reader = new FileReader();
                    reader.onerror = () => reject(new Error('Не удалось подготовить изображение.'));
                    reader.onload = () => resolve({ name: file.name.replace(/\.[^.]+$/, '') + '.jpg', contentType: 'image/jpeg', size: blob.size, dataUrl: reader.result });
                    reader.readAsDataURL(blob);
                } catch (error) { reject(error); }
                finally { URL.revokeObjectURL(objectUrl); }
            };
            image.src = objectUrl;
        }));
    }
};

window.donaStore.requestPersistence();
