"""Restart and restore only the disposable instance created by Start-LocalAcceptance.ps1."""
import argparse
import hashlib
import http.cookiejar
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import time
import urllib.request


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--instance', type=Path, required=True)
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--pg-bin', type=Path, required=True)
    parser.add_argument('--pg-data', type=Path, required=True)
    parser.add_argument('--ffmpeg-bin', type=Path, required=True)
    parser.add_argument('--isolated-postgres', action='store_true', required=True,
                        help='Confirm that pg-data belongs exclusively to the disposable acceptance environment.')
    args = parser.parse_args()
    repository = Path(__file__).resolve().parent.parent
    instance = json.loads(args.instance.read_text(encoding='utf-8-sig'))
    root = Path(instance['root']).resolve()
    if root.parent != repository / '.audit' or not re.fullmatch(r'acceptance-[a-f0-9]{32}', root.name):
        raise ValueError('Only a disposable acceptance instance is supported.')
    database = instance['database']
    port = int(instance.get('postgresPort', 55433))
    if not 1 <= port <= 65535:
        raise ValueError('Invalid PostgreSQL port.')
    if database != 'notebook_acceptance_' + root.name.removeprefix('acceptance-'):
        raise ValueError('Unexpected database identity.')
    cookies = http.cookiejar.CookieJar()
    client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookies))
    base = 'http://127.0.0.1:5080'

    def request(path, data=None, token=None):
        headers = {'Content-Type': 'application/json'}
        if token:
            headers['X-CSRF-TOKEN'] = token
        req = urllib.request.Request(base + path, data=None if data is None else json.dumps(data).encode(), headers=headers)
        with client.open(req, timeout=20) as response:
            return response.read()

    session = json.loads(request('/api/auth/session'))
    assert session['required'] and session['configured']
    request('/api/auth/login', {'password': 'controlled-acceptance-password'}, session['csrfToken'])

    def snapshot():
        paths = ['/api/week', '/api/productions', '/api/studio/topics', '/api/studio/jobs']
        result = {path: json.loads(request(path)) for path in paths}
        assert len(result['/api/week']) == 7
        artifacts = 0
        for production in result['/api/productions']:
            for path in [f"/api/productions/{production['id']}", f"/api/studio/productions/{production['id']}"]:
                result[path] = json.loads(request(path))
            detail = result[f"/api/studio/productions/{production['id']}"]
            for artifact in detail['artifacts']:
                if artifact['status'] == 'Valid':
                    digest = hashlib.sha256(request(f"/api/studio/artifacts/{artifact['id']}/file")).hexdigest()
                    assert digest == artifact['sha256']
                    artifacts += 1
        assert artifacts > 0, 'Run the browser media test before persistence acceptance.'
        return result

    before = snapshot()
    def command(arguments):
        subprocess.run([str(arg) for arg in arguments], check=True, creationflags=subprocess.CREATE_NO_WINDOW)

    def stop():
        command(['powershell.exe', '-NoProfile', '-Command', f"Stop-Process -Id {int(instance['processId'])} -ErrorAction Stop"])

    def start(db, media, keys):
        salt = secrets.token_bytes(16)
        import base64
        password_hash = '100000:' + base64.b64encode(salt).decode() + ':' + base64.b64encode(hashlib.pbkdf2_hmac('sha256', b'controlled-acceptance-password', salt, 100000)).decode()
        env = dict(os.environ, ConnectionStrings__Editorial=f'Host=127.0.0.1;Port={port};Database={db};Username=postgres',
                   Hosting__Urls=base, ASPNETCORE_ENVIRONMENT='Production', Security__RequireAuthentication='true',
                   Security__PasswordHash=password_hash, Security__KeyDirectory=str(keys), Storage__Root=str(media),
                   Media__ffmpegPath=str(args.ffmpeg_bin / 'ffmpeg.exe'), Media__ffprobePath=str(args.ffmpeg_bin / 'ffprobe.exe'), Worker__Enabled='true')
        with (root / 'restart.log').open('ab') as output:
            process = subprocess.Popen([args.dotnet, 'NotebookYt.Api.dll'], cwd=root / 'runtime', env=env,
                                       stdout=output, stderr=output, creationflags=subprocess.CREATE_NO_WINDOW)
        instance['processId'] = process.pid
        args.instance.write_text(json.dumps(instance, indent=2), encoding='utf-8')
        for _ in range(30):
            try:
                request('/health')
                return
            except (OSError, urllib.error.URLError):
                time.sleep(1)
        raise RuntimeError('Restart failed.')

    stop()
    backup = root / ('backup-' + secrets.token_hex(8))
    common = ['-PgBin', str(args.pg_bin), '-Port', str(port), '-Username', 'postgres']
    command(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', repository / 'scripts/Backup-NotebookYt.ps1',
             '-Database', database, '-MediaDirectory', root / 'media', '-KeyDirectory', root / 'keys', '-Destination', backup, '-ApplicationStopped', *common])
    command([args.pg_bin / 'pg_ctl.exe', '-D', args.pg_data, '-l', root / 'postgres-restart.log', '-o', f'-h 127.0.0.1 -p {port}', 'restart'])
    start(database, root / 'media', root / 'keys')
    assert snapshot() == before, 'State changed after API and PostgreSQL restart.'
    stop()
    restore_id = secrets.token_hex(16)
    restore_database = 'notebook_restore_' + restore_id
    media, keys = root / ('restored-media-' + restore_id), root / ('restored-keys-' + restore_id)
    command(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', repository / 'scripts/Restore-NotebookYt.ps1',
             '-BackupDirectory', backup, '-NewDatabase', restore_database, '-MediaDirectory', media, '-KeyDirectory', keys, *common])
    start(restore_database, media, keys)
    assert snapshot() == before, 'State changed after isolated restore.'
    result = {'apiRestart': 'passed', 'postgresRestart': 'passed', 'isolatedRestore': 'passed',
              'authenticatedCookieSurvived': True, 'allValidArtifactHashes': 'passed',
              'sourceDatabase': database, 'restoredDatabase': restore_database, 'genuineNotebookVideo': False}
    (root / 'persistence-result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))
    # Leave the original disposable database selected; preserve the restored copy for inspection.
    stop()
    start(database, root / 'media', root / 'keys')


if __name__ == '__main__':
    main()
