"""Fetch a pinned CC0 instrument subset. Requires Python 3 and curl, no packages."""
from __future__ import annotations

import concurrent.futures
import hashlib
import json
import re
import subprocess
import urllib.parse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BANK = ROOT / 'Assets/Resources/Orchestra'
REVISION = '440300901dfe9275fd84e0b7763af1f8443ae62e'
BASE = f'https://raw.githubusercontent.com/sgossner/VSCO-2-CE/{REVISION}/'
# Filenames use C3 = MIDI 60; confirmed against the recorded A3 = 440 Hz.
INSTRUMENTS = [
    ('ViolinShort', 'Strings/Violin Section/Spic', 60, 84),
    ('ViolinLong', 'Strings/Violin Section/susVib', 60, 81),
    ('CelloShort', 'Strings/Cello Section/spic', 36, 60),
    ('CelloLong', 'Strings/Cello Section/susvib', 36, 60),
    ('Bass', 'Strings/Solo Contrabass/SusNV', 28, 48),
    ('Horn', 'Brass/F Horn/sus', 48, 72),
    ('Trumpet', 'Brass/Trumpet/stac', 60, 81),
    ('Flute', 'Woodwinds/Flute/susNV', 65, 86),
    ('Bassoon', 'Woodwinds/Bassoon/stac', 43, 65),
]


def fetch(url: str, destination: Path) -> None:
    if destination.exists() and destination.stat().st_size > 100:
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(['curl', '-L', '--fail', '--silent', '--show-error', '--retry', '3',
                    url, '-o', str(destination)], check=True)


def main() -> None:
    cache = ROOT / 'Builds/sample-cache'
    tree_path = cache / 'tree.json'
    fetch(f'https://api.github.com/repos/sgossner/VSCO-2-CE/git/trees/{REVISION}?recursive=1', tree_path)
    tree = json.loads(tree_path.read_text())['tree']
    paths = [item['path'] for item in tree if item['path'].endswith('.wav')]
    entries = []
    pitch_class = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
    for instrument, folder, low, high in INSTRUMENTS:
        for path in paths:
            if path.rsplit('/', 1)[0] != folder:
                continue
            match = re.search(r'_([A-G])(#?)(\d)_v(\d)', path)
            if not match:
                continue
            root = pitch_class[match[1]] + bool(match[2]) + (int(match[3]) + 2) * 12
            velocity = int(match[4])
            rr = re.search(r'_rr(\d)', path, re.IGNORECASE)
            if low <= root <= high and (not rr or int(rr[1]) <= 2):
                entries.append(dict(instrument=instrument, root=root, layer=velocity,
                                    alternate=int(rr[1]) if rr else 1, source=path))
    for instrument, prefix, root in [
        ('Timpani', 'Percussion/Timpani/Timpani3_Hit_', 49),
        ('BassDrum', 'Percussion/BDrumNewhit_', 36),
        ('Snare', 'Percussion/Snare2-HitSN_', 60),
        ('Cymbal', 'VSCO 1 Percussion/varMetal/Cymbals/clash/crash_hit_', 60),
    ]:
        candidates = [p for p in paths if p.startswith(prefix)]
        if instrument == 'Snare':
            candidates = [p for p in candidates if 'hit' in p.lower()]
        if not candidates:
            raise RuntimeError(f'No samples for {instrument}')
        for index, path in enumerate(candidates[:6]):
            entries.append(dict(instrument=instrument, root=root, layer=1 + index // 2,
                                alternate=1 + index % 2, tuningCents=48 if instrument == 'Timpani' else 0, source=path))
    def download(entry: dict) -> dict:
        filename = entry['instrument'] + '_' + Path(entry['source']).name
        destination = BANK / 'Samples' / filename
        fetch(BASE + urllib.parse.quote(entry['source']), destination)
        entry['resource'] = 'Orchestra/Samples/' + destination.stem
        entry['sha256'] = hashlib.sha256(destination.read_bytes()).hexdigest()
        return entry
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        entries = list(pool.map(download, entries))
    selected = {entry['resource'].split('/')[-1] + '.wav' for entry in entries}
    for unused in (BANK / 'Samples').glob('*.wav'):
        if unused.name not in selected:
            unused.rename(cache / unused.name)
    fetch(BASE + 'LICENSE', BANK / 'LICENSE.txt')
    (BANK / 'manifest.json').write_text(json.dumps(dict(revision=REVISION, samples=entries), indent=2) + '\n')
    print(f'Prepared {len(entries)} orchestral samples; {sum(p.stat().st_size for p in (BANK / "Samples").glob("*.wav")) / 1048576:.1f} MiB')


if __name__ == '__main__':
    main()
