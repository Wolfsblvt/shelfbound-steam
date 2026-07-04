"""Test setup for importing Decky backend modules off-loader."""

import importlib
import logging
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

DECKY_ROOT = Path(__file__).resolve().parent.parent
for path in (DECKY_ROOT, DECKY_ROOT / "py_modules"):
    sys.path.insert(0, str(path))


@pytest.fixture()
def decky_plugin_module(tmp_path, monkeypatch):
    decky_stub = SimpleNamespace(
        DECKY_PLUGIN_SETTINGS_DIR=str(tmp_path),
        DECKY_PLUGIN_VERSION="0.1.0-test",
        logger=logging.getLogger("shelfbound-decky-test"),
    )
    monkeypatch.setitem(sys.modules, "decky", decky_stub)
    sys.modules.pop("main", None)

    module = importlib.import_module("main")
    yield module

    sys.modules.pop("main", None)
