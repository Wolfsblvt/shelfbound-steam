"""Minimal parser for Valve's text VDF / KeyValues format.

Used by libraryfolders.vdf, appmanifest_*.acf, loginusers.vdf and friends. Handles
quoted tokens with escapes, nested braces, and // line comments. It does not evaluate
platform conditionals (e.g. [$WIN32]) or #include directives, which do not appear in
the files Shelfbound reads.

Mirrors the C# reference parser (src/Shelfbound.Steam/Vdf/): key lookups are
case-insensitive (matching Steam's behaviour) and the last value wins on duplicate
keys. Keep the two in sync if either grows.
"""

from __future__ import annotations

import os

from . import limits


class VdfFormatError(ValueError):
    """Raised when VDF text is structurally malformed."""


class VdfObject:
    """A parsed VDF object. Children are split into scalar values and nested objects.

    Lookups are case-insensitive; enumeration preserves insertion order (Steam files
    rely on it for e.g. account ordering) and duplicate keys keep their first position.
    """

    __slots__ = ("_values", "_objects")

    def __init__(self) -> None:
        # lowercased key -> (original key, value); insertion-ordered.
        self._values: dict[str, tuple[str, str]] = {}
        self._objects: dict[str, tuple[str, "VdfObject"]] = {}

    @property
    def values(self) -> dict[str, str]:
        """Scalar children as {original key: value}, in insertion order."""
        return {original: value for original, value in self._values.values()}

    @property
    def objects(self) -> dict[str, "VdfObject"]:
        """Nested children as {original key: object}, in insertion order."""
        return {original: obj for original, obj in self._objects.values()}

    def get_value(self, key: str) -> str | None:
        entry = self._values.get(key.lower())
        return entry[1] if entry else None

    def get_object(self, key: str) -> "VdfObject | None":
        entry = self._objects.get(key.lower())
        return entry[1] if entry else None

    def _set_value(self, key: str, value: str) -> None:
        lower = key.lower()
        existing = self._values.get(lower)
        self._values[lower] = (existing[0] if existing else key, value)

    def _set_object(self, key: str, value: "VdfObject") -> None:
        lower = key.lower()
        existing = self._objects.get(lower)
        self._objects[lower] = (existing[0] if existing else key, value)


_OPEN, _CLOSE, _STRING, _EOF = "{", "}", "string", "eof"


def parse(text: str) -> VdfObject:
    """Parse VDF text into a root object; raises VdfFormatError on malformed input."""
    if len(text) > limits.MAX_VDF_TEXT_CHARS:
        raise VdfFormatError(
            f"VDF input exceeds the {limits.MAX_VDF_TEXT_CHARS}-character limit."
        )
    lexer = _Lexer(text)
    root = VdfObject()
    while True:
        kind, token, position = lexer.next()
        if kind == _EOF:
            return root
        if kind != _STRING:
            raise VdfFormatError(f"Unexpected token '{token}' at top level (position {position}).")
        _read_key_value(lexer, root, token, depth=0)


def parse_file(path: str) -> VdfObject:
    # utf-8-sig strips a BOM if present; errors are replaced rather than fatal,
    # matching the lenient way the C# core reads these files.
    if os.path.getsize(path) > limits.MAX_VDF_FILE_BYTES:
        raise VdfFormatError(f"VDF file exceeds the {limits.MAX_VDF_FILE_BYTES}-byte limit.")
    with open(path, "r", encoding="utf-8-sig", errors="replace") as handle:
        return parse(handle.read())


def _read_key_value(lexer: "_Lexer", target: VdfObject, key: str, depth: int) -> None:
    kind, token, position = lexer.next()
    if kind == _OPEN:
        child_depth = depth + 1
        if child_depth > limits.MAX_VDF_DEPTH:
            raise VdfFormatError(f"VDF nesting exceeds the depth limit of {limits.MAX_VDF_DEPTH}.")
        target._set_object(key, _read_object(lexer, child_depth))
    elif kind == _STRING:
        target._set_value(key, token)
    else:
        raise VdfFormatError(f"Expected a value or '{{' after key '{key}' (position {position}).")


def _read_object(lexer: "_Lexer", depth: int) -> VdfObject:
    obj = VdfObject()
    while True:
        kind, token, position = lexer.next()
        if kind == _CLOSE:
            return obj
        if kind == _EOF:
            raise VdfFormatError("Unexpected end of input inside object (missing '}').")
        if kind != _STRING:
            raise VdfFormatError(f"Expected a key or '}}' (position {position}).")
        _read_key_value(lexer, obj, token, depth)


_ESCAPES = {"n": "\n", "t": "\t", "r": "\r", "\\": "\\", '"': '"'}


class _Lexer:
    def __init__(self, source: str) -> None:
        self._s = source
        self._i = 0

    def next(self) -> tuple[str, str, int]:
        """Returns (kind, text, position)."""
        self._skip_trivia()
        if self._i >= len(self._s):
            return (_EOF, "", self._i)

        char = self._s[self._i]
        if char == "{":
            position = self._i
            self._i += 1
            return (_OPEN, "{", position)
        if char == "}":
            position = self._i
            self._i += 1
            return (_CLOSE, "}", position)
        if char == '"':
            return self._read_quoted()
        return self._read_unquoted()

    def _skip_trivia(self) -> None:
        s = self._s
        while self._i < len(s):
            char = s[self._i]
            if char.isspace():
                self._i += 1
            elif char == "/" and self._i + 1 < len(s) and s[self._i + 1] == "/":
                while self._i < len(s) and s[self._i] != "\n":
                    self._i += 1
            else:
                break

    def _read_quoted(self) -> tuple[str, str, int]:
        s = self._s
        start = self._i
        self._i += 1  # consume opening quote
        parts: list[str] = []
        while self._i < len(s):
            char = s[self._i]
            self._i += 1
            if char == '"':
                return (_STRING, "".join(parts), start)
            if char == "\\" and self._i < len(s):
                escaped = s[self._i]
                self._i += 1
                parts.append(_ESCAPES.get(escaped, escaped))
            else:
                parts.append(char)
        raise VdfFormatError(f"Unterminated quoted string starting at position {start}.")

    def _read_unquoted(self) -> tuple[str, str, int]:
        s = self._s
        start = self._i
        while self._i < len(s):
            char = s[self._i]
            if char.isspace() or char in '{}"':
                break
            self._i += 1
        return (_STRING, s[start:self._i], start)
