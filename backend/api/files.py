"""File management API endpoints (Step 1.6, refactored Step 3.1).

Implements API.md §6 — all 8 file-management endpoints under ``/api/files``.

All business logic has been extracted to ``backend.services.file_service``.
This module handles only parameter validation and response formatting.
"""

from __future__ import annotations

from fastapi import APIRouter
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field

from backend.services.file_service import file_service
from backend.utils.logger import get_logger
from backend.utils.response import ErrorCode, error_response, success_response

logger = get_logger("backend.api.files", category="backend")

router = APIRouter(prefix="/files", tags=["files"])


def _error(code: int, message: str | None = None) -> JSONResponse:
    """Shortcut: build error envelope and return as JSONResponse."""
    body, status = error_response(code, message)
    return JSONResponse(content=body, status_code=status)


# ============================================================
# Request / Response models (local to this module)
# ============================================================


class AddFilesRequest(BaseModel):
    paths: list[str]


class AddFolderRequest(BaseModel):
    folder: str
    include_subdir: bool = Field(default=True, alias="includeSubdir")


class RemoveFilesRequest(BaseModel):
    paths: list[str]


class SearchRequest(BaseModel):
    keyword: str


class PinFolderRequest(BaseModel):
    folder: str


# ============================================================
# Endpoints
# ============================================================


# 6.1 — Get current file list
@router.get("")
async def get_files() -> dict:
    """Return all files currently in the processing queue."""
    return success_response({"files": file_service.get_all_files()})


# 6.2 — Add files
@router.post("/add")
async def add_files(req: AddFilesRequest):
    """Add one or more .doc/.docx files by path."""
    if not req.paths:
        return _error(ErrorCode.PARAM_ERROR, "paths must not be empty")

    result = file_service.add_files(req.paths)

    if result["added"] == 0 and result["failures"]:
        return _error(
            ErrorCode.FILE_NOT_FOUND,
            f"All files invalid or not found: {result['failures']}",
        )

    response_data: dict = {"count": result["added"]}
    if result["failures"]:
        response_data["failures"] = result["failures"]

    return success_response(response_data, message="Files added")


# 6.3 — Add folder
# 同步 def（FastAPI 放入线程池执行）：递归 rglob + 逐文件 stat 是阻塞 IO，
# 若放在 async 处理器里会卡死事件循环，导致前端的 format/status 轮询全部超时。
@router.post("/add-folder")
def add_folder(req: AddFolderRequest):
    """Scan a folder for .doc/.docx files and add them."""
    try:
        result = file_service.add_folder(req.folder, req.include_subdir)
    except FileNotFoundError as exc:
        return _error(ErrorCode.FILE_NOT_FOUND, str(exc))

    return success_response({"count": result["added"]})


# 6.4 — Remove specified files
@router.post("/remove")
async def remove_files(req: RemoveFilesRequest):
    """Remove files from the queue by path."""
    if not req.paths:
        return _error(ErrorCode.PARAM_ERROR, "paths must not be empty")

    removed = file_service.remove_files(req.paths)
    return success_response({"count": removed}, message="Files removed")


# 6.5 — Clear file list
@router.delete("")
async def clear_files() -> dict:
    """Remove all files from the queue."""
    file_service.clear_files()
    return success_response(message="File list cleared")


# 6.6 — Search files
@router.post("/search")
async def search_files(req: SearchRequest) -> dict:
    """Filter the current file list by keyword (case-insensitive name match)."""
    matched = file_service.search_files(req.keyword)
    return success_response({"files": matched})


# 6.7 — Get recent open records
@router.get("/recent")
async def get_recent() -> dict:
    """Return recently opened files and folders (most recent first)."""
    return success_response({"recent": file_service.get_recent()})


# 6.8 — Pin a common directory
@router.post("/pin")
async def pin_folder(req: PinFolderRequest):
    """Pin a frequently used directory."""
    try:
        pinned = file_service.pin_folder(req.folder)
    except FileNotFoundError as exc:
        return _error(ErrorCode.FILE_NOT_FOUND, str(exc))

    return success_response({"pinned": pinned})
