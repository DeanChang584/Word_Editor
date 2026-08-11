"""Unit tests for theme default values (spec: default light)."""

from __future__ import annotations

from backend.config.defaults import DEFAULT_SETTINGS
from shared.schemas import Settings


def test_default_settings_theme_is_light():
    assert DEFAULT_SETTINGS["theme"] == "light"


def test_schema_settings_theme_is_light():
    assert Settings().theme == "light"
