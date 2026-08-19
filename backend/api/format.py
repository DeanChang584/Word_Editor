"""Formatting task API endpoints (Step 1.9, refactored Step 3.2).

Implements API.md §9 — 4 endpoints for batch-formatting tasks under
``/api/format``.

All business logic has been extracted to ``backend.services.format_service``.
This module handles only parameter validation and response formatting.
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field

from backend.services.format_service import format_service
from backend.utils.logger import get_logger
from backend.utils.response import ErrorCode, error_response, success_response

logger = get_logger("backend.api.format", category="backend")

router = APIRouter(prefix="/format", tags=["format"])


def _error(code: int, message: str | None = None) -> JSONResponse:
    """Shortcut: build error envelope and return as JSONResponse."""
    body, status = error_response(code, message)
    return JSONResponse(content=body, status_code=status)


# ============================================================
# Request / Response models
# ============================================================


class StartFormatRequest(BaseModel):
    files: list[str]
    profile: str | dict[str, Any] = "default"
    output_dir: str = Field(default="", alias="outputDir")


class CancelRequest(BaseModel):
    task_id: str = Field(alias="taskId")


# ============================================================
# Endpoints
# ============================================================


# 9.1 — Start formatting
@router.post("/start")
async def start_format(req: StartFormatRequest):
    """Launch a batch-formatting task.

    Returns HTTP 202 with the task ID. The frontend should poll
    ``GET /format/status/{task_id}`` every 500 ms.
    """
    try:
        result = format_service.start_task(req.files, req.profile, req.output_dir)
    except ValueError as exc:
        return _error(ErrorCode.PARAM_ERROR, str(exc))
    except FileNotFoundError as exc:
        return _error(ErrorCode.FILE_NOT_FOUND, str(exc))

    return JSONResponse(
        content=success_response({"taskId": result["task_id"]}),
        status_code=202,
    )


# 9.2 — Query task status
@router.get("/status/{task_id}")
async def get_status(task_id: str):
    """Return current task state, progress, and active file."""
    status = format_service.get_task_status(task_id)

    if status is None:
        return _error(ErrorCode.TASK_NOT_FOUND)

    return success_response(status)


# 9.3 — Cancel task
@router.post("/cancel")
async def cancel_task(req: CancelRequest):
    """Request cancellation of a running task.

    Already-processed files are kept; remaining files are skipped.
    """
    try:
        format_service.cancel_task(req.task_id)
    except KeyError:
        return _error(ErrorCode.TASK_NOT_FOUND)

    return success_response(message="Task cancelled")


# 9.4 — Get task result
@router.get("/result/{task_id}")
async def get_result(task_id: str):
    """Return the detailed result after a task finishes."""
    result = format_service.get_task_result(task_id)

    if result is None:
        return _error(ErrorCode.TASK_NOT_FOUND)

    if result.get("_not_finished"):
        return _error(ErrorCode.TASK_NOT_FOUND, "Task has not finished yet")

    return success_response(result)
