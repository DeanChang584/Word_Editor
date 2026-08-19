"""
Word Formatter — Shared DTO definitions (Step 1.4)

All data-transfer objects used by the API layer, template storage, and
frontend↔backend JSON contract live here.

Convention (per data model.md §13 and Q1 decision):
  - Python internals:  snake_case  (field names)
  - JSON wire format:  camelCase   (via Pydantic alias_generator)

Each class exposes:
  - to_dict()           → dict with camelCase keys (for JSONResponse)
  - from_dict(d)        → class instance, accepts camelCase or snake_case
  - to_json(indent=2)   → JSON string
  - from_json(text)     → class instance
"""

from __future__ import annotations

import json
from typing import Any, Optional

from pydantic import BaseModel, ConfigDict, Field, field_validator


# ============================================================
# Helpers
# ============================================================


def _to_camel(snake: str) -> str:
    """Convert ``snake_case`` to ``camelCase``."""
    parts = snake.split("_")
    return parts[0] + "".join(p.capitalize() for p in parts[1:])


# ============================================================
# Base model with shared configuration
# ============================================================


class _Base(BaseModel):
    """Common config: snake_case fields → camelCase JSON, with convenience
    ``to_dict`` / ``from_dict`` / ``to_json`` / ``from_json`` helpers."""

    model_config = ConfigDict(
        alias_generator=_to_camel,
        populate_by_name=True,   # accept snake_case AND camelCase on input
        serialize_by_alias=True, # model_dump() emits camelCase keys
        extra="ignore",          # silently drop unknown keys on input
    )

    # ---- serialisation helpers ----

    def to_dict(self) -> dict[str, Any]:
        """Return a dict with camelCase keys (alias names)."""
        return self.model_dump(by_alias=True)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "_Base":
        """Create an instance from a dict (accepts camelCase or snake_case)."""
        return cls.model_validate(data)

    def to_json(self, indent: int | None = 2) -> str:
        """Serialise to a JSON string with camelCase keys."""
        return self.model_dump_json(by_alias=True, indent=indent)

    @classmethod
    def from_json(cls, text: str) -> "_Base":
        """Deserialise from a JSON string (camelCase or snake_case)."""
        return cls.model_validate_json(text)


# ============================================================
# §3  FileItem
# ============================================================


class FileItem(_Base):
    """A Word file queued for processing."""

    id: str = ""
    name: str = ""
    path: str = ""
    size: int = 0
    modified_time: str = Field(default="", alias="modifiedTime")
    status: str = "waiting"  # waiting / running / done / error


# ============================================================
# §4.1  DocumentGridConfig
# ============================================================


class DocumentGridConfig(_Base):
    """Document grid settings (Word 文档网格).

    Mode options:
      - "none"  : no grid (free layout)
      - "lines" : lines-only grid (fixed rows per page)
      - "both"  : lines + character grid (fixed rows and chars per line)
    """

    mode: str = "none"              # none / lines / both
    lines_per_page: int = 30
    chars_per_line: int = 35
    adjust_right_indent: bool = True
    align_to_grid: bool = True


# ============================================================
# §4.2  PageConfig
# ============================================================


class PageConfig(_Base):
    """Page layout settings."""

    paper_size: str = "A4"
    orientation: str = "portrait"  # portrait / landscape
    margin_top: float = 25.4
    margin_bottom: float = 25.4
    margin_left: float = 31.7
    margin_right: float = 31.7
    page_number: bool = True
    custom_width: float = 0.0   # mm, used when paper_size == "custom"
    custom_height: float = 0.0  # mm, used when paper_size == "custom"
    document_grid: DocumentGridConfig = Field(default_factory=DocumentGridConfig)


# ============================================================
# §4.2  HeaderFooterConfig
# ============================================================


class HeaderFooterConfig(_Base):
    """Header / footer typography and positioning."""

    font_cn: str = "宋体"
    font_en: str = "Times New Roman"
    font_size: float = 10.5
    font_style: str = "normal"   # normal / bold / italic / underline / strikethrough (space-separated combinations)
    alignment: str = "center"    # left / center / right
    header_distance: float = 15.0
    footer_distance: float = 17.5


# ============================================================
# §4.3  BodyConfig
# ============================================================


class BodyConfig(_Base):
    """Body text style settings."""

    font_cn: str = "宋体"
    font_en: str = "Times New Roman"
    font_size: float = 12.0
    font_style: str = "normal"    # normal / bold / italic
    alignment: str = "justify"    # left / center / right / justify
    line_spacing: float = 1.5
    line_spacing_mode: str = "multiple"  # multiple / fixed / at_least
    line_spacing_unit: str = Field(default="pt", alias="lineSpacingUnit")
    indent_type: str = "firstLine"  # firstLine / none / hanging
    indent_value: float = 2.0
    indent_unit: str = Field(default="字符", alias="indentUnit")
    space_before: float = 0.0
    space_after: float = 0.0
    space_before_unit: str = Field(default="行", alias="spaceBeforeUnit")
    space_after_unit: str = Field(default="行", alias="spaceAfterUnit")


# ============================================================
# §5  HeadingStyleConfig
# ============================================================


class HeadingStyleConfig(_Base):
    """Per-level heading style (1–6)."""

    level: int = 1
    font_cn: str = "黑体"
    font_en: str = "Times New Roman"
    font_size: float = 22.0
    font_style: str = "bold"       # normal / bold / italic
    font_color: str = "#000000"
    alignment: str = "left"        # left / center / right / justify
    line_spacing: float = 1.5
    line_spacing_mode: str = "multiple"  # multiple / fixed / at_least / single
    line_spacing_unit: str = Field(default="pt", alias="lineSpacingUnit")
    indent_type: str = "none"      # firstLine / none / hanging
    indent_value: float = 0.0
    indent_unit: str = Field(default="字符", alias="indentUnit")
    space_before: float = 0.0
    space_after: float = 0.0
    space_before_unit: str = Field(default="行", alias="spaceBeforeUnit")
    space_after_unit: str = Field(default="行", alias="spaceAfterUnit")


# ============================================================
# §9.5  PictureConfig  (design-document.md 9.5)
# ============================================================


class PictureConfig(_Base):
    """Image formatting settings.

    size_mode controls which dimension is user-specified:
      - "width"  : user sets width, height auto-calculated from ratio
      - "height" : user sets height, width auto-calculated from ratio
      - "auto"   : keep original image dimensions
    """

    # Size mode
    size_mode: str = "auto"        # width / height / auto

    # Dimensions
    width: float = 14.0
    width_unit: str = "cm"
    height: float = 8.0
    height_unit: str = "cm"

    # Scaling
    keep_ratio: bool = Field(default=True, alias="keepAspectRatio")
    no_enlarge: bool = True

    # Layout
    alignment: str = "center"      # left / center / right / top / middle / bottom / distribute_h / distribute_v
    wrapping_style: str = "inline" # inline / square / tight / through / topBottom / behindText / inFrontOfText

    # Compression
    quality: int = 85
    max_side_pixels: int = 1600
    max_file_size: int = 2 * 1024 * 1024  # 2MB
    auto_compress: bool = False


# ============================================================
# §9.6  TableConfig  (design-document.md 9.6)
# ============================================================


class TableConfig(_Base):
    """Table formatting settings — full implementation."""

    # Layout
    table_alignment: str = "left"          # center / left / right
    width_mode: str = "auto"               # auto / fixed
    width_value: float = 16.0
    width_unit: str = "cm"
    auto_fit_columns: bool = True

    # Header style
    header_font_cn: str = "宋体"
    header_font_en: str = "Times New Roman"
    header_size: float = 10.5
    header_bold: bool = True
    header_text_center: bool = True
    header_bg_color: str = "none"          # "none" or hex color e.g. "#D9E2F3"
    body_bg_color: str = "none"            # "none" or hex color e.g. "#FFFFFF"

    # Border
    border_style: str = "all"              # none / all / horizontal / grid
    border_color: str = "#000000"
    border_width: float = 0.5

    # Cell alignment
    cell_align_h: str = "left"             # left / center / right
    cell_align_v: str = "center"           # top / middle / bottom
    cell_margin_h: float = 0.19
    cell_margin_h_unit: str = "cm"
    cell_margin_v: float = 0.0
    cell_margin_v_unit: str = "cm"

    # Indent (special format)
    indent_type: str = "none"           # firstLine / none / hanging
    indent_value: float = 0.0
    indent_unit: str = Field(default="字符", alias="indentUnit")

    # Global font style (applied to all cells)
    font_bold: bool = False
    font_italic: bool = False
    font_underline: bool = False

    # Row height
    row_height_mode: str = "auto"          # auto / fixed / at_least
    row_height: float = 0.8
    row_height_unit: str = "cm"

    # Line spacing
    line_spacing: float = 1.5
    line_spacing_mode: str = "multiple"
    line_spacing_unit: str = Field(default="pt", alias="lineSpacingUnit")

    # Pagination
    auto_split: bool = True
    repeat_header: bool = False


# ============================================================
# §4  ProfileConfig  (composite — all formatting params)
# ============================================================


def _default_headings() -> dict[str, HeadingStyleConfig]:
    """Create fresh HeadingStyleConfig instances with per-level defaults."""
    defaults = {
        1: HeadingStyleConfig(level=1, font_size=22.0, space_before=1.0, space_after=1.0),
        2: HeadingStyleConfig(level=2, font_size=18.0, space_before=1.0, space_after=1.0),
        3: HeadingStyleConfig(level=3, font_size=16.0, space_before=0.5, space_after=0.5),
        4: HeadingStyleConfig(level=4, font_size=14.0, space_before=0.5, space_after=0.5),
        5: HeadingStyleConfig(level=5, font_size=12.0, space_before=0.25, space_after=0.25),
        6: HeadingStyleConfig(level=6, font_size=10.5, space_before=0.25, space_after=0.25),
    }
    return {str(i): defaults[i] for i in range(1, 7)}


class ProfileConfig(_Base):
    """Complete formatting profile — the single unit passed to the engine."""

    page: PageConfig = Field(default_factory=PageConfig)
    header_footer: HeaderFooterConfig = Field(default_factory=HeaderFooterConfig)
    body: BodyConfig = Field(default_factory=BodyConfig)
    heading: dict[str, HeadingStyleConfig] = Field(
        default_factory=_default_headings,
    )
    picture: PictureConfig = Field(default_factory=PictureConfig)
    table: TableConfig = Field(default_factory=TableConfig)


# ============================================================
# §6  Template
# ============================================================


class Template(_Base):
    """A saved formatting template (wraps a ProfileConfig)."""

    id: str = ""
    name: str = ""
    version: str = "2.0"
    author: str = ""
    description: str = ""
    create_time: str = Field(default="", alias="createTime")
    update_time: str = Field(default="", alias="updateTime")
    profile: ProfileConfig = Field(default_factory=ProfileConfig)


# ============================================================
# §7  Task
# ============================================================


class Task(_Base):
    """A batch-formatting task."""

    task_id: str = Field(default="", alias="taskId")
    status: str = "idle"  # idle/preparing/running/saving/completed/failed/cancelled
    create_time: str = Field(default="", alias="createTime")
    start_time: Optional[str] = Field(default=None, alias="startTime")
    finish_time: Optional[str] = Field(default=None, alias="finishTime")
    files: list[FileItem] = Field(default_factory=list)


# ============================================================
# §8  TaskResult
# ============================================================


class FileResult(_Base):
    """Result for a single file within a task."""

    file: str = ""
    status: str = "success"  # success / error / skipped
    output: str = ""
    output_path: str = Field(default="", alias="outputPath")
    message: str = ""


class FailedFileItem(_Base):
    """One failed file within a task result (for frontend retry)."""

    file: str = ""
    path: str = ""
    status: str = "error"


class TaskResult(_Base):
    """Aggregate result after a task finishes."""

    success: int = 0
    failed: int = 0
    skipped: int = 0
    elapsed: float = 0.0
    output_directory: str = Field(default="", alias="outputDirectory")
    results: list[FileResult] = Field(default_factory=list)
    failed_files: list[FailedFileItem] = Field(default_factory=list, alias="failedFiles")


# ============================================================
# §9  PreviewResult
# ============================================================


class PreviewResult(_Base):
    """Preview output (Level 1 = parameter summary text)."""

    page_count: int = Field(default=0, alias="pageCount")
    warnings: list[str] = Field(default_factory=list)
    preview_images: list[str] = Field(default_factory=list, alias="previewImages")


# ============================================================
# §10  HistoryRecord
# ============================================================


class HistoryFileItem(_Base):
    """One file within a history record with metadata.

    Contains display name, full path, and formatting status so the
    frontend can show rich file lists without recomputation.
    """

    name: str = ""
    path: str = ""
    status: str = ""  # "success" / "error" / "skipped" / ""
    output_name: str = ""
    output_path: str = ""


class HistoryRecord(_Base):
    """One completed task saved in the history ledger.

    Summary fields (task_id, time, template, file_count, elapsed) are always
    present.  Detail fields (success, failed, skipped, profile, files,
    results) are populated in the persisted JSON so the detail endpoint can
    return them without recomputation.
    """

    # Summary fields (returned by list endpoint)
    task_id: str = Field(default="", alias="taskId")
    time: str = ""
    template: str = ""
    file_count: int = Field(default=0, alias="fileCount")
    elapsed: float = 0.0
    success: int = 0
    failed: int = 0
    skipped: int = Field(default=0, alias="skipped")

    # Detail fields (returned only by detail endpoint)
    profile: Optional[dict[str, Any]] = None
    files: list[HistoryFileItem] = Field(default_factory=list)
    results: Optional[dict[str, Any]] = None

    @field_validator("files", mode="before")
    @classmethod
    def _coerce_files(cls, v: Any) -> list[dict[str, Any]]:
        """Accept both old string-list and new HistoryFileItem-list formats."""
        if not v:
            return []
        result: list[dict[str, Any]] = []
        for item in v:
            if isinstance(item, str):
                result.append({"name": item, "path": item, "status": ""})
            elif isinstance(item, dict):
                result.append(item)
            elif hasattr(item, "to_dict"):
                result.append(item.to_dict())
            else:
                result.append({"name": str(item), "path": str(item), "status": ""})
        return result


# ============================================================
# §11  Settings
# ============================================================


class Settings(_Base):
    """Application-wide settings (independent of formatting profiles)."""

    theme: str = "light"             # system / light / dark
    language: str = "zh-CN"
    default_output: str = Field(default="sameFolder", alias="defaultOutput")
    default_template: str = Field(default="Default", alias="defaultTemplate")
    recent_count: int = Field(default=20, alias="recentCount")
    auto_check_update: bool = Field(default=True, alias="autoCheckUpdate")


# ============================================================
# §12  LogEntry
# ============================================================


class LogEntry(_Base):
    """A single structured log record."""

    time: str = ""
    level: str = "INFO"   # INFO / WARNING / ERROR / DEBUG
    module: str = ""
    message: str = ""