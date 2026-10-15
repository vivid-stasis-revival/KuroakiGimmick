#!/usr/bin/env python3
"""Restore the signing file inside RUNNER_TEMP without logging secret values."""
import base64
import os
from pathlib import Path

def main():
    names = ['ANDROID_KEYSTORE_BASE64', 'ANDROID_KEYSTORE_PASSWORD', 'ANDROID_KEY_ALIAS', 'ANDROID_KEY_PASSWORD']
    missing = [n for n in names if not os.environ.get(n)]
    if missing:
        raise SystemExit('Missing required repository Secrets: ' + ', '.join(missing))
    payload = base64.b64decode(os.environ['ANDROID_KEYSTORE_BASE64'], validate=True)
    if not payload or len(payload) > 32768:
        raise SystemExit('Unexpected keystore size')
    path = Path(os.environ['RUNNER_TEMP']) / 'kuroaki-signing.jks'
    with path.open('xb') as output:
        os.chmod(path, 0o600)
        output.write(payload)
    with open(os.environ['GITHUB_ENV'], 'a') as output:
        output.write(f'KG_ANDROID_KEYSTORE={path}\n')
    print('Signing file prepared in runner temporary storage.')

if __name__ == '__main__':
    main()
