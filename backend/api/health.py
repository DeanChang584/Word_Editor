"""Health check endpoint.

Exposes ``GET /api/health`` so the frontend can confirm the backend is up
before issuing any other request.
"""

from fastapi import APIRouter

from backend.utils.response import success_response
from shared.version import VERSION

router = APIRouter()


@router.get("/health")
async def health() -> dict:
    """Return service health in the unified response envelope."""
    return success_response({"status": "ok", "version": VERSION})
