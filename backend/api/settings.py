"""Application-wide settings endpoints (theme/language/…).

Implements GET + PUT ``/api/settings`` backed by the config manager
(``backend.config.manager``), which persists to
``%LOCALAPPDATA%\\WordFormatter\\settings.json``.
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter
from pydantic import BaseModel

from backend.config import manager
from backend.utils.logger import get_logger
from backend.utils.response import success_response

logger = get_logger("backend.api.settings", category="backend")

router = APIRouter(prefix="/settings", tags=["settings"])


class UpdateSettingsRequest(BaseModel):
    """Partial settings update — only supplied keys are overwritten."""

    settings: dict[str, Any]


@router.get("")
async def get_settings() -> dict:
    """Return the full settings dict."""
    return success_response({"settings": manager.get_all_settings()})


@router.put("")
async def update_settings(req: UpdateSettingsRequest) -> dict:
    """Merge the supplied keys into settings and persist."""
    merged = manager.update_settings(req.settings)
    logger.info("Settings updated: keys=%s", list(req.settings.keys()))
    return success_response({"settings": merged})
