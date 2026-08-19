"""Template management service (Step 3.3).

Extracts all business logic from ``backend.api.templates`` into a reusable
service class. Handles template CRUD, JSON persistence, version validation,
import/export, default template management, and preset seeding.

The API layer (``backend.api.templates``) should only validate request
parameters and delegate to this service.
"""

from __future__ import annotations

import json
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from backend.utils.logger import get_logger
from backend.utils.app_paths import TEMPLATES_DIR as _TEMPLATES_DIR
from shared.schemas import ProfileConfig, Template
from shared.version import VERSION

logger = get_logger("backend.services.template_service", category="backend")

# Paths — runtime data lives under %LOCALAPPDATA%\WordFormatter
_INDEX_FILE = _TEMPLATES_DIR / "index.json"


class TemplateService:
    """Template management service with disk-backed persistence.

    Templates are stored as individual JSON files in ``%LOCALAPPDATA%``.
    An ``index.json`` tracks ordering and the current default template ID.
    On first access, a single default template is seeded automatically.
    """

    def __init__(self) -> None:
        self._templates: dict[str, Template] = {}
        self._default_id: str = ""
        self._initialised: bool = False

    # ------------------------------------------------------------------
    # Public query methods
    # ------------------------------------------------------------------

    def get_all_templates(self) -> list[dict[str, Any]]:
        """Return list of all templates, each including its full profile.

        The profile is required by the frontend to apply a template when the
        user selects it (ApplySelectedTemplate reads ``TemplateDto.Profile``).
        Omitting it made template selection a silent no-op and left the apply
        path with a null profile.
        """
        self._load_from_disk()
        return [
            {
                "id": tmpl.id,
                "name": tmpl.name,
                "isDefault": tmpl.id == self._default_id,
                "profile": tmpl.profile.to_dict(),
            }
            for tmpl in self._templates.values()
        ]

    def get_template(self, template_id: str) -> Template | None:
        """Return a single Template object, or None if not found."""
        self._load_from_disk()
        return self._templates.get(template_id)

    def get_default_id(self) -> str:
        """Return the current default template ID."""
        self._load_from_disk()
        return self._default_id

    def template_exists(self, template_id: str) -> bool:
        """Check whether a template ID exists."""
        self._load_from_disk()
        return template_id in self._templates

    # ------------------------------------------------------------------
    # Public mutation methods
    # ------------------------------------------------------------------

    def create_template(self, name: str, profile_data: dict[str, Any] | None = None) -> dict[str, str]:
        """Create a new template.

        Returns {"id": ..., "name": ...}.
        """
        self._load_from_disk()

        tmpl_id = "tpl_" + uuid.uuid4().hex[:8]
        now = self._now_iso()

        profile = ProfileConfig.from_dict(profile_data) if profile_data else ProfileConfig()

        tmpl = Template(
            id=tmpl_id,
            name=name,
            version=VERSION,
            create_time=now,
            update_time=now,
            profile=profile,
        )

        self._templates[tmpl_id] = tmpl
        self._save_template_file(tmpl)
        self._save_index()

        logger.info("Template created: id=%s, name=%s", tmpl_id, name)
        return {"id": tmpl_id, "name": name}

    def update_template(
        self,
        template_id: str,
        name: str | None = None,
        profile_data: dict[str, Any] | None = None,
    ) -> None:
        """Update a template's name and/or profile.

        Raises KeyError if the template does not exist.
        """
        self._load_from_disk()

        if template_id not in self._templates:
            raise KeyError(f"Template not found: {template_id}")

        tmpl = self._templates[template_id]

        if name is not None:
            tmpl.name = name
        if profile_data is not None:
            tmpl.profile = ProfileConfig.from_dict(profile_data)

        tmpl.update_time = self._now_iso()
        self._save_template_file(tmpl)

        logger.info("Template updated: id=%s", template_id)

    def delete_template(self, template_id: str) -> None:
        """Delete a template.

        Raises KeyError if the template does not exist.
        Raises PermissionError if the template is the current default.
        """
        self._load_from_disk()

        if template_id not in self._templates:
            raise KeyError(f"Template not found: {template_id}")

        if template_id == self._default_id:
            raise PermissionError("Cannot delete the default template")

        del self._templates[template_id]
        self._delete_template_file(template_id)
        self._save_index()

        logger.info("Template deleted: id=%s", template_id)

    def import_template(self, file_path: str) -> dict[str, str]:
        """Import a template from a JSON file.

        Validates version field. Assigns a new ID to avoid collision.
        Returns {"id": ..., "name": ...}.

        Raises FileNotFoundError if the file does not exist.
        Raises ValueError if the JSON is invalid or version mismatches.
        """
        self._load_from_disk()

        path = Path(file_path)
        if not path.exists():
            raise FileNotFoundError(f"File not found: {file_path}")

        try:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)
        except (json.JSONDecodeError, OSError) as exc:
            raise ValueError(f"Invalid JSON: {exc}") from exc

        # Version check
        file_version = data.get("version", "")
        if file_version and file_version != VERSION:
            raise ValueError(
                f"Version mismatch: file={file_version}, expected={VERSION}"
            )

        try:
            tmpl = Template.from_dict(data)
        except Exception as exc:
            raise ValueError(f"Invalid template data: {exc}") from exc

        # Assign new ID to avoid collision
        tmpl.id = "tpl_" + uuid.uuid4().hex[:8]
        tmpl.create_time = self._now_iso()
        tmpl.update_time = self._now_iso()

        self._templates[tmpl.id] = tmpl
        self._save_template_file(tmpl)
        self._save_index()

        logger.info("Template imported: id=%s, name=%s, from=%s", tmpl.id, tmpl.name, file_path)
        return {"id": tmpl.id, "name": tmpl.name}

    def export_template(self, template_id: str, target_dir: str) -> str:
        """Export a template to a JSON file.

        Auto-increments suffix to avoid overwriting existing files.
        Returns the absolute path of the exported file.

        Raises KeyError if the template does not exist.
        Raises FileNotFoundError if the target directory does not exist.
        Raises OSError if writing fails.
        """
        self._load_from_disk()

        if template_id not in self._templates:
            raise KeyError(f"Template not found: {template_id}")

        target = Path(target_dir)
        if not target.is_dir():
            raise FileNotFoundError(f"Target directory not found: {target_dir}")

        tmpl = self._templates[template_id]
        file_name = f"{tmpl.name}.json"
        file_path = target / file_name

        # Avoid overwriting — add numeric suffix if file exists
        counter = 1
        while file_path.exists():
            file_path = target / f"{tmpl.name}({counter}).json"
            counter += 1

        with open(file_path, "w", encoding="utf-8") as f:
            f.write(tmpl.to_json(indent=2))

        exported_path = str(file_path.resolve())
        logger.info("Template exported: id=%s -> %s", template_id, exported_path)
        return exported_path

    def set_default(self, template_id: str) -> None:
        """Designate a template as the default.

        Raises KeyError if the template does not exist.
        """
        self._load_from_disk()

        if template_id not in self._templates:
            raise KeyError(f"Template not found: {template_id}")

        self._default_id = template_id
        self._save_index()

        logger.info("Default template set: id=%s", self._default_id)

    # ------------------------------------------------------------------
    # Private helpers
    # ------------------------------------------------------------------

    @staticmethod
    def _now_iso() -> str:
        return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    def _ensure_dir(self) -> None:
        _TEMPLATES_DIR.mkdir(parents=True, exist_ok=True)

    def _save_template_file(self, tmpl: Template) -> None:
        """Persist a single template to its JSON file."""
        try:
            self._ensure_dir()
            file_path = _TEMPLATES_DIR / f"{tmpl.id}.json"
            with open(file_path, "w", encoding="utf-8") as f:
                f.write(tmpl.to_json(indent=2))
        except OSError as exc:
            logger.error("Failed to write template %s: %s", tmpl.id, exc)

    def _delete_template_file(self, tmpl_id: str) -> None:
        """Remove a template's JSON file from disk."""
        try:
            file_path = _TEMPLATES_DIR / f"{tmpl_id}.json"
            if file_path.exists():
                file_path.unlink()
        except OSError as exc:
            logger.error("Failed to delete template %s: %s", tmpl_id, exc)

    def _save_index(self) -> None:
        """Persist the index (ordering + default ID) to disk."""
        try:
            self._ensure_dir()
            index = {
                "defaultId": self._default_id,
                "order": list(self._templates.keys()),
            }
            with open(_INDEX_FILE, "w", encoding="utf-8") as f:
                json.dump(index, f, ensure_ascii=False, indent=2)
                f.write("\n")
        except OSError as exc:
            logger.error("Failed to write index.json: %s", exc)

    def _seed_presets(self) -> None:
        """Create a single default template on first-ever launch only."""
        tmpl = Template(
            id="default",
            name="默认模板",
            version=VERSION,
            description="默认模板",
            create_time=self._now_iso(),
            update_time=self._now_iso(),
            profile=ProfileConfig(),
        )

        self._templates[tmpl.id] = tmpl
        self._default_id = tmpl.id

        self._save_template_file(tmpl)
        self._save_index()

        logger.info("Seeded default template")

    def _load_from_disk(self) -> None:
        """Load all templates from disk into memory.

        If the templates directory doesn't exist or is empty, seed presets.
        """
        if self._initialised:
            return

        self._ensure_dir()

        # Load index
        loaded_index = False
        if _INDEX_FILE.exists():
            try:
                with open(_INDEX_FILE, encoding="utf-8") as f:
                    index = json.load(f)
                self._default_id = index.get("defaultId", "")
                loaded_index = True
            except (json.JSONDecodeError, OSError) as exc:
                logger.warning("Failed to read index.json: %s", exc)

        # Load template files
        loaded = 0
        for json_file in sorted(_TEMPLATES_DIR.glob("*.json")):
            if json_file.name == "index.json":
                continue
            try:
                with open(json_file, encoding="utf-8") as f:
                    data = json.load(f)
                tmpl = Template.from_dict(data)
                self._templates[tmpl.id] = tmpl
                loaded += 1
            except Exception as exc:
                logger.warning("Failed to load template %s: %s", json_file.name, exc)

        # Seed presets if nothing was loaded
        if loaded == 0:
            self._seed_presets()
        else:
            # Ensure _default_id points to a valid template
            if self._default_id not in self._templates:
                self._default_id = next(iter(self._templates))
            logger.info("Loaded %d templates from disk (default=%s)", loaded, self._default_id)

        # Mark initialised ONLY after everything succeeds
        self._initialised = True


# Module-level singleton — the API layer imports this instance
template_service = TemplateService()
