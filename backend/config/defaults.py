"""
Word Formatter — Default settings values (Step 1.3)

All software-wide defaults are defined here, per data model.md §11.
Import ``DEFAULT_SETTINGS`` to seed new configs or fill missing keys.
"""

# Default settings matching data model.md §11 (camelCase keys, same as JSON).
DEFAULT_SETTINGS: dict = {
    "theme": "light",            # system / light / dark
    "language": "zh-CN",         # 界面语言
    "defaultOutput": "sameFolder",  # sameFolder 或 自定义路径
    "defaultTemplate": "Default",   # 默认模板 ID 或名称
    "recentCount": 20,           # 最近记录保留条数
    "autoCheckUpdate": True,     # 是否自动检查更新
}
