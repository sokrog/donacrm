"""Vendor Google Fonts with their OFL licenses; run only when updating fonts."""
from pathlib import Path
from urllib.request import Request, urlopen
import base64, re

root = Path(__file__).resolve().parent
url = 'https://fonts.googleapis.com/css2?family=Cormorant+Garamond:ital,wght@0,400;1,400&family=Prata&family=Manrope:wght@400&family=Marck+Script&display=swap'
request = Request(url, headers={'User-Agent': 'Mozilla/5.0'})
css = urlopen(request, timeout=30).read().decode()
for link in set(re.findall(r'url\((https[^)]+)\)', css)):
    data = urlopen(link, timeout=30).read()
    mime = 'font/woff2' if data[:4] == b'wOF2' else 'font/ttf'
    css = css.replace(link, 'data:' + mime + ';base64,' + base64.b64encode(data).decode())
(root / 'fonts.css').write_text(css, encoding='utf-8')
(root / 'licenses').mkdir(exist_ok=True)
for family in ['cormorantgaramond','prata','manrope','marckscript']:
    data = urlopen(f'https://raw.githubusercontent.com/google/fonts/main/ofl/{family}/OFL.txt',timeout=30).read()
    (root / 'licenses' / f'{family}-OFL.txt').write_bytes(data)
css += '\n' + '\n'.join('/* ' + f.name + '\n' + f.read_text(encoding='utf-8') + ' */' for f in sorted((root / 'licenses').glob('*.txt')))
(root / 'fonts.css').write_text(css, encoding='utf-8')
print('Embedded 4 font families and downloaded OFL licenses.')
