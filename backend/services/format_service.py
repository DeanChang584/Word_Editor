"""Format task service (Step 3.2).

Extracts all business logic from ``backend.api.format`` into a reusable
service class. Handles task creation, state management, progress updates,
cancellation, and result aggregation.

The API layer (``backend.api.format``) should only validate request
parameters and delegate to this service.
"""

from __future__ import annotations

import threading
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from backend.history.manager import history_manager
from backend.utils.logger import get_logger
from shared.constants import TaskState
from shared.schemas import FileResult, ProfileConfig, TaskResult

logger = get_logger("backend.services.format_service", category="backend")

# Allowed file extensions for formatting
_ALLOWED_EXTENSIONS = {".doc", ".docx"}


class FormatService:
    """Thread-safe formatting task service.

    Manages the lifecycle of batch-formatting tasks: creation, background
    execution, progress tracking, cancellation, and result retrieval.
    Each task runs in its own daemon thread.
    """

    def __init__(self) -> None:
        # Task registry: task_id → task dict
        self._tasks: dict[str, dict[str, Any]] = {}
        self._task_lock = threading.Lock()

    # ------------------------------------------------------------------
    # Public query methods
    # ------------------------------------------------------------------

    def get_task_status(self, task_id: str) -> dict[str, Any] | None:
        """Return current task state, progress, and active file.

        Returns ``None`` if the task does not exist.
        """
        with self._task_lock:
            task = self._tasks.get(task_id)
            if task is None:
                return None
            return {
                "state": task["status"],
                "progress": task["progress"],
                "current": task["current"],
                "total": task["total"],
                "currentFile": task["currentFile"],
            }

    def get_task_result(self, task_id: str) -> dict[str, Any] | None:
        """Return the detailed result after a task finishes.

        Returns ``None`` if the task does not exist.
        Returns a special sentinel ``{"_not_finished": True}`` if the task
        exists but has not finished yet (API layer should distinguish).

        Terminal tasks are pruned from the registry once their result is
        served, so ``_tasks`` does not grow without bound over the lifetime
        of the backend process. The frontend fetches the result exactly once
        after the task reaches a terminal state, so pruning here is safe.
        """
        with self._task_lock:
            task = self._tasks.get(task_id)
            if task is None:
                return None
            if task["result"] is None:
                return {"_not_finished": True}
            result = task["result"]
            if task["status"] in (TaskState.COMPLETED, TaskState.FAILED, TaskState.CANCELLED):
                del self._tasks[task_id]
            return result

    def task_exists(self, task_id: str) -> bool:
        """Check if a task ID exists."""
        with self._task_lock:
            return task_id in self._tasks

    def get_task_state(self, task_id: str) -> str | None:
        """Return raw status string for a task, or None if not found."""
        with self._task_lock:
            task = self._tasks.get(task_id)
            return task["status"] if task else None

    # ------------------------------------------------------------------
    # Public mutation methods
    # ------------------------------------------------------------------

    def start_task(
        self,
        file_paths: list[str],
        profile_spec: str | dict[str, Any] = "default",
        output_dir: str = "",
    ) -> dict[str, Any]:
        """Create and launch a new formatting task.

        Validates files, resolves the profile, creates the task registry
        entry, and starts a background daemon thread.

        Returns a dict with:
        - task_id (str): the new task ID
        - file_count (int): number of valid files

        Raises FileNotFoundError if no valid .doc/.docx files are found.
        Raises ValueError if file_paths is empty.
        """
        if not file_paths:
            raise ValueError("files must not be empty")

        # Validate files exist and are .doc/.docx
        valid_paths: list[str] = []
        for raw in file_paths:
            p = Path(raw)
            if p.exists() and p.suffix.lower() in _ALLOWED_EXTENSIONS:
                valid_paths.append(str(p.resolve()))
            else:
                logger.warning("Skipping invalid file: %s", raw)

        if not valid_paths:
            raise FileNotFoundError("No valid .doc/.docx files found")

        # Generate task ID and cancel event
        task_id = "task_" + uuid.uuid4().hex[:10]
        cancel_event = threading.Event()

        # Resolve profile BEFORE registering the task. A malformed profile
        # dict raises here (Propagates as ValueError → HTTP 400), and the
        # task never enters the registry — otherwise a zombie entry with no
        # worker thread would sit at "preparing" forever.
        profile, template_name = self._resolve_profile(profile_spec)

        task: dict[str, Any] = {
            "status": TaskState.PREPARING,
            "startTime": None,
            "finishTime": None,
            "progress": 0,
            "current": 0,
            "total": len(valid_paths),
            "currentFile": "",
            "result": None,
            "_cancel_event": cancel_event,
            "_template_name": template_name,
        }

        with self._task_lock:
            self._tasks[task_id] = task

        # Start background thread
        thread = threading.Thread(
            target=self._run_task,
            args=(task_id, valid_paths, profile, output_dir),
            daemon=True,
            name=f"format-{task_id}",
        )
        thread.start()

        logger.info("Task started: id=%s, files=%d", task_id, len(valid_paths))

        return {"task_id": task_id, "file_count": len(valid_paths)}

    def cancel_task(self, task_id: str) -> dict[str, Any]:
        """Request cancellation of a running task.

        Returns a dict with:
        - success (bool): True if cancellation was requested
        - reason (str): message describing the result

        Raises KeyError if the task does not exist.
        Raises RuntimeError if the task is already in a terminal state.
        """
        with self._task_lock:
            task = self._tasks.get(task_id)

        if task is None:
            raise KeyError(f"Task not found: {task_id}")

        if task["status"] not in (TaskState.PREPARING, TaskState.RUNNING):
            # 任务已结束 → 取消是无意义的 no-op，返回成功而非报错，
            # 避免前端对"已完成"任务误报"取消请求发送失败"。
            logger.info("Cancel ignored: task %s already %s", task_id, task["status"])
            return {"success": True, "reason": f"Task already {task['status']}"}

        cancel_event: threading.Event = task["_cancel_event"]
        cancel_event.set()

        logger.info("Task cancel requested: id=%s", task_id)
        return {"success": True, "reason": "Task cancelled"}

    # ------------------------------------------------------------------
    # Private helpers
    # ------------------------------------------------------------------

    @staticmethod
    def _now_iso() -> str:
        return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    @staticmethod
    def _resolve_profile(profile_spec: str | dict[str, Any]) -> tuple[ProfileConfig, str]:
        """Resolve a profile spec to (ProfileConfig, template_name).

        If the spec is a non-empty string that isn't a plain dict, treat it as
        a template ID and load from the templates cache. Otherwise parse as
        a ProfileConfig dict.

        Returns a tuple of (ProfileConfig, template_name).  ``template_name``
        is the human-readable name when resolved from a template, or ``""``
        when using a raw profile dict or default.
        """
        if isinstance(profile_spec, str) and profile_spec:
            # Try loading from template service
            try:
                from backend.services.template_service import template_service
                tmpl = template_service.get_template(profile_spec)
                if tmpl is not None:
                    return tmpl.profile, tmpl.name
            except (KeyError, FileNotFoundError, OSError):
                # Template not found or cannot be loaded — fallback to default
                pass
            # Fallback: use default profile
            return ProfileConfig(), ""

        if isinstance(profile_spec, dict):
            return ProfileConfig.from_dict(profile_spec), ""

        return ProfileConfig(), ""

    def _run_task(
        self,
        task_id: str,
        file_paths: list[str],
        profile: ProfileConfig,
        output_dir: str,
    ) -> None:
        """Background worker: process each file sequentially.

        Runs in a daemon thread. Updates the task dict in-place so the
        status endpoint can read progress at any time.
        """
        from backend.formatter.engine import process_file

        with self._task_lock:
            task = self._tasks[task_id]
        total = len(file_paths)
        start_time = time.monotonic()

        # Ensure output dir exists
        if output_dir:
            Path(output_dir).mkdir(parents=True, exist_ok=True)

        results: list[dict[str, Any]] = []
        failed_files: list[dict[str, Any]] = []
        ok_count = 0
        fail_count = 0
        skipped_count = 0

        with self._task_lock:
            task["status"] = TaskState.RUNNING
            task["startTime"] = self._now_iso()

        for idx, fpath in enumerate(file_paths):
            # Check cancellation
            if task["_cancel_event"].is_set():
                # Mark remaining files as skipped
                skipped_count += total - idx
                for remaining in file_paths[idx:]:
                    results.append({
                        "file": Path(remaining).name,
                        "status": "skipped",
                        "output": "",
                        "message": "Cancelled",
                    })
                with self._task_lock:
                    task["status"] = TaskState.CANCELLED
                    task["current"] = idx
                    task["currentFile"] = ""
                break

            fname = Path(fpath).name
            with self._task_lock:
                task["current"] = idx + 1
                task["currentFile"] = fname

            logger.info("Processing [%d/%d]: %s", idx + 1, total, fname)

            try:
                ok, msg, out_path = process_file(fpath, profile, output_dir)
            except Exception as exc:
                ok, msg, out_path = False, f"Unexpected error: {exc}", ""

            if ok:
                ok_count += 1
                out_name = Path(out_path).name if out_path else ""
                results.append({
                    "file": fname,
                    "status": "success",
                    "output": out_name,
                    "outputPath": out_path,
                    "message": msg,
                })
            else:
                fail_count += 1
                results.append({
                    "file": fname,
                    "status": "error",
                    "output": "",
                    "message": msg,
                })
                # 记录完整路径 —— 前端重试失败文件需要全路径而非仅文件名
                failed_files.append({
                    "file": fname,
                    "path": fpath,
                    "status": "error",
                })

            # Update progress (0-100)
            with self._task_lock:
                task["progress"] = int((idx + 1) / total * 100)

        elapsed = round(time.monotonic() - start_time, 2)

        # Build typed TaskResult
        file_results = [FileResult(**r) for r in results]
        result_obj = TaskResult.from_dict({
            "success": ok_count,
            "failed": fail_count,
            "skipped": skipped_count,
            "elapsed": elapsed,
            "outputDirectory": output_dir,
            "results": [r.to_dict() for r in file_results],
            "failed_files": failed_files,
        })
        with self._task_lock:
            if task["status"] not in (TaskState.CANCELLED,):
                # 全部文件都失败 → FAILED 终态（前端轮询依赖该状态触发结果加载）
                task["status"] = (
                    TaskState.FAILED
                    if ok_count == 0 and fail_count > 0
                    else TaskState.COMPLETED
                )
            task["finishTime"] = self._now_iso()
            task["result"] = result_obj.to_dict()
            task["currentFile"] = ""
            template_name = task.get("_template_name", "")

        logger.info(
            "Task %s finished: ok=%d, fail=%d, skipped=%d, elapsed=%.2fs",
            task_id, ok_count, fail_count, skipped_count, elapsed,
        )

        # Persist to history (Step 4.1)
        try:
            profile_dict = profile.to_dict() if profile else None
            history_manager.save_record(
                task_id=task_id,
                profile=profile_dict,
                file_paths=file_paths,
                result=task["result"],
                template_name=template_name,
            )
        except Exception as exc:
            logger.error("Failed to save history for task %s: %s", task_id, exc)


# Module-level singleton — the API layer imports this instance
format_service = FormatService()
