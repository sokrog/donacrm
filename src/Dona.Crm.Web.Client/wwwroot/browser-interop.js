window.donaGoogle = {
    authorize(clientId) {
        return new Promise((resolve, reject) => {
            if (!window.google?.accounts?.oauth2) {
                reject(new Error('Google Identity Services ещё не загрузился. Повторите через несколько секунд.'));
                return;
            }
            const client = google.accounts.oauth2.initTokenClient({
                client_id: clientId,
                scope: 'openid email profile https://www.googleapis.com/auth/spreadsheets https://www.googleapis.com/auth/drive.file',
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
    pickImage(maxBytes) {
        return new Promise((resolve, reject) => {
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = 'image/*';
            input.onchange = () => {
                const file = input.files?.[0];
                if (!file) { resolve(null); return; }
                if (file.size > maxBytes) { reject(new Error('Для локального браузерного хранения выберите изображение до 600 КБ.')); return; }
                const reader = new FileReader();
                reader.onerror = () => reject(new Error('Не удалось прочитать изображение.'));
                reader.onload = () => resolve({ name: file.name, contentType: file.type, size: file.size, dataUrl: reader.result });
                reader.readAsDataURL(file);
            };
            input.click();
        });
    }
};
