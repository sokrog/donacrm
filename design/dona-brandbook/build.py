"""Build the offline HTML, or a public static site with --pages."""
from pathlib import Path
import argparse
import json
import shutil
import base64
import hashlib

root = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--pages', action='store_true', help='Prepare dist/ for GitHub Pages')
args = parser.parse_args()

if args.pages:
    # Explicit public-file list: never upload the repository or design drafts.
    files = [Path(name) for name in (
        'index.html', 'app.js', 'assets.js', 'fonts.css', 'styles.css', 'social.css', 'media.css', 'client.css',
    )]
    files += sorted(path.relative_to(root) for path in (root / 'licenses').glob('*.txt'))
    files += sorted(path.relative_to(root) for path in (root / 'assets').glob('*.png'))
    files += sorted(path.relative_to(root) for path in (root / 'previews').glob('*.webp'))
    target = root / 'dist'
    allowed = set(files) | {Path('.nojekyll'), Path('dona-brandbook.html')}
    if target.exists():
        unexpected = [p.relative_to(target) for p in target.rglob('*')
                      if p.is_file() and p.relative_to(target) not in allowed]
        if unexpected:
            parser.error(f'Unexpected files in dist; move them out before publishing: {unexpected}')
    for relative in files:
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / relative, destination)
    # Bind markup, scripts, styles and fetched media to the same content version.
    digest=hashlib.sha256()
    for relative in files:
        digest.update((root / relative).read_bytes())
    version=digest.hexdigest()[:16]
    page=(target / 'index.html').read_text(encoding='utf-8')
    for relative in files:
        if relative.suffix in ('.js','.css'):
            page=page.replace(f'"{relative.as_posix()}"', f'"{relative.as_posix()}?v={version}"')
    versions={relative.as_posix():hashlib.sha256((root / relative).read_bytes()).hexdigest()[:16] for relative in files if relative.parts[0] in ('assets','previews','licenses')}
    page=page.replace('<head>', f'<head><script>window.DONA_BUILD="{version}";window.DONA_RESOURCE_VERSIONS={json.dumps(versions)};</script>')
    (target / 'index.html').write_text(page,encoding='utf-8')
    # Keep existing shared URLs and hash links working with the modular build.
    shutil.copyfile(target / 'index.html', target / 'dona-brandbook.html')
    (target / '.nojekyll').write_text('', encoding='utf-8')
    print(f'GitHub Pages site: {target}')
    raise SystemExit(0)

html = (root / 'index.html').read_text(encoding='utf-8')
css = '\n'.join((root / filename).read_text(encoding='utf-8') for filename in ['fonts.css', 'styles.css', 'social.css', 'media.css', 'client.css'])
js = (root / 'app.js').read_text(encoding='utf-8')
media = {path.stem: 'data:image/png;base64,' + base64.b64encode(path.read_bytes()).decode('ascii')
         for path in sorted((root / 'assets').glob('*.png'))}
licenses = {path.stem: path.read_text(encoding='utf-8') for path in (root / 'licenses').glob('*.txt')}
html = html.replace('<script src="assets.js"></script>', '<script>window.DONA_MEDIA=' + json.dumps(media) + ';window.DONA_LICENSES=' + json.dumps(licenses).replace('<', r'\u003c') + ';</script>')
assets = json.dumps({'css': css, 'js': js}, ensure_ascii=False).replace('<', r'\u003c')
html = html.replace('<link rel="stylesheet" href="client.css">', '')
html = html.replace('<link rel="stylesheet" href="media.css">', '')
html = html.replace('<link rel="stylesheet" href="fonts.css">', '')
html = html.replace('<link rel="stylesheet" href="social.css">', '')
html = html.replace('<link rel="stylesheet" href="styles.css">', f'<style>{css}</style>')
html = html.replace('<script src="app.js"></script>', f'<script>window.DONA_ASSETS={assets};</script>\n<script>{js}</script>')
target = root / 'dona-brandbook.html'
target.write_text(html, encoding='utf-8')
print(target)
