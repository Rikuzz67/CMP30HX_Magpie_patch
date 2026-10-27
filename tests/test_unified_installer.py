"""Windows integration tests using caller-supplied, unmodified release binaries.

Developer-only Python test. End users need only Windows PowerShell 5.1.
All target mutations occur in an isolated test directory; no application is launched.
"""
import argparse
from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import uuid


@contextmanager
def test_directory(root):
    directory = root/('run-'+uuid.uuid4().hex)
    directory.mkdir()
    try:
        yield directory
    finally:
        # Validate the absolute target before recursive Windows cleanup.
        if directory.resolve().parent != root.resolve():
            raise ValueError('Test cleanup target escaped its workspace')
        shutil.rmtree(directory)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(installer, app, action, core, model, ok=True):
    result = subprocess.run([
        'powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', str(installer), '-AppDir', str(app), '-Action', action,
        '-CoreFile', str(core), '-SnippetFile', str(model)
    ], capture_output=True)
    if (result.returncode == 0) != ok:
        raise AssertionError(result.stdout.decode(errors='replace') + result.stderr.decode(errors='replace'))


def snapshot(app):
    return {p.relative_to(app).as_posix(): sha(p) for p in app.rglob('*') if p.is_file()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--exe0610', required=True, type=Path)
    parser.add_argument('--exe0611', required=True, type=Path)
    parser.add_argument('--core', required=True, type=Path)
    parser.add_argument('--model', required=True, type=Path)
    parser.add_argument('--report', required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    versions = json.loads((root/'versions.json').read_text())
    results = []
    work_dir = root/'.test-work'
    work_dir.mkdir(exist_ok=True)
    with test_directory(work_dir) as temporary:
        base = Path(temporary)
        for version, source in [('0.6.10', args.exe0610), ('0.6.11', args.exe0611)]:
            spec = next(v for v in versions if v['version'] == version)
            app = base/version
            app.mkdir()
            shutil.copyfile(source, app/'Magpie.exe')
            (app/'config').mkdir()
            (app/'config'/'preserve.txt').write_bytes(b'unchanged-style-0')
            original = snapshot(app)
            run(root/'Install.ps1', app, 'Install', args.core, args.model)
            assert sha(app/'Magpie.exe') == spec.get('optimizedHash', spec['patchedHash'])
            assert (app/'config'/'preserve.txt').read_bytes() == b'unchanged-style-0'
            run(root/'Install.ps1', app, 'Check', args.core, args.model)
            installed = snapshot(app)
            run(root/'Install.ps1', app, 'Install', args.core, args.model, ok=False)
            assert snapshot(app) == installed
            exe = app/'Magpie.exe'
            good = exe.read_bytes()
            changed = bytearray(good)
            changed[-1] ^= 1
            exe.write_bytes(changed)
            tampered = snapshot(app)
            run(root/'Install.ps1', app, 'Check', args.core, args.model, ok=False)
            run(root/'Install.ps1', app, 'Restore', args.core, args.model, ok=False)
            assert snapshot(app) == tampered
            exe.write_bytes(good)
            run(root/'Install.ps1', app, 'Restore', args.core, args.model)
            assert {k: v for k, v in snapshot(app).items() if not k.startswith('.30hx-compat-restored-')} == original
            run(root/'Install.ps1', app, 'Install', args.core, args.model)
            assert sha(exe) == spec.get('optimizedHash', spec['patchedHash'])
            run(root/'Install.ps1', app, 'Restore', args.core, args.model)
            results.append({'version': version, 'install_check_restore': True,
                            'final_executable_hash': spec.get('optimizedHash', spec['patchedHash']),
                            'duplicate_install_no_writes': True, 'tampered_check_restore_no_writes': True,
                            'reinstall_after_restore': True, 'config_preserved': True})
        # Exercise migration of a genuine v1 install and its existing backup state.
        legacy = base/'legacy'
        legacy.mkdir()
        shutil.copyfile(args.exe0611, legacy/'Magpie.exe')
        legacy_pkg = base/'legacy-package'
        legacy_pkg.mkdir()
        shutil.copytree(root/'src', legacy_pkg/'src')
        shutil.copyfile(root/'30HXCoreLoader.dll', legacy_pkg/'30HXCoreLoader.dll')
        old_versions = [{k: v for k, v in entry.items() if k not in ('optimizedHash', 'performanceModelHash')} for entry in versions]
        (legacy_pkg/'versions.json').write_text(json.dumps(old_versions))
        # Compatibility-only installation is byte-identical to the previous release.
        shutil.copyfile(root/'Install.ps1', legacy_pkg/'Install.ps1')
        run(legacy_pkg/'Install.ps1', legacy, 'Install', args.core, args.model)
        assert sha(legacy/'Magpie.exe') == versions[1]['patchedHash']
        run(root/'Install.ps1', legacy, 'Check', args.core, args.model)
        run(root/'Install.ps1', legacy, 'Restore', args.core, args.model)
        run(root/'Install.ps1', legacy, 'Install', args.core, args.model)
        assert sha(legacy/'Magpie.exe') == versions[1]['optimizedHash']
        results.append({'v1_restore_then_v2_install': True})
        unknown = base/'unknown'
        unknown.mkdir()
        (unknown/'Magpie.exe').write_bytes(b'unknown binary')
        before = snapshot(unknown)
        run(root/'Install.ps1', unknown, 'Install', args.core, args.model, ok=False)
        assert snapshot(unknown) == before
        assert len(list(unknown.iterdir())) == 1
        # A modified shader must fail before backup creation or any app writes.
        shader_app = base/'shader-reject'
        shader_app.mkdir()
        shutil.copyfile(args.exe0611, shader_app/'Magpie.exe')
        broken_pkg = base/'broken-package'
        shutil.copytree(legacy_pkg, broken_pkg)
        shutil.copyfile(root/'versions.json', broken_pkg/'versions.json')
        (broken_pkg/'src'/'optimized-downsample.hlsl').write_bytes(b'changed shader')
        before = snapshot(shader_app)
        run(broken_pkg/'Install.ps1', shader_app, 'Install', args.core, args.model, ok=False)
        assert snapshot(shader_app) == before
        assert len(list(shader_app.iterdir())) == 1
        missing_backup = base/'missing-backup'
        missing_backup.mkdir()
        shutil.copyfile(legacy/'Magpie.exe', missing_backup/'Magpie.exe')
        before = snapshot(missing_backup)
        run(root/'Install.ps1', missing_backup, 'Check', args.core, args.model, ok=False)
        assert snapshot(missing_backup) == before
        layered = base/'separate-v2'
        layered.mkdir()
        shutil.copyfile(args.exe0611, layered/'Magpie.exe')
        (layered/'.30hx-performance-backup-v1').mkdir()
        before = snapshot(layered)
        run(root/'Install.ps1', layered, 'Install', args.core, args.model, ok=False)
        assert snapshot(layered) == before
    report = {'tests': results, 'unknown_executable_no_writes': True,
              'modified_shader_no_writes': True, 'missing_backup_check_rejected': True,
              'separate_v2_layout_rejected_without_writes': True, 'shell': 'Windows PowerShell 5.1',
              'application_launched': False, 'full_pipeline_retested': False}
    args.report.write_bytes((json.dumps(report, indent=2)+'\n').encode('utf-8'))
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
