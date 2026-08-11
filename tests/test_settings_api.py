"""Integration tests for GET/PUT /api/settings (theme persistence)."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

PROJECT_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT_ROOT))

from backend.server import app  # noqa: E402
from backend.config import manager  # noqa: E402


@pytest.fixture(autouse=True)
def isolated_settings(tmp_path, monkeypatch):
    """Point the settings manager at a temp file, bypassing the user's real settings."""
    monkeypatch.setattr(manager, "SETTINGS_FILE", tmp_path / "settings.json")
    monkeypatch.setattr(manager, "_cache", None)
    yield


@pytest_asyncio.fixture
async def client():
    transport = ASGITransport(app=app, raise_app_exceptions=False)
    async with AsyncClient(transport=transport, base_url="http://test/api") as ac:
        yield ac


@pytest.mark.asyncio
async def test_get_settings_returns_light_default(client: AsyncClient):
    r = await client.get("/settings")
    assert r.status_code == 200
    body = r.json()
    assert body["success"] is True
    assert body["data"]["settings"]["theme"] == "light"


@pytest.mark.asyncio
async def test_put_settings_persists_theme(client: AsyncClient):
    r = await client.put("/settings", json={"settings": {"theme": "dark"}})
    assert r.status_code == 200
    body = r.json()
    assert body["data"]["settings"]["theme"] == "dark"

    # persisted to disk — a fresh cache read sees it
    manager._cache = None
    assert manager.get_setting("theme") == "dark"


@pytest.mark.asyncio
async def test_put_settings_unknown_key_ignored(client: AsyncClient):
    r = await client.put("/settings", json={"settings": {"bogusKey": 1}})
    assert r.status_code == 200
    body = r.json()
    assert "bogusKey" not in body["data"]["settings"]
