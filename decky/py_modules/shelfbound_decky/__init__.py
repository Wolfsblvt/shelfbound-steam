"""Shelfbound Decky plugin backend (prototype).

A second *producer* of the standard Shelfbound snapshot contract — the same versioned
JSON the CLI/tray emit (see ../../schema/snapshot.v0.schema.json and
docs/project/snapshot-schema.md). This package deliberately mirrors the C# reference
implementation in src/Shelfbound.Steam so both producers read Steam files and shape
snapshots identically. It never invents contract fields; Deck-only intelligence
(internal SSD vs microSD, free space) stays UI-only and is never uploaded.

Pure stdlib — no pip dependencies at runtime.
"""

TOOL_NAME = "shelfbound-decky"
TOOL_VERSION = "0.1.0"

# Single source of truth in code is SnapshotSchema.Version (C#); keep this constant in
# lockstep when the contract bumps.
SCHEMA_VERSION = "0.4.0"
