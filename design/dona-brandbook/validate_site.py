"""Check the Pages artifact using only the Python standard library."""
from html.parser import HTMLParser
from pathlib import Path
import re
from urllib.parse import urlsplit, unquote

root = Path(__file__).resolve().parent / 'dist'


class Page(HTMLParser):
    def __init__(self):
        super().__init__()
        self.refs = []
        self.ids = []

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if 'id' in values:
            self.ids.append(values['id'])
        for attr in ('src', 'href'):
            if values.get(attr):
                self.refs.append(values[attr])


page = Page()
page.feed((root / 'index.html').read_text(encoding='utf-8'))
assert len(page.ids) == len(set(page.ids)), 'Duplicate HTML IDs'
assert {'identity', 'applications', 'editor-page', 'files', 'instagram', 'donamour'} <= set(page.ids)
for ref in page.refs:
    url = urlsplit(ref)
    if url.scheme or url.netloc:
        continue
    if url.path:
        target = (root / unquote(url.path)).resolve()
        assert target.is_relative_to(root.resolve()), f'Nonportable path: {ref}'
        assert target.is_file(), f'Missing page resource: {ref}'
    elif url.fragment:
        assert unquote(url.fragment) in page.ids, f'Broken anchor: {ref}'

loader = (root / 'assets.js').read_text(encoding='utf-8')
for expression, suffix, folder in [
    (r"const mediaIds=\[(.*?)\]", '.png', 'assets'),
    (r"const licenseIds=\[(.*?)\]", '-OFL.txt', 'licenses'),
]:
    match = re.search(expression, loader, re.S)
    assert match, f'Loader list not found: {folder}'
    for name in re.findall(r"'([^']+)'", match[1]):
        if folder == 'assets':
            assert (root / 'previews' / (name + '.webp')).is_file(), f'Missing preview: {name}'
        path = root / folder / (name + suffix)
        assert path.is_file() and path.stat().st_size > 0, f'Missing asset: {path}'

assert (root / 'dona-brandbook.html').read_bytes() == (root / 'index.html').read_bytes()
assert (root / '.nojekyll').is_file()
assert not any((root / name).exists() for name in ['source-artwork', '.git', 'README.md'])
print('Pages artifact valid: five screens, local links, images, fonts and legacy entry URL.')
