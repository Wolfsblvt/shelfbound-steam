from shelfbound_decky import storage
from shelfbound_decky.storage import (
    MountEntry,
    classify_library_path,
    classify_storage,
    find_mount,
    parse_proc_mounts,
)

# A realistic SteamOS mount table (trimmed): rootfs + /home on nvme, SD card on mmcblk.
DECK_MOUNTS = """\
/dev/nvme0n1p3 / btrfs rw 0 0
/dev/nvme0n1p8 /home ext4 rw 0 0
/dev/mmcblk0p1 /run/media/mmcblk0p1 ext4 rw 0 0
/dev/sda1 /run/media/deck/USB\\040Stick exfat rw 0 0
tmpfs /run tmpfs rw 0 0
"""


def mounts():
    return parse_proc_mounts(DECK_MOUNTS)


def test_parse_proc_mounts_decodes_octal_escapes():
    entries = mounts()
    usb = [entry for entry in entries if entry.device == "/dev/sda1"][0]
    assert usb.mount_point == "/run/media/deck/USB Stick"


def test_find_mount_picks_deepest_ancestor():
    entries = mounts()
    assert find_mount("/home/deck/.local/share/Steam", entries).device == "/dev/nvme0n1p8"
    assert find_mount("/etc", entries).device == "/dev/nvme0n1p3"
    assert find_mount("/run/media/mmcblk0p1/steamlib", entries).device == "/dev/mmcblk0p1"


def test_component_wise_prefix_no_false_match():
    entries = [MountEntry("/dev/nvme0n1p8", "/home/deck", "ext4")]
    assert find_mount("/home/deck2/Steam", entries) is None


def test_classification_by_backing_device():
    entries = mounts()
    home = "/home/deck"
    assert classify_library_path("/home/deck/.local/share/Steam", entries, home) == storage.INTERNAL
    assert classify_library_path("/run/media/mmcblk0p1/steamlib", entries, home) == storage.SD_CARD
    assert classify_library_path("/run/media/deck/USB Stick/lib", entries, home) == storage.EXTERNAL


def test_unrecognized_device_under_run_media_uses_path_heuristic():
    # e.g. a dm-crypt mapping: device name tells us nothing, mount location does.
    entries = [MountEntry("/dev/dm-0", "/run/media/deck/Card", "ext4")]
    assert classify_library_path("/run/media/deck/Card/lib", entries, "/home/deck") == storage.EXTERNAL
    entries = [MountEntry("/dev/dm-0", "/run/media/mmcblk0p1", "ext4")]
    assert classify_library_path("/run/media/mmcblk0p1/lib", entries, "/home/deck") == storage.SD_CARD


def test_fallbacks_without_mount_table():
    assert classify_library_path("/home/deck/.local/share/Steam", [], "/home/deck") == storage.INTERNAL
    assert classify_library_path("/run/media/mmcblk0p1/lib", [], "/home/deck") == storage.SD_CARD
    assert classify_library_path("/run/media/deck/Card/lib", [], "/home/deck") == storage.EXTERNAL
    assert classify_library_path("/mnt/somewhere", [], "/home/deck") == storage.UNKNOWN


def test_network_filesystem_classified_as_network():
    # fs_type wins over the device-name heuristic — a network share is `network`.
    entries = [MountEntry("//nas/games", "/mnt/nas", "cifs")]
    assert classify_library_path("/mnt/nas/steamlib", entries, "/home/deck") == storage.NETWORK


def test_classify_storage_emits_contract_dict_with_sizes():
    entries = mounts()
    result = classify_storage(
        "/run/media/mmcblk0p1/steamlib",
        entries,
        usage_fn=lambda _: (10_000, 50_000),
        home="/home/deck",
    )
    assert result == {"kind": "sdCard", "freeBytes": 10_000, "totalBytes": 50_000}


def test_classify_storage_omits_sizes_when_usage_unavailable():
    # os.statvfs is unavailable on Windows dev machines -> sizes omitted, never null.
    result = classify_storage(
        "/home/deck/.local/share/Steam", mounts(), usage_fn=lambda _: None, home="/home/deck"
    )
    assert result == {"kind": "internal"}
