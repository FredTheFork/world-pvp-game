#!/usr/bin/env python3
"""Explain a Unity Package Manager resolution failure, without opening the Editor.

Unity 6.3 can fail to resolve packages and then report only a Package Manager UI
crash such as:

    Failed to resolve packages: Cannot read properties of null (reading 'severity').
    [Package Manager Window] Error fetching package list offline.

That text is a symptom. The real cause (a pinned version that this Editor cannot
see, an unreachable registry, TLS interception, an offline Hub, a stale cache)
is written to the Editor log and to upm.log. This script reads this clone, finds
those logs, extracts the real error, probes the package registries, and prints
what to do next.

Usage:
    python3 Tools/diagnose_unity_packages.py
    python3 Tools/diagnose_unity_packages.py --log "C:/Users/me/AppData/Local/Unity/Editor/Editor.log"
    python3 Tools/diagnose_unity_packages.py --no-network
    python3 Tools/diagnose_unity_packages.py --use-cesium-tarball "C:/Users/me/Downloads/com.cesium.unity-1.26.0.tgz"
    python3 Tools/diagnose_unity_packages.py --restore

See Documentation/PACKAGE_RESOLUTION_TROUBLESHOOTING.md.
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import re
import shutil
import socket
import ssl
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXPECTED_EDITOR = "6000.3.24f1"
EXPECTED_CHANGESET = "4e7b9b5b6244"
CESIUM_PACKAGE = "com.cesium.unity"
CESIUM_TARBALL_NAME = "com.cesium.unity-1.26.0.tgz"
CESIUM_TARBALL_BYTES = 595_730_101
CESIUM_TARBALL_URL = (
    "https://github.com/CesiumGS/cesium-unity/releases/download/"
    "v1.26.0/com.cesium.unity-1.26.0.tgz"
)

REGISTRY_HOSTS = (
    "packages.unity.com",
    "download.packages.unity.com",
    "unity.pkg.cesium.com",
    "github.com",
    "objects.githubusercontent.com",
)

# log line -> (label, remedy)
LOG_SIGNATURES = (
    (re.compile(r"self[- ]signed certificate|unable to get local issuer certificate|"
                r"CERTIFICATE_VERIFY_FAILED|SSL certificate|\bSSL\b.*certificat", re.I),
     "TLS certificates are being intercepted or rejected",
     "A firewall/proxy is inspecting HTTPS. Allow-list the registry hosts (or install "
     "the proxy's root certificate) for: " + ", ".join(REGISTRY_HOSTS) + ". "
     "Cesium's own support forum documents this exact failure."),
    (re.compile(r"Project has invalid dependencies|invalid dependencies", re.I),
     "The resolver named specific package(s) it could not use",
     "Read the package lines printed with this error (this script prints them above). "
     "A named package/version either does not exist for this Editor or its registry "
     "was unreachable."),
    (re.compile(r"cannot be found", re.I),
     "A package/version in the manifest could not be found",
     "Confirm the Editor is exactly 6000.3.24f1 (unityhub://" + EXPECTED_EDITOR + "/" +
     EXPECTED_CHANGESET + "). Package versions are resolved against the Editor's "
     "compatible set; a newer/older Editor can hide a pinned version."),
    (re.compile(r"Cannot perform upm operation|Error fetching package list|offline", re.I),
     "Unity Package Manager went offline while fetching the package list",
     "Unity uses the operating system's connectivity report. Check: Hub signed in and "
     "online (not offline mode), Windows shows a working internet connection, no VPN/proxy "
     "blocking, then clear the caches below and reopen the project."),
    (re.compile(r"severity", re.I),
     "Package Manager UI crash while reporting the real error (cosmetic)",
     "The line above this one carries the real failure. Keep reading the log."),
    (re.compile(r"Failed to resolve packages", re.I),
     "The project opened with no packages loaded",
     "Every Unity.Netcode / CesiumForUnity / Unity.Mathematics / UnityEngine.InputSystem / "
     "Unity.Services.* compile error after this is a consequence, not a separate bug."),
)


def info(message: str) -> None:
    print(message)


def header(title: str) -> None:
    print()
    print(title)
    print("-" * len(title))


def candidate_log_paths() -> list[Path]:
    home = Path.home()
    system = platform.system()
    paths: list[Path] = []
    if system == "Windows":
        local = Path(os.environ.get("LOCALAPPDATA", home / "AppData/Local"))
        paths += [local / "Unity" / "Editor" / "Editor.log", local / "Unity" / "Editor" / "upm.log"]
    elif system == "Darwin":
        paths += [home / "Library/Logs/Unity/Editor.log", home / "Library/Logs/Unity/upm.log"]
    else:
        config = Path(os.environ.get("XDG_CONFIG_HOME", home / ".config"))
        paths += [config / "unity3d" / "Editor.log", config / "unity3d" / "upm.log"]
    return paths


def read_manifest() -> dict | None:
    path = ROOT / "Packages" / "manifest.json"
    if not path.is_file():
        info(f"MISSING: {path.relative_to(ROOT)}")
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        info(f"INVALID JSON in {path.relative_to(ROOT)}: {error}")
        return None


def report_project() -> None:
    header("This clone")
    pinned = (ROOT / "ProjectSettings" / "ProjectVersion.txt")
    if pinned.is_file():
        for line in pinned.read_text(encoding="utf-8").splitlines():
            if line.startswith("m_EditorVersion:"):
                info(f"Pinned Editor        : {line.split(':', 1)[1].strip()}")
    lock = ROOT / "Packages" / "packages-lock.json"
    info(f"packages-lock.json   : {'present' if lock.is_file() else 'MISSING (resolution never succeeded)'}")

    instance = ROOT / "Library" / "EditorInstance.json"
    if instance.is_file():
        try:
            data = json.loads(instance.read_text(encoding="utf-8"))
            version = data.get("version") or data.get("editorVersion") or "unknown"
            info(f"Last Editor to open  : {version}")
            if version != EXPECTED_EDITOR:
                info(f"  WARNING: this project is pinned to {EXPECTED_EDITOR}.")
                info(f"  Open it with {EXPECTED_EDITOR} only: unityhub://{EXPECTED_EDITOR}/{EXPECTED_CHANGESET}")
        except (ValueError, OSError):
            info("Last Editor to open  : unreadable Library/EditorInstance.json")
    else:
        info("Last Editor to open  : Library/ not present (project never imported here)")

    cache = ROOT / "Library" / "PackageCache"
    if cache.is_dir():
        entries = [p.name for p in cache.iterdir()]
        info(f"Library/PackageCache : {len(entries)} entr{'y' if len(entries) == 1 else 'ies'}")
        if not entries:
            info("  Empty. No package was ever resolved for this project.")
    manifest = read_manifest()
    if manifest:
        registries = [r.get("url", "?") for r in manifest.get("scopedRegistries", [])]
        info(f"Scoped registries    : {', '.join(registries) if registries else '(none)'}")
        cesium = manifest.get("dependencies", {}).get(CESIUM_PACKAGE)
        info(f"{CESIUM_PACKAGE:<20} : {cesium}")
        pinned_packages = manifest.get("pinnedPackages", [])
        if len(pinned_packages) != len(manifest.get("dependencies", {})):
            info("  NOTE: pinnedPackages does not cover every dependency; the Editor may "
                 "re-resolve some of them.")


def report_logs(explicit: Path | None) -> None:
    header("Editor / Package Manager logs")
    paths = [explicit] if explicit else candidate_log_paths()
    found = [p for p in paths if p and p.is_file()]
    if not found:
        info("No Editor log found. Expected one of:")
        for path in candidate_log_paths():
            info(f"  {path}")
        info("Open the project once (even in Safe Mode); the log is written on open.")
        return

    handled = set()
    for path in found:
        try:
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError as error:
            info(f"{path}: unreadable ({error})")
            continue
        info(f"{path}")
        lines = text.splitlines()
        interesting = []
        for index, line in enumerate(lines):
            for pattern, label, remedy in LOG_SIGNATURES:
                if pattern.search(line):
                    key = (label, remedy)
                    interesting.append((index, line.strip(), label, remedy, key))
                    break
        if not interesting:
            info("  No known resolution-failure signature in this log.")
            continue
        for index, line, label, remedy, key in interesting:
            if key in handled and not explicit:
                continue
            handled.add(key)
            info(f"  line {index + 1}: {line[:200]}")
            info(f"    => {label}")
            info(f"       {remedy}")
            if "invalid dependencies" in line.lower():
                for follow in lines[index + 1:index + 8]:
                    if follow.strip():
                        info(f"       {follow.strip()[:200]}")


def probe(host: str) -> tuple[str, str]:
    try:
        socket.getaddrinfo(host, 443, proto=socket.IPPROTO_TCP)
    except socket.gaierror as error:
        return "FAIL", f"DNS lookup failed ({error}). The machine cannot resolve this host."
    context = ssl.create_default_context()
    try:
        with socket.create_connection((host, 443), timeout=8) as raw:
            with context.wrap_socket(raw, server_hostname=host) as tls:
                peer = tls.getpeercert()
                issuer = dict(x[0] for x in peer.get("issuer", ()))
                tls.sendall(
                    f"GET /{CESIUM_PACKAGE} HTTP/1.1\r\nHost: {host}\r\n"
                    f"Connection: close\r\nUser-Agent: world-pvp-diagnose\r\n\r\n".encode()
                )
                status = tls.recv(64).decode("latin-1", "replace").splitlines()
    except ssl.SSLCertVerificationError as error:
        return "CERT", (f"TLS certificate rejected: {error.verify_message}. "
                        "This is the interception/firewall signature.")
    except (OSError, ssl.SSLError) as error:
        return "FAIL", f"Connection failed: {error}"
    line = status[0] if status else "(no response)"
    return "OK", f"{line} (issuer: {issuer.get('organizationName', 'unknown')})"


def report_network() -> None:
    header("Registry reachability")
    info("Unity Package Manager needs TCP 443 + valid TLS to these hosts:")
    for host in REGISTRY_HOSTS:
        state, detail = probe(host)
        marker = {"OK": "ok  ", "CERT": "CERT", "FAIL": "FAIL"}[state]
        info(f"  [{marker}] {host}: {detail}")
    info("")
    info("If a host fails here, fix the network path (firewall/proxy/VPN/DNS) before")
    info("touching the project. If unity.pkg.cesium.com alone fails, use the tarball")
    info("fallback below; the other twelve packages come from Unity's registry.")


def report_next_steps() -> None:
    header("Fix order")
    steps = (
        "1. Unity Hub: sign in (not offline mode) and confirm the account's email is verified.",
        f"2. Open the project only with Unity {EXPECTED_EDITOR} (changeset {EXPECTED_CHANGESET}):",
        f"   unityhub://{EXPECTED_EDITOR}/{EXPECTED_CHANGESET}",
        "3. Close the Editor, then clear the package caches and reopen:",
        f"   delete  {ROOT / 'Library' / 'PackageCache'}",
        f"   delete  {ROOT / 'Temp'}",
        "   delete  the Unity global cache folder (recreated on demand):",
        "           Windows %LOCALAPPDATA%\\Unity\\cache   macOS ~/Library/Caches/unity3d   Linux ~/.config/unity3d/cache",
        "4. If only unity.pkg.cesium.com is blocked, install Cesium from its GitHub tarball:",
        f"   download {CESIUM_TARBALL_URL}",
        f"   ({CESIUM_TARBALL_NAME} is about {CESIUM_TARBALL_BYTES / 1_000_000:.0f} MB)",
        f"   then: python3 Tools/diagnose_unity_packages.py --use-cesium-tarball <path to tgz>",
        "   (or remove the Cesium scoped registry by hand and use Package Manager ->",
        "    Install package from tarball). Revert with --restore once the registry works.",
        "5. Let Package Manager finish, confirm Packages/packages-lock.json appears, then commit it",
        "   together with the .meta files Unity generates. Never hand-write the lock file.",
        "6. Re-run Window -> General -> Test Runner -> EditMode -> Run All.",
    )
    for step in steps:
        info(step)


def use_cesium_tarball(tarball: Path) -> int:
    manifest_path = ROOT / "Packages" / "manifest.json"
    backup_path = ROOT / "Packages" / "manifest.json.bak"
    if not manifest_path.is_file():
        info("Packages/manifest.json not found.")
        return 2
    if not tarball.is_file():
        info(f"Tarball not found: {tarball}")
        info(f"Download it from {CESIUM_TARBALL_URL}")
        return 2
    size = tarball.stat().st_size
    if size != CESIUM_TARBALL_BYTES:
        info(f"WARNING: {tarball.name} is {size} bytes; the published 1.26.0 tarball is "
             f"{CESIUM_TARBALL_BYTES} bytes. A truncated download will fail to import.")
    if not backup_path.exists():
        shutil.copy2(manifest_path, backup_path)
        info(f"Backed up to {backup_path.relative_to(ROOT)}")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["scopedRegistries"] = [
        r for r in manifest.get("scopedRegistries", [])
        if CESIUM_PACKAGE not in r.get("scopes", [])
    ]
    try:
        relative = tarball.resolve().relative_to(ROOT.resolve())
        reference = f"file:{relative.as_posix()}"
    except ValueError:
        reference = f"file:{tarball.resolve().as_posix()}"
    manifest.setdefault("dependencies", {})[CESIUM_PACKAGE] = reference
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    info(f"{CESIUM_PACKAGE} now resolves from {reference} with no Cesium scoped registry.")
    info("Reopen the project, then run Tools/diagnose_unity_packages.py --restore when the")
    info("registry is reachable again so the manifest matches the pinned toolchain.")
    return 0


def restore_manifest() -> int:
    manifest_path = ROOT / "Packages" / "manifest.json"
    backup_path = ROOT / "Packages" / "manifest.json.bak"
    if not backup_path.is_file():
        info("No Packages/manifest.json.bak to restore from.")
        return 2
    shutil.copy2(backup_path, manifest_path)
    backup_path.unlink()
    info("Restored Packages/manifest.json from the backup.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--log", type=Path, default=None, help="path to Editor.log or upm.log")
    parser.add_argument("--no-network", action="store_true", help="skip registry reachability probes")
    parser.add_argument("--use-cesium-tarball", type=Path, default=None,
                        help="point com.cesium.unity at a downloaded GitHub tarball and drop the registry")
    parser.add_argument("--restore", action="store_true", help="restore Packages/manifest.json from its backup")
    args = parser.parse_args()

    if args.use_cesium_tarball:
        return use_cesium_tarball(args.use_cesium_tarball)
    if args.restore:
        return restore_manifest()

    info("World PvP - Unity package resolution diagnosis")
    report_project()
    report_logs(args.log)
    if not args.no_network:
        report_network()
    report_next_steps()
    return 0


if __name__ == "__main__":
    sys.exit(main())
