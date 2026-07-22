import pytest

from shelfbound_decky import limits, vdf
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


def test_oversized_input_is_rejected():
    oversized = "x" * (limits.MAX_VDF_TEXT_CHARS + 1)

    with pytest.raises(VdfFormatError, match="character limit"):
        vdf.parse(oversized)


def test_pathological_nesting_is_rejected_before_python_recursion_limit():
    depth = limits.MAX_VDF_DEPTH + 1
    nested = '"k" { ' * depth + " }" * depth

    with pytest.raises(VdfFormatError, match="depth limit"):
        vdf.parse(nested)


def test_selects_only_the_requested_scalar_and_reports_matching_siblings():
    payload = """
        "UnrelatedRoot" { "UnrelatedScalar" "must-not-be-selected" }
        "UserLocalConfigStore"
        {
            "WebStorage"
            {
                "UnrelatedScalar" "must-not-be-selected"
                "PrivateApps_11" "[40]"
                "PrivateApps_10" "[20]"
            }
        }
    """

    selection = vdf.select_value(
        payload,
        ("UserLocalConfigStore", "WebStorage"),
        "PrivateApps_10",
        "PrivateApps_",
    )

    assert selection.value == "[20]"
    assert selection.has_matching_sibling
    assert "must-not-be-selected" not in repr(selection)


def test_selective_reader_enforces_depth_while_skipping_unrelated_subtrees():
    depth = limits.MAX_VDF_DEPTH + 1
    nested = '"unrelated" { ' * depth + " }" * depth

    with pytest.raises(VdfFormatError, match="depth limit"):
        vdf.select_value(
            nested,
            ("UserLocalConfigStore", "WebStorage"),
            "PrivateApps_10",
            "PrivateApps_",
        )
