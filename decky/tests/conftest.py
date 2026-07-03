"""Test setup: put py_modules on sys.path (mirrors what Decky Loader does at runtime)."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "py_modules"))
