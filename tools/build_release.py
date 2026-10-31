"""Build a patch-only release from an explicit file allowlist. Maintainer tool."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

FILES = [
    '.gitattributes', '.gitignore', '30HXCoreLoader.dll',
    'Check-30HX.cmd', 'Install-30HX.cmd', 'Restore-30HX.cmd', 'Install.ps1',
    'README.md',
    'VALIDATION.json', 'VALIDATION-v2.json', 'versions.json',
    'src/PatchEngine.cs', 'src/PerformancePatch.cs', 'src/pinned_loader.cpp',
    'src/optimized-downsample.hlsl', 'src/30hx-performance.patch', 'src/LICENSE-Magpie.txt',
    'tests/test_unified_installer.py', 'tools/build_release.py',
]
SHADER_HASH = 'e4ebb611c65b63e4890f3665437e4f023dbf4189f6f8dc0b38c5025464df8f0a'
PACKAGE = 'Magpie-30HX-Compat-v2-community-0.6.10-0.6.11'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    records, payload = [], {}
    for name in FILES:
        path = root/name
        if path.is_symlink():
            raise ValueError('Refusing to package a symbolic link: '+name)
        data = path.read_bytes()
        if name.endswith('.dll'):
            if digest(data) != 'a414b35489082e3795984d3fd8d15f70c65892f7da91c69b40c254100df38b04':
                raise ValueError('Loader fingerprint mismatch')
        elif name.endswith('.hlsl'):
            if digest(data) != SHADER_HASH:
                raise ValueError('Exact shader bytes changed')
        elif name.endswith('.cmd'):
            if data.replace(b'\r\n', b'').find(b'\n') >= 0:
                raise ValueError('CMD file must use CRLF: '+name)
        elif b'\r' in data:
            raise ValueError('Text file must use LF as specified by .gitattributes: '+name)
        payload[name] = data
        records.append({'path': name, 'size': len(data), 'sha256': digest(data)})
    checksum = (json.dumps(records, indent=2, ensure_ascii=False)+'\n').encode('utf-8')
    (root/'CHECKSUMS.json').write_bytes(checksum)
    payload['CHECKSUMS.json'] = checksum
    args.output_dir.mkdir(parents=True, exist_ok=True)
    output = args.output_dir/(PACKAGE+'.zip')
    # Exclusive creation avoids overwriting an earlier release attachment.
    with zipfile.ZipFile(output, 'x', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(payload.items()):
            archive.writestr(PACKAGE+'/'+name, data)
    with zipfile.ZipFile(output) as archive:
        assert archive.testzip() is None
        for name, data in payload.items():
            assert archive.read(PACKAGE+'/'+name) == data
    checksum_file = output.with_suffix('.zip.sha256')
    with checksum_file.open('x', encoding='ascii', newline='\n') as target:
        target.write(digest(output.read_bytes())+'  '+output.name+'\n')
    print(json.dumps({'archive': str(output.resolve()), 'bytes': output.stat().st_size,
                      'sha256': digest(output.read_bytes()), 'files': len(payload)}, indent=2))


if __name__ == '__main__':
    main()
