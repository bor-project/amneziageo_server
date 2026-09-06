#!/usr/bin/env python3
"""Writes the C# view of the kernel module uapi header."""

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
HEADER = ROOT / "amneziawg" / "amneziawg-linux-kernel-module" / "src" / "uapi" / "wireguard.h"
TARGET = ROOT / "amneziageo-server" / "AmneziaGeo.Server.Awg" / "Uapi" / "WgUapi.cs"

NAMES = {
    "wg_cmd": ("WgCmd", "byte", False),
    "wgdevice_flag": ("WgDeviceFlag", "uint", True),
    "wgdevice_attribute": ("WgDeviceAttribute", "ushort", False),
    "wgpeer_flag": ("WgPeerFlag", "uint", True),
    "wgpeer_attribute": ("WgPeerAttribute", "ushort", False),
    "wgallowedip_flag": ("WgAllowedIpFlag", "uint", True),
    "wgallowedip_attribute": ("WgAllowedIpAttribute", "ushort", False),
}

DROP = ("WG_CMD_", "WGDEVICE_F_", "WGDEVICE_A_", "WGPEER_F_", "WGPEER_A_",
        "WGALLOWEDIP_F_", "WGALLOWEDIP_A_")


def member(raw):
    """Turns a SCREAMING_CASE uapi member into a C# name."""
    for prefix in DROP:
        if raw.startswith(prefix):
            raw = raw[len(prefix):]
            break
    parts = [p for p in raw.split("_") if p]
    out = []
    for p in parts:
        if p.isdigit():
            out.append(p)
        elif re.fullmatch(r"[A-Z]\d+", p):
            out.append(p[0] + p[1:])
        else:
            out.append(p.capitalize())
    name = "".join(out)
    return re.sub(r"Ip(addr|v4|v6)", lambda m: "Ip" + m.group(1).capitalize(), name)


def enums(text):
    """Reads every enum the map names, in header order."""
    found = {}
    for block in re.finditer(r"enum\s+(\w+)\s*\{(.*?)\};", text, re.S):
        key = block.group(1)
        if key not in NAMES:
            continue
        members, auto = [], 0
        body = re.sub(r"/\*.*?\*/", "", block.group(2), flags=re.S)
        for entry in body.split(","):
            entry = " ".join(entry.split())
            if not entry:
                continue
            if "=" in entry:
                raw, value = (p.strip() for p in entry.split("=", 1))
            else:
                raw, value = entry, str(auto)
            if raw.startswith("__"):
                continue
            value = re.sub(r"\b(\d+)U\b", r"\1", value)
            for other in members:
                value = re.sub(rf"\b{other[2]}\b", other[0], value)
            members.append((member(raw), value, raw))
            digits = re.fullmatch(r"\d+", value)
            auto = int(value) + 1 if digits else auto + 1
        found[key] = members
    return found


def main():
    text = HEADER.read_text(encoding="utf-8")

    name = re.search(r'#define\s+WG_GENL_NAME\s+"([^"]+)"', text).group(1)
    version = re.search(r"#define\s+WG_GENL_VERSION\s+(\d+)", text).group(1)
    key_len = re.search(r"#define\s+WG_KEY_LEN\s+(\d+)", text).group(1)

    sha = subprocess.run(
        ["git", "-C", str(HEADER.parent.parent.parent), "rev-parse", "--short", "HEAD"],
        capture_output=True, text=True, check=False).stdout.strip() or "unknown"

    found = enums(text)

    lines = [
        "// Written by tools/generate-uapi.py from the kernel module header. Do not edit.",
        f"// Source: amneziawg-linux-kernel-module {sha}, src/uapi/wireguard.h",
        "",
        "namespace AmneziaGeo.Server.Awg.Uapi;",
        "",
        "/// <summary>",
        "/// Names and sizes the kernel module publishes.",
        "/// </summary>",
        "public static class WgUapi",
        "{",
        f'    public const string FamilyName = "{name}";',
        "",
        f"    public const byte FamilyVersion = {version};",
        "",
        f"    public const int KeyLength = {key_len};",
        "}",
    ]

    for key, (cs, kind, flags) in NAMES.items():
        if key not in found:
            continue
        lines += ["", "/// <summary>", f"/// The {key} values of the module header.", "/// </summary>"]
        if flags:
            lines.append("[Flags]")
        lines.append(f"public enum {cs} : {kind}")
        lines.append("{")
        for csname, value, _ in found[key]:
            lines.append(f"    {csname} = {value},")
        lines.append("}")

    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"wrote {TARGET.relative_to(ROOT)} from {sha}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
