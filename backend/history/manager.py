"""History record manager (Step 4.1).

Persists completed formatting-task records as individual JSON files under
``%LOCALAPPDATA%\\WordFormatter\\history``.  Each record is a
``HistoryRecord`` (see ``shared.schemas``).  The manager provides CRUD
operations and a hook for ``FormatService`` to call after a task finishes.

Storage layout::

    %LOCALAPPDATA%/WordFormatter/history/
    ├── h_abc123.json
    ├── h_def456.json
    └── ...
"""

from __future__ import annotations

import json
import threading
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from backend.utils.logger import get_logger
from backend.utils.app_paths import HISTORY_DIR as _HISTORY_DIR
from shared.schemas import HistoryRecord
from shared.version import VERSION

logger = get_logger("backend.history.manager", category="backend")

# Maximum number of history records to keep (API.md §11.1: last 50)
_MAX_RECORDS = 50


class HistoryManager:
    """Thread-safe history record manager with JSON-file persistence.

    Each record is stored as ``{id}.json`` in ``%LOCALAPPDATA%``.  Records
    are loaded lazily on first access and cached in memory.
    """

    def __init__(self) -> None:
        self._records: dict[str, HistoryRecord] = {}
        self._order: list[str] = []  # newest-first insertion order
        self._lock = threading.Lock()
        self._initialised = False

    # ------------------------------------------------------------------
    # Public query methods
    # ------------------------------------------------------------------

    def get_all(self) -> list[dict[str, Any]]:
        """Return summary dicts for the most recent ``_MAX_RECORDS`` tasks.

        Each dict contains: id, time, duration, success, failed, template,
        fileCount, and files (name + status per file).
        Ordered newest-first.
        """
        self._ensure_loaded()
        summaries: list[dict[str, Any]] = []
        with self._lock:
            for rid in self._order[:_MAX_RECORDS]:
                rec = self._records[rid]
                # Build file summaries: name, path + status for each file
                file_summaries: list[dict[str, str]] = []
                for f in rec.files:
                    file_summaries.append({
                        "name": f.name,
                        "path": f.path,
                        "status": f.status,
                        "outputName": f.output_name,
                        "outputPath": f.output_path,
                    })
                summaries.append({
                    "id": rec.task_id,
                    "time": rec.time,
                    "duration": rec.elapsed,
                    "success": rec.success,
                    "failed": rec.failed,
                    "template": rec.template,
                    "fileCount": rec.file_count,
                    "files": file_summaries,
                })
        return summaries

    def get_detail(self, record_id: str) -> dict[str, Any] | None:
        """Return the full detail dict for a single record, or None."""
        self._ensure_loaded()
        with self._lock:
            rec = self._records.get(record_id)
        if rec is None:
            return None
        return {
            "id": rec.task_id,
            "time": rec.time,
            "duration": rec.elapsed,
            "profile": rec.profile,
            "files": rec.files,
            "results": rec.results,
            "success": rec.success,
            "failed": rec.failed,
            "skipped": rec.skipped,
            "template": rec.template,
            "fileCount": rec.file_count,
        }

    def clear(self) -> int:
        """Delete all history records from disk and memory.

        Returns the number of records removed.
        """
        self._ensure_loaded()
        with self._lock:
            count = len(self._records)
            for rid in list(self._records.keys()):
                self._delete_file(rid)
            self._records.clear()
            self._order.clear()
        logger.info("History cleared (%d records removed)", count)
        return count

    # ------------------------------------------------------------------
    # Public mutation (called by FormatService)
    # ------------------------------------------------------------------

    def save_record(
        self,
        task_id: str,
        profile: dict[str, Any] | None,
        file_paths: list[str],
        result: dict[str, Any],
        template_name: str = "",
    ) -> None:
        """Persist a completed task result as a history record.

        Called by ``FormatService._run_task`` after a task finishes.
        Builds ``HistoryFileItem`` entries from the full file paths and
        per-file statuses in the result dict.
        """
        self._ensure_loaded()

        now = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

        # Build rich file list: name + path + status
        from shared.schemas import HistoryFileItem
        result_results = result.get("results", [])
        status_map: dict[str, str] = {}
        output_map: dict[str, tuple[str, str]] = {}
        for r in result_results:
            f = r.get("file", "")
            status_map[f] = r.get("status", "")
            output_map[f] = (r.get("output", ""), r.get("outputPath", ""))

        file_items: list[HistoryFileItem] = []
        for p in file_paths:
            fname = Path(p).name
            out_name, out_path = output_map.get(fname, ("", ""))
            file_items.append(HistoryFileItem(
                name=fname,
                path=str(Path(p).resolve()),
                status=status_map.get(fname, ""),
                output_name=out_name,
                output_path=out_path,
            ))

        rec = HistoryRecord(
            task_id=task_id,
            time=now,
            template=template_name,
            file_count=result.get("fileCount", len(file_paths)),
            elapsed=result.get("elapsed", 0.0),
            success=result.get("success", 0),
            failed=result.get("failed", 0),
            skipped=result.get("skipped", 0),
            profile=profile,
            files=file_items,
            results=result,
        )

        with self._lock:
            self._records[task_id] = rec
            # Insert at front (newest first), avoid duplicates
            if task_id in self._order:
                self._order.remove(task_id)
            self._order.insert(0, task_id)

        # Persist to disk
        self._save_file(rec)

        # Enforce max records
        self._trim()

        logger.info("History saved: id=%s, files=%d, ok=%d, fail=%d",
                     task_id, rec.file_count, rec.success, rec.failed)

    # ------------------------------------------------------------------
    # Private helpers
    # ------------------------------------------------------------------

    def _ensure_loaded(self) -> None:
        """Lazy-load records from disk on first access."""
        if self._initialised:
            return
        with self._lock:
            if self._initialised:
                return
            self._load_from_disk()
            self._initialised = True

    def _load_from_disk(self) -> None:
        """Load all ``*.json`` files from the history directory."""
        self._ensure_dir()
        loaded = 0
        for fpath in sorted(_HISTORY_DIR.glob("*.json"), reverse=True):
            if fpath.name == "index.json":
                continue
            try:
                text = fpath.read_text(encoding="utf-8")
                data = json.loads(text)
                rec = HistoryRecord.from_dict(data)
                rid = rec.task_id
                self._records[rid] = rec
                self._order.append(rid)
                loaded += 1
            except Exception as exc:
                logger.warning("Failed to load history file %s: %s", fpath.name, exc)
        # Sort by time descending (newest first)
        self._order.sort(
            key=lambda rid: self._records[rid].time,
            reverse=True,
        )
        logger.info("Loaded %d history records from disk", loaded)

    def _save_file(self, rec: HistoryRecord) -> None:
        """Write a single record to disk."""
        self._ensure_dir()
        fpath = _HISTORY_DIR / f"{rec.task_id}.json"
        try:
            fpath.write_text(rec.to_json(), encoding="utf-8")
        except Exception as exc:
            logger.error("Failed to save history file %s: %s", fpath.name, exc)

    def _delete_file(self, record_id: str) -> None:
        """Delete a single record file from disk."""
        fpath = _HISTORY_DIR / f"{record_id}.json"
        try:
            if fpath.exists():
                fpath.unlink()
        except Exception as exc:
            logger.warning("Failed to delete history file %s: %s", fpath.name, exc)

    def _trim(self) -> None:
        """Remove oldest records exceeding ``_MAX_RECORDS``."""
        with self._lock:
            while len(self._order) > _MAX_RECORDS:
                old_id = self._order.pop()
                self._records.pop(old_id, None)
                self._delete_file(old_id)
                logger.debug("Trimmed old history record: %s", old_id)

    @staticmethod
    def _ensure_dir() -> None:
        _HISTORY_DIR.mkdir(parents=True, exist_ok=True)


# Module-level singleton
history_manager = HistoryManager()
