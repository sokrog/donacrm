window.donaGoogle = {
    authorize(clientId) {
        return new Promise((resolve, reject) => {
            if (!window.google?.accounts?.oauth2) {
                reject(new Error('Google Identity Services ещё не загрузился. Повторите через несколько секунд.'));
                return;
            }
            const client = google.accounts.oauth2.initTokenClient({
                client_id: clientId,
                scope: 'openid email profile https://www.googleapis.com/auth/spreadsheets https://www.googleapis.com/auth/drive https://www.googleapis.com/auth/drive.appdata',
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

window.donaBrowser = {
    pickBackup() {
        return new Promise(resolve => {
            const input = document.createElement('input');
            input.type = 'file'; input.accept = '.zip,application/zip';
            input.onchange = async () => {
                const file = input.files?.[0];
                resolve(file ? new Uint8Array(await file.arrayBuffer()) : null);
            };
            input.click();
        });
    },
    download(name, bytes) {
        const url = URL.createObjectURL(new Blob([bytes], { type: 'application/zip' }));
        const link = document.createElement('a'); link.href = url; link.download = name; link.click();
        setTimeout(() => URL.revokeObjectURL(url), 0);
    },
    pickImage(maxBytes) {
        return new Promise((resolve, reject) => {
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = 'image/*';
            input.onchange = () => {
                const file = input.files?.[0];
                if (!file) { resolve(null); return; }
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
            };
            input.click();
        });
    }
};
