"""Resolves the local device identity for a snapshot.

The device id is a random GUID persisted under the user's config directory — the SAME
file the C# CLI/agent uses (~/.config/Shelfbound/device-id), so a Deck that has synced
from desktop mode and from Gaming Mode stays ONE device. Never derived from hardware
or account data. The display-name fallback is neutral, never the hostname; see
docs/project/privacy-and-data.md.
"""

from __future__ import annotations

import os
import sys
import uuid
from pathlib import Path

from .private_file import write_private_text

# Mirrors .NET's Environment.SpecialFolder.ApplicationData resolution on Linux
# ($XDG_CONFIG_HOME, else ~/.config) and the C# DeviceIdentity/ShelfboundPaths constants.
SHELFBOUND_CONFIG_DIRNAME = "Shelfbound"
DEVICE_ID_FILENAME = "device-id"
DEFAULT_DEVICE_NAME = "Shelfbound device"


def shelfbound_config_dir() -> Path:
    xdg = os.environ.get("XDG_CONFIG_HOME")
    base = Path(xdg) if xdg and xdg.strip() else Path.home() / ".config"
    return base / SHELFBOUND_CONFIG_DIRNAME


def get_or_create_device_id() -> str:
    """Reads the shared persisted device id, creating it if missing (CLI-compatible)."""
    file = shelfbound_config_dir() / DEVICE_ID_FILENAME
    try:
        if file.is_file():
            existing = file.read_text(encoding="utf-8-sig").strip()
            if _is_guid(existing):
                return existing

        new_id = str(uuid.uuid4())
        write_private_text(file, new_id)
        return new_id
    except OSError:
        # If we cannot persist, fall back to an ephemeral id rather than failing the scan.
        return str(uuid.uuid4())


def _is_guid(text: str) -> bool:
    try:
        uuid.UUID(text)
        return True
    except ValueError:
        return False


def detect_device_type() -> str:
    """Best-effort: Steam Deck runs SteamOS (Linux) under a 'deck' user."""
    if sys.platform.startswith("linux") and (
        Path("/home/deck").is_dir() or os.environ.get("USER") == "deck"
    ):
        return "steamDeck"
    return "unknown"


def detect_os() -> str:
    if sys.platform.startswith("linux"):
        return "linux"
    if sys.platform == "win32":
        return "windows"
    if sys.platform == "darwin":
        return "macOs"
    return "unknown"


def resolve_device(name_override: str | None = None, type_override: str | None = None) -> dict:
    """Builds the snapshot `device` object (contract field order, nulls omitted)."""
    device: dict = {
        "id": get_or_create_device_id(),
        "name": name_override.strip() if name_override and name_override.strip() else DEFAULT_DEVICE_NAME,
        "type": type_override or detect_device_type(),
        "os": detect_os(),
    }
    specs = collect_specs()
    if specs:
        device["specs"] = specs
    return device


def collect_specs() -> dict | None:
    """Best-effort hardware facts; every read is defensive and failure just omits the field.

    GPU is deliberately not collected: the C# core uses the Hardware.Info library for
    it, and a dependency-free Linux equivalent (mapping PCI ids to names) isn't worth
    hand-rolling for a prototype. [NEEDS-DECK] verify which fields actually populate
    on real SteamOS.
    """
    specs: dict = {}

    cpu = _cpu_model()
    if cpu:
        specs["cpu"] = cpu

    cores = os.cpu_count()
    if cores:
        specs["logicalCores"] = cores

    memory = _total_memory_bytes()
    if memory:
        specs["totalMemoryBytes"] = memory

    os_description = _os_description()
    if os_description:
        specs["osDescription"] = os_description

    architecture = _architecture()
    if architecture:
        specs["architecture"] = architecture

    return specs or None


def _cpu_model() -> str | None:
    try:
        with open("/proc/cpuinfo", "r", encoding="utf-8", errors="replace") as handle:
            for line in handle:
                if line.lower().startswith("model name"):
                    return line.split(":", 1)[1].strip() or None
    except OSError:
        pass
    return None


def _total_memory_bytes() -> int | None:
    try:
        with open("/proc/meminfo", "r", encoding="utf-8", errors="replace") as handle:
            for line in handle:
                if line.startswith("MemTotal:"):
                    kilobytes = int(line.split()[1])
                    return kilobytes * 1024
    except (OSError, ValueError, IndexError):
        pass
    return None


def _os_description() -> str | None:
    # Mirrors .NET RuntimeInformation.OSDescription on Linux (kernel identification),
    # not /etc/os-release — keeps the two producers' snapshots comparable.
    try:
        uname = os.uname()
        return f"{uname.sysname} {uname.release} {uname.version}".strip() or None
    except (AttributeError, OSError):
        return None  # os.uname() does not exist on Windows (dev machines running tests)


_ARCHITECTURES = {
    "x86_64": "X64",
    "amd64": "X64",
    "aarch64": "Arm64",
    "arm64": "Arm64",
    "i386": "X86",
    "i686": "X86",
    "armv7l": "Arm",
}


def _architecture() -> str | None:
    import platform

    machine = platform.machine()
    if not machine:
        return None
    # Map to .NET's Architecture enum names so both producers speak the same vocabulary.
    return _ARCHITECTURES.get(machine.lower(), machine)
