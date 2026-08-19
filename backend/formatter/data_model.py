"""
排版配置数据模型（Phase 2 重构版）

dataclass 用于格式化器内部持有数据（从 shared.schemas.ProfileConfig 转换而来）。
共享常量（FONT_SIZE_MAP / PAPER_SIZES 等）统一定义在 shared.constants。

Python 内部使用 snake_case（PEP 8），JSON 输出由 shared/schemas.py
的 Pydantic alias_generator 统一转 camelCase（Q1 决议）。
"""

from dataclasses import dataclass, field

# ============================================================
# 内部配置 dataclass（从 shared.schemas DTO 转换后使用）
# 所有值单位标准化：边距 mm，字号 pt，行距无单位（倍数模式）
# ============================================================

@dataclass
class DocumentGridConfig:
    """文档网格配置（Word 文档网格）

    Mode options:
      - "none"  : no grid (free layout)
      - "lines" : lines-only grid (fixed rows per page)
      - "both"  : lines + character grid (fixed rows and chars per line)
    """
    mode: str = "none"               # none / lines / both
    lines_per_page: int = 30
    chars_per_line: int = 35
    adjust_right_indent: bool = True
    align_to_grid: bool = True


@dataclass
class PageConfig:
    """页面设置 — 所有边距已标准化为 mm"""
    margin_top: float = 25.4
    margin_bottom: float = 25.4
    margin_left: float = 31.7
    margin_right: float = 31.7
    paper_size: str = "A4"
    orientation: str = "portrait"   # portrait / landscape
    page_number: bool = True
    header_distance: float = 15.0   # mm
    footer_distance: float = 17.5   # mm
    custom_width: float = 0.0       # mm, used when paper_size == "custom"
    custom_height: float = 0.0      # mm, used when paper_size == "custom"
    document_grid: DocumentGridConfig = field(default_factory=DocumentGridConfig)


@dataclass
class BodyConfig:
    """正文样式 + 段落格式（合并 body + paragraph 参数）"""
    font_cn: str = "宋体"
    font_en: str = "Times New Roman"
    font_size: float = 12.0
    font_color: str = "#000000"
    font_bold: bool = True
    font_italic: bool = False
    alignment: str = "justify"
    line_spacing: float = 1.5
    line_spacing_mode: str = "multiple"  # multiple / fixed / at_least
    indent_type: str = "firstLine"
    indent_value: float = 2.0
    indent_unit: str = "字符"
    space_before: float = 0.0
    space_after: float = 0.0
    space_before_unit: str = "行"       # pt / 行
    space_after_unit: str = "行"        # pt / 行


@dataclass
class HeadingStyleConfig:
    """单级标题样式（1~6 级独立配置）"""
    level: int = 1
    font_cn: str = "黑体"
    font_en: str = "Times New Roman"
    font_size: float = 16.0
    font_color: str = "#000000"
    font_bold: bool = True
    font_italic: bool = False
    alignment: str = "left"
    line_spacing: float = 1.5
    line_spacing_mode: str = "multiple"  # multiple / fixed / at_least / single
    indent_type: str = "none"
    indent_value: float = 0.0
    indent_unit: str = "字符"
    space_before: float = 1.0
    space_after: float = 1.0
    space_before_unit: str = "行"       # pt / 行
    space_after_unit: str = "行"        # pt / 行


@dataclass
class PictureConfig:
    """图片格式化配置（匹配 shared.schemas.PictureConfig）"""
    size_mode: str = "auto"        # width / height / auto
    width: float = 14.0
    width_unit: str = "cm"
    height: float = 8.0
    height_unit: str = "cm"
    keep_ratio: bool = True
    no_enlarge: bool = True
    alignment: str = "center"          # left / center / right / top / middle / bottom / distribute_h / distribute_v
    wrapping_style: str = "inline"     # inline / square / tight / through / topBottom / behindText / inFrontOfText
    quality: int = 85
    max_side_pixels: int = 1600
    max_file_size: int = 2 * 1024 * 1024  # 2MB
    auto_compress: bool = False


@dataclass
class TableConfig:
    """表格格式化配置（匹配 shared.schemas.TableConfig）"""
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
    header_bg_color: str = "none"
    body_bg_color: str = "none"

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

    # Special indent
    indent_type: str = "none"              # firstLine / none / hanging
    indent_value: float = 0.0
    indent_unit: str = "字符"

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
    line_spacing_unit: str = "pt"

    # Pagination
    auto_split: bool = True
    repeat_header: bool = False


@dataclass
class HeaderFooterConfig:
    """页眉页脚字体和对齐配置"""
    font_cn: str = "宋体"
    font_en: str = "Times New Roman"
    font_size: float = 10.5
    font_style: str = "normal"
    alignment: str = "center"


@dataclass
class FormatProfile:
    """完整排版配置 — 格式化器入口参数"""
    page: PageConfig = field(default_factory=PageConfig)
    body: BodyConfig = field(default_factory=BodyConfig)
    headings: dict = field(default_factory=dict)
    picture: PictureConfig = field(default_factory=PictureConfig)
    table: TableConfig = field(default_factory=TableConfig)
    header_footer: HeaderFooterConfig = field(default_factory=HeaderFooterConfig)
    output_dir: str = ""

    def __post_init__(self):
        if not self.headings:
            sizes = {1: 22, 2: 18, 3: 16, 4: 14, 5: 12, 6: 10.5}
            spacings = {1: (1.0, 1.0), 2: (1.0, 1.0), 3: (0.5, 0.5),
                        4: (0.5, 0.5), 5: (0.25, 0.25), 6: (0.25, 0.25)}
            for i in range(1, 7):
                h = HeadingStyleConfig(level=i)
                h.font_size = sizes[i]
                h.space_before, h.space_after = spacings[i]
                self.headings[i] = h