"""
Word Formatter — Settings manager (Step 1.3)

Loads, caches, and persists software-wide settings (data model.md §11).

Storage path: ``%LOCALAPPDATA%\\WordFormatter\\settings.json``.

Thread-safe: a threading.Lock serialises reads/writes so concurrent API
requests never corrupt the JSON file.

Public API:
    get_setting(key, default=None) -> value
    get_all_settings() -> dict
    update_settings(partial: dict) -> dict          (merged result)
    reset_settings() -> dict                        (back to defaults)
"""

import json
import threading

from backend.config.defaults import DEFAULT_SETTINGS
from backend.utils.logger import get_logger
from backend.utils.app_paths import SETTINGS_FILE as _SETTINGS_FILE

# ============================================================
# Paths
# ============================================================

SETTINGS_FILE = _SETTINGS_FILE

logger = get_logger("backend.config", category="backend")

# ============================================================
# Internal state
# ============================================================

_lock = threading.Lock()
_cache: dict | None = None  # lazily loaded on first access


# ============================================================
# Helpers
# ============================================================


def _ensure_dir() -> None:
    """Create the config directory if it doesn't exist."""
    SETTINGS_FILE.parent.mkdir(parents=True, exist_ok=True)


def _load_from_disk() -> dict:
    """Read settings.json and merge over defaults (missing keys filled).

    Returns a complete settings dict — every key from DEFAULT_SETTINGS
    is guaranteed present.
    """
    merged = dict(DEFAULT_SETTINGS)

    if SETTINGS_FILE.exists():
        try:
            with open(SETTINGS_FILE, encoding="utf-8") as f:
                stored = json.load(f)
            # Only accept known keys; ignore unknown ones gracefully.
            for k in DEFAULT_SETTINGS:
                if k in stored:
                    merged[k] = stored[k]
            logger.info("Settings loaded from %s", SETTINGS_FILE)
        except (json.JSONDecodeError, OSError) as exc:
            logger.warning("Failed to read %s, using defaults: %s", SETTINGS_FILE, exc)
    else:
        logger.info("No settings file found, using defaults")

    return merged


def _save_to_disk(settings: dict) -> None:
    """Persist settings to disk (atomic-ish: write then flush)."""
    _ensure_dir()
    try:
        with open(SETTINGS_FILE, "w", encoding="utf-8") as f:
            json.dump(settings, f, ensure_ascii=False, indent=2)
            f.write("\n")
        logger.info("Settings saved to %s", SETTINGS_FILE)
    except OSError as exc:
        logger.error("Failed to save settings: %s", exc)


def _get_cache() -> dict:
    """Return the in-memory settings, loading from disk on first call."""
    global _cache
    if _cache is None:
        _cache = _load_from_disk()
    return _cache


# ============================================================
# Public API
# ============================================================


def get_setting(key: str, default=None):
    """Return a single setting value.

    Falls back to *default* if the key doesn't exist in defaults either.
    """
    with _lock:
        return _get_cache().get(key, default)


def get_all_settings() -> dict:
    """Return a shallow copy of the full settings dict."""
    with _lock:
        return dict(_get_cache())


def update_settings(partial: dict) -> dict:
    """Merge *partial* into current settings, persist, and return the result.

    Only keys present in DEFAULT_SETTINGS are accepted; unknown keys are
    ignored with a log warning.
    """
    global _cache
    with _lock:
        current = _get_cache()
        for k, v in partial.items():
            if k not in DEFAULT_SETTINGS:
                logger.warning("Ignoring unknown setting key: %s", k)
                continue
            current[k] = v
        _save_to_disk(current)
        return dict(current)


def reset_settings() -> dict:
    """Reset all settings to factory defaults and persist."""
    global _cache
    with _lock:
        _cache = dict(DEFAULT_SETTINGS)
        _save_to_disk(_cache)
        logger.info("Settings reset to defaults")
        return dict(_cache)
