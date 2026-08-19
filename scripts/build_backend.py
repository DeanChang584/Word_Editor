"""PyInstaller build script for WordFormatter Backend.

Usage:
    cd WordFormatter && python scripts/build_backend.py

Output: dist/WordFormatterBackend/WordFormatterBackend.exe
"""
import os
import sys
import shutil
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
DIST_DIR = PROJECT_ROOT / "dist"
BUILD_DIR = PROJECT_ROOT / "build"

# ── Clean previous build ──────────────────────────────────────────────
for d in [DIST_DIR / "WordFormatterBackend", BUILD_DIR]:
    if d.exists():
        shutil.rmtree(d)

# ── Build command ─────────────────────────────────────────────────────
# We use a custom entry point that starts uvicorn programmatically,
# avoiding the complexity of bundling uvicorn CLI as an entry.
# The entry point is backend/__main__.py which does:
#   import uvicorn
#   uvicorn.run("backend.server:app", host="127.0.0.1", port=8765)

# Create entry point
ENTRY_POINT = PROJECT_ROOT / "backend" / "__main__.py"
if not ENTRY_POINT.exists():
    with open(ENTRY_POINT, "w", encoding="utf-8") as f:
        f.write('''"""Entry point for PyInstaller-packaged backend."""
import uvicorn
from backend.server import HOST, PORT

uvicorn.run(
    "backend.server:app",
    host=HOST,
    port=PORT,
    log_level="info",
)
''')

# Build with PyInstaller
import PyInstaller.__main__

PyInstaller.__main__.run([
    "--name=WordFormatterBackend",
    "--onefile",
    "--console",
    f"--distpath={DIST_DIR / 'WordFormatterBackend'}",
    f"--workpath={BUILD_DIR}",
    "--specpath=scripts",
    # Hidden imports for FastAPI/uvicorn
    "--hidden-import=uvicorn.logging",
    "--hidden-import=uvicorn.loops",
    "--hidden-import=uvicorn.loops.auto",
    "--hidden-import=uvicorn.protocols",
    "--hidden-import=uvicorn.protocols.http.auto",
    "--hidden-import=uvicorn.protocols.http.h11_impl",
    "--hidden-import=uvicorn.protocols.websockets",
    "--hidden-import=uvicorn.protocols.websockets.auto",
    "--hidden-import=uvicorn.protocols.websockets.wsproto_impl",
    "--hidden-import=uvicorn.middleware",
    "--hidden-import=uvicorn.middleware.proxy_headers",
    "--hidden-import=uvicorn.middleware.asgi2",
    "--hidden-import=uvicorn.middleware.wsgi",
    "--hidden-import=uvicorn.lifespan",
    "--hidden-import=uvicorn.lifespan.on",
    "--hidden-import=uvicorn.lifespan.off",
    "--hidden-import=uvicorn.middleware.message_logger",
    "--hidden-import=fastapi",
    "--hidden-import=pydantic",
    "--hidden-import=pydantic.deprecated",
    "--hidden-import=pydantic.types",
    "--hidden-import=docx",
    "--hidden-import=docx.oxml",
    "--hidden-import=docx.opc",
    "--hidden-import=lxml",
    "--hidden-import=lxml.etree",
    "--hidden-import=multipart",
    "--hidden-import=httpx",
    # COM support for .doc conversion (Word/WPS automation)
    "--hidden-import=pythoncom",
    "--hidden-import=pywintypes",
    "--hidden-import=win32com",
    "--hidden-import=win32com.client",
    "--hidden-import=win32api",
    # Add data paths (runtime data 已迁移到 %LOCALAPPDATA%\WordFormatter，
    # 不再需要打包项目根的 config/)
    f"--add-data={PROJECT_ROOT / 'backend'}{os.pathsep}backend",
    f"--add-data={PROJECT_ROOT / 'shared'}{os.pathsep}shared",
    f"--add-data={PROJECT_ROOT / 'logs'}{os.pathsep}logs",
    # Entry point
    str(ENTRY_POINT),
])

print(f"\nBuild complete: {DIST_DIR / 'WordFormatterBackend' / 'WordFormatterBackend.exe'}")
