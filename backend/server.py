"""Word Formatter — FastAPI backend server (entry point).

Initializes the FastAPI application, configures CORS, and registers the
API routers from the ``backend.api`` package. Run with:

    python -m uvicorn backend.server:app --port 8765
"""

import os
import sys
from contextlib import asynccontextmanager
from pathlib import Path

# Ensure the project root is importable so `shared` and `backend` packages
# resolve regardless of the current working directory.
_ROOT = Path(__file__).parent.parent
if str(_ROOT) not in sys.path:
    sys.path.insert(0, str(_ROOT))

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from shared.version import VERSION
from backend.utils.logger import get_logger
from backend.api import health
from backend.api import files
from backend.api import profile
from backend.api import templates
from backend.api import format as format_api
from backend.api import history
from backend.api import preview
from backend.api import settings

# ============================================================
# Configuration
# ============================================================

HOST = "127.0.0.1"
PORT = 8765

logger = get_logger("backend.server", category="backend")

logger.info("Application starting")


# ============================================================
# Lifespan — startup / shutdown hooks
# ============================================================


@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("Listening on %s:%s", HOST, PORT)
    yield
    logger.info("Application shutting down")


# ============================================================
# FastAPI application
# ============================================================

app = FastAPI(
    title="Word Formatter API",
    version=VERSION,
    description="Word Formatter backend service",
    lifespan=lifespan,
)

# CORS — the WinUI 3 client talks to this service over local HTTP.
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# ============================================================
# Router registration (backend.api layer)
# ============================================================

app.include_router(health.router, prefix="/api", tags=["health"])
app.include_router(files.router, prefix="/api", tags=["files"])
app.include_router(profile.router, prefix="/api", tags=["profile"])
app.include_router(templates.router, prefix="/api", tags=["templates"])
app.include_router(format_api.router, prefix="/api", tags=["format"])
app.include_router(history.router, prefix="/api", tags=["history"])
app.include_router(preview.router, prefix="/api", tags=["preview"])
app.include_router(settings.router, prefix="/api", tags=["settings"])


# ============================================================
# Shutdown endpoint — called by the frontend on window close
# ============================================================


@app.post("/api/shutdown")
async def shutdown():
    logger.info("Shutdown requested via /api/shutdown")
    # Schedule exit after returning the response
    import asyncio

    async def _delayed_exit():
        await asyncio.sleep(0.1)
        os._exit(0)

    asyncio.create_task(_delayed_exit())
    return {"success": True, "message": "Shutting down"}

logger.info("FastAPI initialized")


# ============================================================
# Entry point
# ============================================================

if __name__ == "__main__":
    import uvicorn

    uvicorn.run("backend.server:app", host=HOST, port=PORT, reload=False)
