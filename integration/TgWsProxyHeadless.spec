# -*- mode: python ; coding: utf-8 -*-

import os
from PyInstaller.utils.hooks import collect_data_files

root = os.path.join(os.path.dirname(SPEC), os.pardir)

a = Analysis(
    [os.path.join(root, 'zapret_tg_ws_proxy.py')],
    pathex=[root],
    binaries=[],
    datas=collect_data_files('certifi'),
    hiddenimports=[
        'httpx',
        'h2',
        'cryptography.hazmat.primitives.ciphers',
        'cryptography.hazmat.primitives.ciphers.algorithms',
        'cryptography.hazmat.primitives.ciphers.modes',
        'cryptography.hazmat.backends.openssl',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=['pystray', 'customtkinter', 'PIL', 'tkinter'],
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name='TgWsProxy',
    debug=False,
    strip=False,
    upx=False,
    console=False,
    disable_windowed_traceback=False,
    version=os.path.join(os.path.dirname(SPEC), 'version_info.txt'),
)
