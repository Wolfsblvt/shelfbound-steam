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
from dataclasses import dataclass
from typing import Protocol, TextIO

from . import limits


class VdfFormatError(ValueError):
    """Raised when VDF text is structurally malformed."""


@dataclass(frozen=True)
class VdfScalarSelection:
    """One selected scalar plus direct sibling-prefix evidence."""

    value: str | None
    has_matching_sibling: bool


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


def select_value(
    text: str,
    object_path: tuple[str, ...],
    value_key: str,
    sibling_prefix: str,
) -> VdfScalarSelection:
    """Select one scalar without retaining unrelated scalar values."""
    if len(text) > limits.MAX_VDF_TEXT_CHARS:
        raise VdfFormatError(
            f"VDF input exceeds the {limits.MAX_VDF_TEXT_CHARS}-character limit."
        )
    return _select_value(_Lexer(text), object_path, value_key, sibling_prefix)


def select_file_value(
    path: str,
    object_path: tuple[str, ...],
    value_key: str,
    sibling_prefix: str,
) -> VdfScalarSelection:
    """Stream-select one scalar from a bounded VDF file."""
    with open(path, "r", encoding="utf-8-sig", errors="replace") as handle:
        if os.fstat(handle.fileno()).st_size > limits.MAX_VDF_FILE_BYTES:
            raise VdfFormatError(
                f"VDF file exceeds the {limits.MAX_VDF_FILE_BYTES}-byte limit."
            )
        return _select_value(_StreamLexer(handle), object_path, value_key, sibling_prefix)


@dataclass
class _MutableSelection:
    value: str | None = None
    has_matching_sibling: bool = False


def _select_value(
    lexer: "_TokenReader",
    object_path: tuple[str, ...],
    value_key: str,
    sibling_prefix: str,
) -> VdfScalarSelection:
    if not object_path or any(not segment.strip() for segment in object_path):
        raise ValueError("The selected VDF object path must contain only non-empty segments.")
    if not value_key.strip() or not sibling_prefix.strip():
        raise ValueError("The selected VDF value key and sibling prefix must be non-empty.")

    selection: VdfScalarSelection | None = None
    while True:
        kind, token, position = lexer.next()
        if kind == _EOF:
            return selection or VdfScalarSelection(None, False)
        if kind != _STRING:
            raise VdfFormatError(f"Unexpected token '{token}' at top level (position {position}).")

        candidate = _read_selected_key_value(
            lexer,
            token,
            depth=0,
            path_index=0,
            object_path=object_path,
            value_key=value_key,
            sibling_prefix=sibling_prefix,
            target_selection=None,
        )
        if candidate is not None:
            selection = candidate


def _read_selected_key_value(
    lexer: "_TokenReader",
    key: str,
    depth: int,
    path_index: int,
    object_path: tuple[str, ...],
    value_key: str,
    sibling_prefix: str,
    target_selection: _MutableSelection | None,
) -> VdfScalarSelection | None:
    in_target_object = path_index == len(object_path)
    is_selected_key = in_target_object and key.casefold() == value_key.casefold()
    is_matching_sibling = (
        in_target_object
        and not is_selected_key
        and key.casefold().startswith(sibling_prefix.casefold())
    )
    kind, token, position = lexer.next(is_selected_key)

    if kind == _OPEN:
        child_depth = _child_depth(depth)
        if (
            not in_target_object
            and key.casefold() == object_path[path_index].casefold()
        ):
            return _read_selected_object(
                lexer,
                child_depth,
                path_index + 1,
                object_path,
                value_key,
                sibling_prefix,
            )
        _skip_object(lexer, child_depth)
        return None
    if kind == _STRING:
        if is_selected_key:
            assert target_selection is not None
            target_selection.value = token
        elif is_matching_sibling:
            assert target_selection is not None
            target_selection.has_matching_sibling = True
        return None
    raise VdfFormatError(f"Expected a value or '{{' after a key (position {position}).")


def _read_selected_object(
    lexer: "_TokenReader",
    depth: int,
    path_index: int,
    object_path: tuple[str, ...],
    value_key: str,
    sibling_prefix: str,
) -> VdfScalarSelection:
    target_selection = _MutableSelection() if path_index == len(object_path) else None
    nested_selection: VdfScalarSelection | None = None

    while True:
        kind, token, position = lexer.next()
        if kind == _CLOSE:
            if target_selection is not None:
                return VdfScalarSelection(
                    target_selection.value,
                    target_selection.has_matching_sibling,
                )
            return nested_selection or VdfScalarSelection(None, False)
        if kind == _EOF:
            raise VdfFormatError("Unexpected end of input inside object (missing '}').")
        if kind != _STRING:
            raise VdfFormatError(f"Expected a key or '}}' (position {position}).")

        candidate = _read_selected_key_value(
            lexer,
            token,
            depth,
            path_index,
            object_path,
            value_key,
            sibling_prefix,
            target_selection,
        )
        if candidate is not None:
            nested_selection = candidate


def _skip_object(lexer: "_TokenReader", depth: int) -> None:
    while True:
        kind, _token, position = lexer.next(False)
        if kind == _CLOSE:
            return
        if kind == _EOF:
            raise VdfFormatError("Unexpected end of input inside object (missing '}').")
        if kind != _STRING:
            raise VdfFormatError(f"Expected a key or '}}' (position {position}).")

        kind, _token, position = lexer.next(False)
        if kind == _OPEN:
            _skip_object(lexer, _child_depth(depth))
        elif kind != _STRING:
            raise VdfFormatError(f"Expected a value or '{{' after a key (position {position}).")


def _child_depth(depth: int) -> int:
    child_depth = depth + 1
    if child_depth > limits.MAX_VDF_DEPTH:
        raise VdfFormatError(f"VDF nesting exceeds the depth limit of {limits.MAX_VDF_DEPTH}.")
    return child_depth


def _read_key_value(lexer: "_TokenReader", target: VdfObject, key: str, depth: int) -> None:
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


def _read_object(lexer: "_TokenReader", depth: int) -> VdfObject:
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


class _TokenReader(Protocol):
    def next(self, capture_text: bool = True) -> tuple[str, str, int]: ...


class _Lexer:
    def __init__(self, source: str) -> None:
        self._source = source
        self._position = 0

    def next(self, capture_text: bool = True) -> tuple[str, str, int]:
        """Returns (kind, text, position)."""
        self._skip_trivia()
        if self._position >= len(self._source):
            return (_EOF, "", self._position)

        char = self._source[self._position]
        if char == "{":
            position = self._position
            self._position += 1
            return (_OPEN, "{", position)
        if char == "}":
            position = self._position
            self._position += 1
            return (_CLOSE, "}", position)
        if char == '"':
            return self._read_quoted(capture_text)
        return self._read_unquoted(capture_text)

    def _skip_trivia(self) -> None:
        source = self._source
        while self._position < len(source):
            char = source[self._position]
            if char.isspace():
                self._position += 1
            elif (
                char == "/"
                and self._position + 1 < len(source)
                and source[self._position + 1] == "/"
            ):
                while self._position < len(source) and source[self._position] != "\n":
                    self._position += 1
            else:
                break

    def _read_quoted(self, capture_text: bool) -> tuple[str, str, int]:
        source = self._source
        start = self._position
        self._position += 1  # consume opening quote
        parts: list[str] | None = [] if capture_text else None
        while self._position < len(source):
            char = source[self._position]
            self._position += 1
            if char == '"':
                return (_STRING, "".join(parts) if parts is not None else "", start)
            if char == "\\" and self._position < len(source):
                escaped = source[self._position]
                self._position += 1
                if parts is not None:
                    parts.append(_ESCAPES.get(escaped, escaped))
            elif parts is not None:
                parts.append(char)
        raise VdfFormatError(f"Unterminated quoted string starting at position {start}.")

    def _read_unquoted(self, capture_text: bool) -> tuple[str, str, int]:
        source = self._source
        start = self._position
        while self._position < len(source):
            char = source[self._position]
            if char.isspace() or char in '{}"':
                break
            self._position += 1
        return (
            _STRING,
            source[start:self._position] if capture_text else "",
            start,
        )


class _StreamLexer:
    """Bounded streaming lexer that can discard scalar text while preserving structure."""

    def __init__(self, source: TextIO) -> None:
        self._source = source
        self._lookahead: list[str] = []
        self._position = 0

    def next(self, capture_text: bool = True) -> tuple[str, str, int]:
        self._skip_trivia()
        char = self._peek()
        if not char:
            return (_EOF, "", self._position)

        if char == "{":
            position = self._position
            self._read_char()
            return (_OPEN, "{", position)
        if char == "}":
            position = self._position
            self._read_char()
            return (_CLOSE, "}", position)
        if char == '"':
            return self._read_quoted(capture_text)
        return self._read_unquoted(capture_text)

    def _skip_trivia(self) -> None:
        while True:
            char = self._peek()
            if not char:
                return
            if char.isspace():
                self._read_char()
            elif char == "/" and self._peek(1) == "/":
                while self._peek() not in ("", "\n"):
                    self._read_char()
            else:
                return

    def _read_quoted(self, capture_text: bool) -> tuple[str, str, int]:
        start = self._position
        self._read_char()
        parts: list[str] | None = [] if capture_text else None
        while self._peek():
            char = self._read_char()
            if char == '"':
                return (_STRING, "".join(parts) if parts is not None else "", start)
            if char == "\\" and self._peek():
                escaped = self._read_char()
                if parts is not None:
                    parts.append(_ESCAPES.get(escaped, escaped))
            elif parts is not None:
                parts.append(char)
        raise VdfFormatError(f"Unterminated quoted string starting at position {start}.")

    def _read_unquoted(self, capture_text: bool) -> tuple[str, str, int]:
        start = self._position
        parts: list[str] | None = [] if capture_text else None
        while self._peek():
            char = self._peek()
            if char.isspace() or char in '{}"':
                break
            consumed = self._read_char()
            if parts is not None:
                parts.append(consumed)
        return (_STRING, "".join(parts) if parts is not None else "", start)

    def _peek(self, offset: int = 0) -> str:
        while len(self._lookahead) <= offset:
            char = self._source.read(1)
            self._lookahead.append(char)
            if not char:
                break
        return self._lookahead[offset] if offset < len(self._lookahead) else ""

    def _read_char(self) -> str:
        char = self._peek()
        if not char:
            raise RuntimeError("Cannot read past the end of the VDF input.")
        if self._position >= limits.MAX_VDF_TEXT_CHARS:
            raise VdfFormatError(
                f"VDF input exceeds the {limits.MAX_VDF_TEXT_CHARS}-character limit."
            )
        self._lookahead.pop(0)
        self._position += 1
        return char
