"""
排版引擎主控 — 编排 docx 格式化、doc→docx 转换、逐文件处理

Phase 2 重构：直接接受 shared.schemas.ProfileConfig，引擎内部转换为
格式化器使用的 data_model 配置对象。不再需要外部转换层。
"""

import os
import threading
from pathlib import Path
from typing import Optional, Tuple

try:
    from docx import Document
    HAS_DOCX = True
except ImportError:
    HAS_DOCX = False

try:
    import pythoncom
    import win32com.client
    HAS_COM = True
except ImportError:
    HAS_COM = False

from shared.schemas import ProfileConfig, HeadingStyleConfig as SharedHeading
from .data_model import (
    BodyConfig, DocumentGridConfig, FormatProfile,
    HeadingStyleConfig, PageConfig,
)
from .font import apply_run_font
from .data_model import HeaderFooterConfig as DmHeaderFooterConfig
from .page import apply_header_footer_font, apply_page_setup
from .paragraph import (
    ALIGNMENT, _apply_line_spacing, apply_first_line_indent,
    apply_hanging_indent, apply_paragraph_format,
)
from docx.enum.text import WD_ALIGN_PARAGRAPH
from .heading import apply_heading_style, HEADING_NAMES
from docx.oxml.ns import qn, nsdecls
from docx.oxml import parse_xml, OxmlElement
from .table import apply_table_format, _is_in_table_cell
from .image import apply_image_size, apply_image_wrapping, apply_image_alignment, compress_images
from .data_model import PictureConfig as DmPictureConfig, TableConfig as DmTableConfig


def _resolve_output_path(base_path: str) -> str:
    """如果输出文件已存在，自动追加序号避免覆盖。

    ``word-R.docx``（已存在） → ``word-R(1).docx`` → ``word-R(2).docx`` → …
    """
    path = Path(base_path)
    if not path.exists():
        return base_path

    stem = path.stem
    suffix = path.suffix
    parent = path.parent

    counter = 1
    while True:
        new_name = f"{stem}({counter}){suffix}"
        new_path = parent / new_name
        if not new_path.exists():
            return str(new_path)
        counter += 1


# ============================================================
# ProfileConfig → FormatProfile 转换（引擎内部）
# ============================================================

def _to_format_profile(profile: ProfileConfig,
                       output_dir: str = "") -> FormatProfile:
    """将共享 DTO ProfileConfig 转换为格式化器内部 FormatProfile。

    所有边距单位标准化为 mm，字号为 pt，行距为倍数浮点数。
    """
    pc = profile.page

    page = PageConfig(
        margin_top=pc.margin_top,
        margin_bottom=pc.margin_bottom,
        margin_left=pc.margin_left,
        margin_right=pc.margin_right,
        paper_size=pc.paper_size,
        orientation=pc.orientation,
        page_number=pc.page_number,
        header_distance=profile.header_footer.header_distance,
        footer_distance=profile.header_footer.footer_distance,
        custom_width=getattr(pc, "custom_width", 0.0),
        custom_height=getattr(pc, "custom_height", 0.0),
        document_grid=DocumentGridConfig(
            mode=getattr(pc.document_grid, "mode", "none"),
            lines_per_page=getattr(pc.document_grid, "lines_per_page", 32),
            chars_per_line=getattr(pc.document_grid, "chars_per_line", 40),
            adjust_right_indent=getattr(pc.document_grid, "adjust_right_indent", True),
            align_to_grid=getattr(pc.document_grid, "align_to_grid", True),
        ),
    )

    # ── Header / Footer ──
    hf = profile.header_footer
    header_footer = DmHeaderFooterConfig(
        font_cn=hf.font_cn,
        font_en=hf.font_en,
        font_size=hf.font_size,
        font_style=hf.font_style,
        alignment=hf.alignment,
    )

    bc = profile.body
    fs = bc.font_style
    body = BodyConfig(
        font_cn=bc.font_cn,
        font_en=bc.font_en,
        font_size=bc.font_size,
        font_bold=(fs == "bold"),
        font_italic=(fs == "italic"),
        alignment=bc.alignment,
        line_spacing=bc.line_spacing,
        line_spacing_mode=getattr(bc, "line_spacing_mode", "multiple"),
        indent_type=bc.indent_type,
        indent_value=bc.indent_value,
        indent_unit=bc.indent_unit,
        space_before=bc.space_before,
        space_after=bc.space_after,
        space_before_unit=getattr(bc, "space_before_unit", "行"),
        space_after_unit=getattr(bc, "space_after_unit", "行"),
    )

    headings: dict[int, HeadingStyleConfig] = {}
    for level_str, hd in profile.heading.items():
        level = int(level_str)
        hfs = hd.font_style
        headings[level] = HeadingStyleConfig(
            level=level,
            font_cn=hd.font_cn,
            font_en=hd.font_en,
            font_size=hd.font_size,
            font_color=getattr(hd, "font_color", "#000000"),
            font_bold=(hfs == "bold"),
            font_italic=(hfs == "italic"),
            alignment=hd.alignment,
            line_spacing=hd.line_spacing,
            line_spacing_mode=getattr(hd, "line_spacing_mode", "multiple"),
            indent_type=hd.indent_type,
            indent_value=hd.indent_value,
            indent_unit=hd.indent_unit,
            space_before=hd.space_before,
            space_after=hd.space_after,
            space_before_unit=getattr(hd, "space_before_unit", "pt"),
            space_after_unit=getattr(hd, "space_after_unit", "pt"),
        )

    # ── Picture ──
    pic = profile.picture
    picture = DmPictureConfig(
        size_mode=pic.size_mode,
        width=pic.width,
        width_unit=pic.width_unit,
        height=pic.height,
        height_unit=pic.height_unit,
        keep_ratio=pic.keep_ratio,
        no_enlarge=pic.no_enlarge,
        alignment=pic.alignment,
        wrapping_style=pic.wrapping_style,
        quality=pic.quality,
        max_side_pixels=pic.max_side_pixels,
        max_file_size=pic.max_file_size,
        auto_compress=pic.auto_compress,
    )

    # ── Table ──
    tbl = profile.table
    table = DmTableConfig(
        table_alignment=tbl.table_alignment,
        width_mode=tbl.width_mode,
        width_value=tbl.width_value,
        width_unit=tbl.width_unit,
        auto_fit_columns=tbl.auto_fit_columns,
        header_font_cn=tbl.header_font_cn,
        header_font_en=tbl.header_font_en,
        header_size=tbl.header_size,
        header_bold=tbl.header_bold,
        header_text_center=tbl.header_text_center,
        header_bg_color=tbl.header_bg_color,
        body_bg_color=tbl.body_bg_color,
        border_style=tbl.border_style,
        border_color=tbl.border_color,
        border_width=tbl.border_width,
        cell_align_h=tbl.cell_align_h,
        cell_align_v=tbl.cell_align_v,
        cell_margin_h=tbl.cell_margin_h,
        cell_margin_h_unit=tbl.cell_margin_h_unit,
        cell_margin_v=tbl.cell_margin_v,
        cell_margin_v_unit=tbl.cell_margin_v_unit,
        indent_type=tbl.indent_type,
        indent_value=tbl.indent_value,
        indent_unit=tbl.indent_unit,
        row_height_mode=tbl.row_height_mode,
        row_height=tbl.row_height,
        row_height_unit=tbl.row_height_unit,
        font_bold=tbl.font_bold,
        font_italic=tbl.font_italic,
        font_underline=tbl.font_underline,
        line_spacing=tbl.line_spacing,
        line_spacing_mode=getattr(tbl, "line_spacing_mode", "multiple"),
        line_spacing_unit=tbl.line_spacing_unit,
        auto_split=tbl.auto_split,
        repeat_header=tbl.repeat_header,
    )

    return FormatProfile(page=page, body=body, headings=headings,
                         picture=picture, table=table,
                         header_footer=header_footer, output_dir=output_dir)


# ============================================================
# 核心排版函数
# ============================================================

# 全局重入锁：串行化 COM 调用与输出文件命名。
#  - 多个线程同时驱动同一个 WPS/Word COM 实例会引发 RPC 失败；
#  - _resolve_output_path 的 check-then-write 存在竞态，两个并发任务处理
#    同一输入会选中同一个 "-R.docx" 输出名，后写者静默覆盖前者结果。
_ENGINE_LOCK = threading.RLock()


def format_docx(filepath: str, profile: ProfileConfig,
                output_path: Optional[str] = None,
                output_dir: str = "") -> Tuple[bool, str, str]:
    """排版单个 .docx 文件（加锁入口，逻辑见 _format_docx_inner）。"""
    with _ENGINE_LOCK:
        return _format_docx_inner(filepath, profile, output_path, output_dir)


def _format_docx_inner(filepath: str, profile: ProfileConfig,
                       output_path: Optional[str] = None,
                       output_dir: str = "") -> Tuple[bool, str, str]:
    """排版单个 .docx 文件。

    Args:
        filepath:    源文件路径
        profile:     排版配置（shared DTO）
        output_path: 指定输出路径（默认自动生成 -R 后缀）
        output_dir:  输出目录（与 output_path 二选一）

    Returns:
        (success, message, actual_output_path) 三元组。
        失败时 actual_output_path 为空字符串。
    """
    if output_path is None:
        p = Path(filepath)
        out_parent = Path(output_dir) if output_dir else p.parent
        out_parent.mkdir(parents=True, exist_ok=True)
        output_path = _resolve_output_path(str(out_parent / f"{p.stem}-R{p.suffix}"))

    fp = _to_format_profile(profile, output_dir)

    try:
        doc = Document(filepath)

        # 1. 页面设置（所有 section 统一）
        for section in doc.sections:
            apply_page_setup(section, fp.page, font_size_pt=fp.body.font_size)
            # 页眉页脚字体（必须在 page_setup 之后，因为页码是在 page_setup 中插入的）
            apply_header_footer_font(section, fp.header_footer)

        # 2. 正文样式（Normal 样式的段落格式：对齐 + 行距，不含字体）
        #     字体在 Step 4 的 run 级别应用，避免泄漏到表格单元格
        normal = doc.styles["Normal"]
        b = fp.body
        normal.paragraph_format.alignment = ALIGNMENT.get(
            b.alignment, WD_ALIGN_PARAGRAPH.JUSTIFY)
        # 文档网格启用时不设行距，让 docGrid 接管
        if fp.page.document_grid.mode == "none":
            _apply_line_spacing(normal.paragraph_format,
                                b.line_spacing, b.line_spacing_mode)

        # 3. 标题样式（1~6 级独立配置）
        heading_names = set(HEADING_NAMES.values())
        for level in range(1, 7):
            hd = fp.headings.get(level)
            if hd is None:
                continue
            apply_heading_style(doc, hd)

        # 4. 逐段落格式化（分类处理）
        for para in doc.paragraphs:
            style_name = para.style.name if para.style else ""

            # 标题段落 → 跳过（已由 Step 3 配置样式）
            if style_name in heading_names:
                continue

            # 表格单元格内段落 → 清零缩进（避免继承 Normal 样式缩进）
            if _is_in_table_cell(para):
                _zero_paragraph_indent(para)
                continue

            # 纯图片段落 → 无缩进，仅应用字体
            if _is_image_only_paragraph(para):
                _zero_paragraph_indent(para)
                for run in para.runs:
                    apply_run_font(run, b.font_cn, b.font_en, b.font_size,
                                   b.font_color, b.font_bold, b.font_italic)
                continue

            # 正文段落 → 完整段落格式（含缩进）
            # 文档网格启用时跳行距，让 docGrid 接管
            use_grid = fp.page.document_grid.mode != "none"
            apply_paragraph_format(para, b, skip_line_spacing=use_grid)
            if use_grid:
                _enable_snap_to_grid(para)
                # Remove empty/conflicting spacing — docGrid sets the pitch
                pPr = para._element.find(qn("w:pPr"))
                if pPr is not None:
                    sp = pPr.find(qn("w:spacing"))
                    if sp is not None:
                        pPr.remove(sp)
            for run in para.runs:
                apply_run_font(run, b.font_cn, b.font_en, b.font_size,
                               b.font_color, b.font_bold, b.font_italic)

        # 5. 表格格式化（所有表格统一应用配置）
        apply_table_format(doc, fp.table)

        # 6. 图片环绕样式设置（inline <-> anchor 转换 + wrap 元素）
        apply_image_wrapping(doc, fp.picture)

        # 7. 图片对齐设置（水平/垂直定位）
        apply_image_alignment(doc, fp.picture)

        # 8. 图片尺寸设置（根据 PictureConfig 调整显示尺寸）
        apply_image_size(doc, fp.picture)

        # 9. 图片压缩
        compress_images(doc, fp.picture)

        # 10. 保存
        doc.save(output_path)
        if not os.path.exists(output_path) or os.path.getsize(output_path) == 0:
            return False, "输出文件写入失败或为空", ""
        return True, f"排版成功: {os.path.basename(output_path)}", output_path

    except Exception as e:
        return False, f"排版失败 [{os.path.basename(filepath)}]: {e}", ""


def convert_doc_to_docx(filepath: str) -> Tuple[bool, str, Optional[str]]:
    """将 .doc 转换为 .docx（通过 Word/WPS COM，加锁串行化）"""
    with _ENGINE_LOCK:
        return _convert_doc_to_docx_inner(filepath)


def _convert_doc_to_docx_inner(filepath: str) -> Tuple[bool, str, Optional[str]]:
    """将 .doc 转换为 .docx（通过 Word/WPS COM）"""
    if not HAS_COM:
        return False, "缺少 pywin32 库，无法处理 .doc 文件。请安装: pip install pywin32", None

    pythoncom.CoInitialize()
    app = doc = temp_docx = None

    try:
        for pid in ("Word.Application", "WPS.Application",
                     "KWPS.Application", "ET.Application"):
            try:
                app = win32com.client.Dispatch(pid)
                break
            except Exception:
                continue

        if app is None:
            return False, "未找到可用的 Word 处理器（MS Word 或 WPS 均未安装）", None

        app.Visible = False
        app.DisplayAlerts = 0
        doc = app.Documents.Open(os.path.abspath(filepath), ReadOnly=True)
        temp_docx = filepath + ".converted.docx"
        doc.SaveAs2(os.path.abspath(temp_docx), FileFormat=12)
        doc.Close()
        return True, f"转换成功: {os.path.basename(filepath)}", temp_docx

    except Exception as e:
        return False, f"转换失败 [{os.path.basename(filepath)}]: {e}", None
    finally:
        if doc is not None:
            try:
                doc.Close(SaveChanges=0)
            except Exception:
                pass
        if app is not None:
            try:
                app.Quit()
            except Exception:
                pass
        pythoncom.CoUninitialize()


def _is_image_only_paragraph(paragraph) -> bool:
    """判断段落是否只包含图片（所有 run 含 drawing/pict 且无文本内容）。

    遍历段落每个 run 的 XML 元素，检测是否包含 drawing 或 pict 标签。
    仅当所有 run 都包含图片元素且段落无文本时才判定为纯图片段落。

    Returns:
        True 如果段落的每个 run 都包含图片元素且段落无文本。
    """
    if len(paragraph.runs) == 0:
        return False
    for run in paragraph.runs:
        found_image = False
        for elem in run._element.iter():
            tag = elem.tag.split("}")[-1] if "}" in elem.tag else elem.tag
            if tag in ("drawing", "pict"):
                found_image = True
                break
        if not found_image:
            return False
    # 所有 run 都是图片 → 确认无文本
    return paragraph.text.strip() == ""


def _enable_snap_to_grid(paragraph) -> None:
    """Add <w:snapToGrid/> to paragraph pPr so Word actually uses docGrid."""
    pPr = paragraph._element.get_or_add_pPr()
    if pPr.find(qn("w:snapToGrid")) is None:
        pPr.append(OxmlElement("w:snapToGrid"))


def _zero_paragraph_indent(paragraph) -> None:
    """在段落级别显式写入零值缩进，覆盖 Normal 样式继承的首行缩进。"""
    pPr = paragraph._element.get_or_add_pPr()
    for old in pPr.findall(qn("w:ind")):
        pPr.remove(old)
    ind = OxmlElement("w:ind")
    ind.set(qn("w:firstLine"), "0")
    ind.set(qn("w:firstLineChars"), "0")
    ind.set(qn("w:hanging"), "0")
    ind.set(qn("w:hangingChars"), "0")
    ind.set(qn("w:left"), "0")
    ind.set(qn("w:right"), "0")
    pPr.append(ind)


def process_file(filepath: str, profile: ProfileConfig,
                 output_dir: str = "") -> Tuple[bool, str, str]:
    """处理单个文件 — 根据扩展名分发。

    Args:
        filepath:  源文件路径
        profile:   排版配置（shared DTO，Phase 2 直接接受）
        output_dir: 输出目录（空则使用原文件目录）

    Returns:
        (success, message, actual_output_path) 三元组。
        失败时 actual_output_path 为空字符串。
    """
    ext = Path(filepath).suffix.lower()
    out_parent = Path(output_dir) if output_dir else Path(filepath).parent
    out_parent.mkdir(parents=True, exist_ok=True)

    if ext == ".docx":
        out_path = _resolve_output_path(str(out_parent / f"{Path(filepath).stem}-R{ext}"))
        return format_docx(filepath, profile, output_path=out_path)

    if ext == ".doc":
        ok, msg, docx_path = convert_doc_to_docx(filepath)
        if not ok or docx_path is None:
            return False, msg, ""

        out_path = _resolve_output_path(str(out_parent / f"{Path(filepath).stem}-R.docx"))
        ok2, msg2, out_path2 = format_docx(docx_path, profile, output_path=out_path)

        try:
            if os.path.exists(docx_path):
                os.remove(docx_path)
        except Exception:
            pass
        return ok2, msg2, out_path2

    return False, f"不支持的文件格式: {ext}", ""