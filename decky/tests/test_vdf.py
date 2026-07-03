import pytest

from shelfbound_decky import vdf
from shelfbound_decky.vdf import VdfFormatError


def test_parses_nested_objects_and_values():
    root = vdf.parse('''"outer"
{
    "key"   "value"
    "inner"
    {
        "a" "1"
    }
}
''')
    outer = root.get_object("outer")
    assert outer is not None
    assert outer.get_value("key") == "value"
    assert outer.get_object("inner").get_value("a") == "1"


def test_key_lookup_is_case_insensitive():
    root = vdf.parse('"Root" { "MostRecent" "1" }')
    obj = root.get_object("root")
    assert obj.get_value("mostrecent") == "1"
    assert obj.get_value("MOSTRECENT") == "1"


def test_last_value_wins_on_duplicate_keys():
    root = vdf.parse('"r" { "k" "first" "K" "second" }')
    assert root.get_object("r").get_value("k") == "second"
    # Duplicate keeps its first position and original spelling in enumeration.
    assert list(root.get_object("r").values.keys()) == ["k"]


def test_enumeration_preserves_insertion_order():
    root = vdf.parse('"users" { "b" { } "a" { } "c" { } }')
    assert list(root.get_object("users").objects.keys()) == ["b", "a", "c"]


def test_escapes_in_quoted_strings():
    root = vdf.parse(r'"r" { "k" "line1\nline2\ttab \"quoted\" back\\slash" }')
    assert root.get_object("r").get_value("k") == 'line1\nline2\ttab "quoted" back\\slash'


def test_line_comments_are_skipped():
    root = vdf.parse('// header comment\n"r" // trailing\n{\n// inner\n"k" "v"\n}')
    assert root.get_object("r").get_value("k") == "v"


def test_unquoted_tokens():
    root = vdf.parse('r { k v }')
    assert root.get_object("r").get_value("k") == "v"


def test_unterminated_string_raises():
    with pytest.raises(VdfFormatError, match="Unterminated quoted string"):
        vdf.parse('"r" { "k" "unclosed }')


def test_missing_close_brace_raises():
    with pytest.raises(VdfFormatError, match="missing '}'"):
        vdf.parse('"r" { "k" "v"')


def test_value_where_key_expected_raises():
    with pytest.raises(VdfFormatError, match="Expected a value"):
        vdf.parse('"r" }')
