"""Integration tests for WordFormatter backend API.

Covers all 27 endpoints defined in API.md. Uses pytest + httpx
against the FastAPI ASGI app directly (no server process needed).

Run: cd WordFormatter && pytest tests/test_api.py -v
"""

from __future__ import annotations

import json
import os
from pathlib import Path

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

# ── Test app import ───────────────────────────────────────────────────
#  Make sure we can import from the project root
import sys

PROJECT_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT_ROOT))

from backend.server import app  # noqa: E402
from shared.version import VERSION

# ── Test data paths ───────────────────────────────────────────────────

TEST_A = str(PROJECT_ROOT / "temp" / "test_A.docx")
TEST_B = str(PROJECT_ROOT / "temp" / "test_B.docx")
TEST_TEMP_DIR = str(PROJECT_ROOT / "temp")
EXPORT_DIR = str(PROJECT_ROOT / "temp" / "exports")
os.makedirs(EXPORT_DIR, exist_ok=True)


# ── Fixtures ──────────────────────────────────────────────────────────

@pytest_asyncio.fixture
async def client():
    """Return an httpx AsyncClient wired to the FastAPI ASGI app."""
    transport = ASGITransport(app=app, raise_app_exceptions=False)
    async with AsyncClient(transport=transport, base_url="http://test/api") as ac:
        yield ac


# ═══════════════════════════════════════════════════════════════════════
#  5. Health
# ═══════════════════════════════════════════════════════════════════════


class TestHealth:
    pytestmark = pytest.mark.asyncio

    async def test_health_ok(self, client: AsyncClient):
        r = await client.get("/health")
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        assert body["code"] == 0
        assert body["data"]["status"] == "ok"
        # 与 shared/version 保持一致，避免版本升级时测试漂移
        assert body["data"]["version"] == VERSION


# ═══════════════════════════════════════════════════════════════════════
#  6. File Management (8 endpoints)
# ═══════════════════════════════════════════════════════════════════════


class TestFiles:
    pytestmark = pytest.mark.asyncio
    async def test_get_files_empty(self, client: AsyncClient):
        r = await client.get("/files")
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        assert isinstance(body["data"]["files"], list)

    async def test_add_files_happy(self, client: AsyncClient):
        r = await client.post("/files/add", json={"paths": [TEST_A, TEST_B]})
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        assert body["data"]["count"] == 2

    async def test_add_files_duplicate_skipped(self, client: AsyncClient):
        # Re-add same files — should be skipped (count=0)
        r = await client.post("/files/add", json={"paths": [TEST_A]})
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        assert body["data"]["count"] == 0

    async def test_add_files_nonexistent(self, client: AsyncClient):
        r = await client.post("/files/add", json={"paths": ["Z:/nonexistent.docx"]})
        assert r.status_code == 404
        body = r.json()
        assert body["success"] is False
        assert body["code"] == 1001

    async def test_add_folder(self, client: AsyncClient):
        # Clear first, then add folder
        await client.delete("/files")
        r = await client.post("/files/add-folder",
                              json={"folder": TEST_TEMP_DIR, "includeSubdir": True})
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        # At least the two test .docx files should be found
        assert body["data"]["count"] >= 2

    async def test_add_folder_nonexistent(self, client: AsyncClient):
        r = await client.post("/files/add-folder",
                              json={"folder": "Z:/no_such_folder", "includeSubdir": False})
        assert r.status_code == 404
        assert r.json()["code"] == 1001

    async def test_remove_files(self, client: AsyncClient):
        # Add files first
        await client.delete("/files")
        await client.post("/files/add", json={"paths": [TEST_A, TEST_B]})
        r = await client.post("/files/remove", json={"paths": [TEST_A]})
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        # 契约统一为 count（与 add / add-folder 一致，前端 AddCountDto 解析）
        assert body["data"]["count"] == 1

    async def test_search_files(self, client: AsyncClient):
        await client.delete("/files")
        await client.post("/files/add", json={"paths": [TEST_A, TEST_B]})
        r = await client.post("/files/search", json={"keyword": "test_A"})
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        assert len(body["data"]["files"]) >= 1

    async def test_search_no_match(self, client: AsyncClient):
        r = await client.post("/files/search", json={"keyword": "zzz_no_match_xyz"})
        assert r.status_code == 200
        assert len(r.json()["data"]["files"]) == 0

    async def test_get_recent(self, client: AsyncClient):
        await client.delete("/files")
        await client.post("/files/add", json={"paths": [TEST_A]})
        r = await client.get("/files/recent")
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        assert isinstance(body["data"]["recent"], list)

    async def test_pin_folder(self, client: AsyncClient):
        r = await client.post("/files/pin", json={"folder": TEST_TEMP_DIR})
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        assert TEST_TEMP_DIR in body["data"]["pinned"]

    async def test_pin_nonexistent(self, client: AsyncClient):
        r = await client.post("/files/pin", json={"folder": "Z:/nope"})
        assert r.status_code == 404

    async def test_clear_files(self, client: AsyncClient):
        await client.post("/files/add", json={"paths": [TEST_A]})
        r = await client.delete("/files")
        assert r.status_code == 200
        assert r.json()["success"] is True
        # Verify empty
        r2 = await client.get("/files")
        assert len(r2.json()["data"]["files"]) == 0


# ═══════════════════════════════════════════════════════════════════════
#  7. Profile (3 endpoints)
# ═══════════════════════════════════════════════════════════════════════


class TestProfile:
    pytestmark = pytest.mark.asyncio
    async def test_get_profile(self, client: AsyncClient):
        r = await client.get("/profile")
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        p = body["data"]["profile"]
        assert "page" in p
        assert "headerFooter" in p or "header_footer" in p
        assert "body" in p
        assert "heading" in p

    async def test_update_profile(self, client: AsyncClient):
        r = await client.put("/profile", json={
            "profile": {"body": {"fontCn": "仿宋", "fontSize": 16}}
        })
        assert r.status_code == 200
        assert r.json()["success"] is True

    async def test_update_profile_persisted(self, client: AsyncClient):
        # Verify the update from previous test persisted
        r = await client.get("/profile")
        body = r.json()
        assert body["data"]["profile"]["body"]["fontCn"] == "仿宋"

    async def test_reset_profile(self, client: AsyncClient):
        r = await client.post("/profile/reset")
        assert r.status_code == 200
        assert r.json()["success"] is True
        # Verify reset
        r2 = await client.get("/profile")
        assert r2.json()["data"]["profile"]["body"]["fontCn"] == "宋体"


# ═══════════════════════════════════════════════════════════════════════
#  8. Templates (7 endpoints)
# ═══════════════════════════════════════════════════════════════════════


class TestTemplates:
    pytestmark = pytest.mark.asyncio
    async def test_get_templates(self, client: AsyncClient):
        r = await client.get("/templates")
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        templates = body["data"]["templates"]
        assert len(templates) >= 1  # at least 1 preset ("默认模板")
        # One should be default
        defaults = [t for t in templates if t.get("isDefault")]
        assert len(defaults) >= 1

    async def test_create_template(self, client: AsyncClient):
        r = await client.post("/templates", json={"name": "Integration Test Template"})
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        assert body["data"]["name"] == "Integration Test Template"
        assert body["data"]["id"].startswith("tpl_")
        # Store ID for subsequent tests
        TestTemplates._created_id = body["data"]["id"]

    async def test_update_template(self, client: AsyncClient):
        tid = getattr(TestTemplates, "_created_id", None)
        if not tid:
            pytest.skip("No template created")
        r = await client.put(f"/templates/{tid}", json={"name": "Updated Template"})
        assert r.status_code == 200
        assert r.json()["success"] is True

    async def test_delete_non_default(self, client: AsyncClient):
        tid = getattr(TestTemplates, "_created_id", None)
        if not tid:
            # Create a fresh one
            r = await client.post("/templates", json={"name": "To Delete"})
            tid = r.json()["data"]["id"]
        r = await client.delete(f"/templates/{tid}")
        assert r.status_code == 200
        assert r.json()["success"] is True

    async def test_delete_default_rejected(self, client: AsyncClient):
        # Try deleting a default template — should fail
        resp = await client.get("/templates")
        templates = resp.json()["data"]["templates"]
        default = next((t for t in templates if t.get("isDefault")), None)
        if default is None:
            pytest.skip("No default template found")
        r = await client.delete(f"/templates/{default['id']}")
        assert r.status_code in (400, 404)
        assert r.json()["success"] is False

    async def test_delete_nonexistent(self, client: AsyncClient):
        r = await client.delete("/templates/tpl_nonexistent_999")
        assert r.status_code == 404

    async def test_export_template(self, client: AsyncClient):
        resp = await client.get("/templates")
        templates = resp.json()["data"]["templates"]
        default = next((t for t in templates if t.get("isDefault")), None)
        if default is None:
            pytest.skip("No default template found")
        r = await client.post("/templates/export", json={
            "templateId": default["id"], "targetPath": EXPORT_DIR
        })
        assert r.status_code == 200
        assert r.json()["success"] is True

    async def test_import_template(self, client: AsyncClient):
        # Find the exported file in the export directory
        export_files = list(Path(EXPORT_DIR).glob("*.json"))
        if not export_files:
            pytest.skip("No exported file to import")
        export_file = str(export_files[0])
        r = await client.post("/templates/import", json={"path": export_file})
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        assert "id" in body["data"]

    async def test_import_nonexistent(self, client: AsyncClient):
        r = await client.post("/templates/import", json={"path": "Z:/no_file.json"})
        assert r.status_code == 404

    async def test_set_default(self, client: AsyncClient):
        resp = await client.get("/templates")
        templates = resp.json()["data"]["templates"]
        non_default = next((t for t in templates if not t.get("isDefault")), None)
        if non_default is None:
            pytest.skip("No non-default template found")
        r = await client.post("/templates/default", json={"templateId": non_default["id"]})
        assert r.status_code == 200
        assert r.json()["success"] is True


# ═══════════════════════════════════════════════════════════════════════
#  9. Format Tasks (4 endpoints)
# ═══════════════════════════════════════════════════════════════════════

import asyncio  # noqa: E402


class TestFormat:
    pytestmark = pytest.mark.asyncio
    async def test_start_format(self, client: AsyncClient):
        await client.delete("/files")
        await client.post("/files/add", json={"paths": [TEST_A, TEST_B]})
        r = await client.post("/format/start", json={
            "files": [TEST_A, TEST_B],
            "profile": "default",
            "outputDir": "",
        })
        assert r.status_code == 202
        body = r.json()
        assert body["success"] is True
        assert "taskId" in body["data"]
        TestFormat._task_id = body["data"]["taskId"]

    async def test_get_status(self, client: AsyncClient):
        tid = getattr(TestFormat, "_task_id", None)
        if not tid:
            pytest.skip("No task started")
        # Poll until completed
        for _ in range(30):
            r = await client.get(f"/format/status/{tid}")
            assert r.status_code == 200
            body = r.json()
            assert body["success"] is True
            state = body["data"]["state"]
            if state in ("completed", "failed", "cancelled"):
                break
            await asyncio.sleep(0.3)
        assert state == "completed"

    async def test_get_result(self, client: AsyncClient):
        tid = getattr(TestFormat, "_task_id", None)
        if not tid:
            pytest.skip("No task started")
        r = await client.get(f"/format/result/{tid}")
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        data = body["data"]
        total = data["success"] + data["failed"] + data.get("skipped", 0)
        # Both test files should be ok
        assert data["success"] >= 2

    async def test_start_format_empty_files(self, client: AsyncClient):
        r = await client.post("/format/start", json={
            "files": [], "profile": "default", "outputDir": "",
        })
        assert r.status_code == 400
        assert r.json()["code"] == 1006

    async def test_cancel_task(self, client: AsyncClient):
        # Start a task and immediately cancel it
        await client.delete("/files")
        await client.post("/files/add", json={"paths": [TEST_A, TEST_B]})
        r = await client.post("/format/start", json={
            "files": [TEST_A, TEST_B],
            "profile": "default",
            "outputDir": "",
        })
        assert r.status_code == 202
        tid = r.json()["data"]["taskId"]

        c = await client.post("/format/cancel", json={"taskId": tid})
        assert c.status_code == 200
        assert c.json()["success"] is True

    async def test_status_nonexistent(self, client: AsyncClient):
        r = await client.get("/format/status/no_such_task")
        assert r.status_code == 404
        assert r.json()["code"] == 1007

    async def test_cancel_nonexistent(self, client: AsyncClient):
        r = await client.post("/format/cancel", json={"taskId": "no_such_task"})
        assert r.status_code == 404


# ═══════════════════════════════════════════════════════════════════════
#  10. Preview (1 endpoint)
# ═══════════════════════════════════════════════════════════════════════


class TestPreview:
    pytestmark = pytest.mark.asyncio
    async def test_preview_default(self, client: AsyncClient):
        r = await client.post("/preview", json={"file": "", "profile": "default"})
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        text = body["data"]["preview"]
        assert len(text) > 0
        assert "页面" in text
        assert "正文" in text

    async def test_preview_with_template_id(self, client: AsyncClient):
        resp = await client.get("/templates")
        templates = resp.json()["data"]["templates"]
        first = templates[0] if templates else None
        if first is None:
            pytest.skip("No templates available")
        r = await client.post("/preview", json={"file": "", "profile": first["id"]})
        assert r.status_code == 200
        assert "页面" in r.json()["data"]["preview"]

    @pytest.mark.skip(reason="Full profile dict — optional")
    async def test_preview_with_dict(self, client: AsyncClient):
        r = await client.post("/preview", json={
            "file": "",
            "profile": {"body": {"fontCn": "楷体", "fontSize": 14}}
        })
        assert r.status_code == 200
        assert "楷体" in r.json()["data"]["preview"]


# ═══════════════════════════════════════════════════════════════════════
#  11. History (3 endpoints)
# ═══════════════════════════════════════════════════════════════════════


class TestHistory:
    pytestmark = pytest.mark.asyncio
    async def test_get_history(self, client: AsyncClient):
        r = await client.get("/history")
        assert r.status_code == 200
        body = r.json()
        assert body["success"] is True
        assert isinstance(body["data"]["history"], list)

    async def test_get_history_detail(self, client: AsyncClient):
        # Get first history record (populated by previous format tests)
        r = await client.get("/history")
        records = r.json()["data"]["history"]
        if not records:
            pytest.skip("No history records")
        first_id = records[0]["id"]
        r2 = await client.get(f"/history/{first_id}")
        assert r2.status_code == 200
        body = r2.json()
        assert body["success"] is True
        assert "profile" in body["data"]
        assert "files" in body["data"]

    async def test_get_history_nonexistent(self, client: AsyncClient):
        r = await client.get("/history/no_such_record")
        assert r.status_code == 404

    async def test_clear_history(self, client: AsyncClient):
        r = await client.delete("/history")
        assert r.status_code == 200
        assert r.json()["success"] is True
        # Verify cleared
        r2 = await client.get("/history")
        assert len(r2.json()["data"]["history"]) == 0


# ═══════════════════════════════════════════════════════════════════════
#  12. Settings — not implemented yet
# ═══════════════════════════════════════════════════════════════════════


class TestSettings:
    pytestmark = pytest.mark.asyncio
    @pytest.mark.skip(reason="Backend /api/settings endpoint not yet implemented")
    async def test_get_settings(self, client: AsyncClient):
        r = await client.get("/settings")
        assert r.status_code == 200

    @pytest.mark.skip(reason="Backend /api/settings endpoint not yet implemented")
    async def test_update_settings(self, client: AsyncClient):
        r = await client.put("/settings", json={"settings": {"darkMode": True}})
        assert r.status_code == 200
