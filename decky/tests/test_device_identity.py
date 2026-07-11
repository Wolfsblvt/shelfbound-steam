import re
import uuid

import pytest

from shelfbound_decky import device_identity

GUID_PATTERN = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")


@pytest.fixture(autouse=True)
def isolated_config(tmp_path, monkeypatch):
    # Point .NET-equivalent ApplicationData ($XDG_CONFIG_HOME) at a temp dir.
    monkeypatch.setenv("XDG_CONFIG_HOME", str(tmp_path))
    return tmp_path


def test_creates_and_persists_a_guid(isolated_config):
    first = device_identity.get_or_create_device_id()
    second = device_identity.get_or_create_device_id()

    assert GUID_PATTERN.match(first)
    assert first == second
    stored = (isolated_config / "Shelfbound" / "device-id").read_text(encoding="utf-8")
    assert stored.strip() == first


def test_reuses_the_cli_written_device_id(isolated_config):
    """The whole point: one device identity shared with the C# CLI/agent."""
    existing = str(uuid.uuid4())
    target = isolated_config / "Shelfbound"
    target.mkdir(parents=True)
    (target / "device-id").write_text(existing, encoding="utf-8")

    assert device_identity.get_or_create_device_id() == existing


def test_replaces_invalid_content(isolated_config):
    target = isolated_config / "Shelfbound"
    target.mkdir(parents=True)
    (target / "device-id").write_text("not-a-guid", encoding="utf-8")

    fresh = device_identity.get_or_create_device_id()
    assert GUID_PATTERN.match(fresh)


def test_resolve_device_shape():
    device = device_identity.resolve_device("Wolf's Deck", "steamDeck")

    assert device["name"] == "Wolf's Deck"
    assert device["type"] == "steamDeck"
    assert device["os"] in ("linux", "windows", "macOs", "unknown")
    assert GUID_PATTERN.match(device["id"])
    if "specs" in device:
        assert isinstance(device["specs"], dict) and device["specs"]


def test_resolve_device_uses_a_neutral_default_instead_of_the_hostname(monkeypatch):
    monkeypatch.setattr("socket.gethostname", lambda: "synthetic-private-host")

    device = device_identity.resolve_device()

    assert device["name"] == device_identity.DEFAULT_DEVICE_NAME
    assert device["name"] != "synthetic-private-host"
