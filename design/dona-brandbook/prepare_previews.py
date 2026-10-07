"""Regenerate committed web previews: python -m pip install Pillow, then run this file.
Original PNGs remain untouched and are used for every export.
"""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parent
preview_dir = root / 'previews'
preview_dir.mkdir(exist_ok=True)
photos = {'packaging', 'garment', 'apparel', 'store-day', 'store-night', 'campaign'}
for source in sorted((root / 'assets').glob('*.png')):
    with Image.open(source) as original:
        preview = original.convert('RGBA')
        photo = source.stem in photos
        preview.thumbnail((960, 960) if photo or source.stem == 'dona-kiss-signature' else (640, 960))
        preview.save(preview_dir / (source.stem + '.webp'), format='WEBP',
                     quality=80 if photo else 92, method=6)
print('Preview bytes:', sum(p.stat().st_size for p in preview_dir.glob('*.webp')))
