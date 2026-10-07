#!/usr/bin/env python3
"""Report whether this clone is ready for Unity, without opening the Editor."""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOCK = ROOT / "Packages" / "packages-lock.json"
WRONG_NAMES = (
    ROOT / "packages.lock",
    ROOT / "Packages" / "packages.lock",
    ROOT / "Packages" / "packages-lock",
)


def git(*args: str) -> str:
    try:
        completed = subprocess.run(
            ["git", *args],
            cwd=ROOT,
            check=False,
            capture_output=True,
            text=True,
        )
    except OSError as error:
        return f"(git unavailable: {error})"
    text = (completed.stdout or completed.stderr or "").strip()
    return text or f"(git exited {completed.returncode} with no output)"


def main() -> int:
    print(f"Project root: {ROOT}")
    required = ("Assets", "Packages/manifest.json", "ProjectSettings/ProjectVersion.txt")
    missing = [relative for relative in required if not (ROOT / relative).exists()]
    if missing:
        print("This folder is not the Unity project root. Missing:")
        for relative in missing:
            print(f"  {relative}")
        print("Open the directory that contains Assets, Packages, and ProjectSettings. Do not open Assets itself.")
        return 2

    version = (ROOT / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8")
    print(version.strip())
    print(f"Remote:\n{git('remote', '-v')}")
    print(f"Branch: {git('rev-parse', '--abbrev-ref', 'HEAD')}")

    for path in WRONG_NAMES:
        if path.exists():
            print(f"Unexpected file {path.name}. Unity's lock file is Packages/packages-lock.json.")

    if LOCK.is_file():
        print(f"OK: {LOCK.relative_to(ROOT)} exists ({LOCK.stat().st_size} bytes).")
        print("Commit it if it is untracked: git add Packages/packages-lock.json")
    else:
        print("MISSING: Packages/packages-lock.json")
        print("There is no packages.lock. Unity writes packages-lock.json only after a successful resolve.")
        library = ROOT / "Library"
        if library.is_dir():
            print("Library/ exists, so an Editor has opened this folder. Resolution did not finish successfully.")
            print("In Unity: Window → Package Manager, and Window → General → Console. Use the first red Package Manager error.")
        else:
            print("Library/ is absent, so this clone has not been imported by Unity yet.")
            print("Unity Hub → Add project from disk → this root, with editor 6000.3.24f1 and WebGL Build Support.")
        print("Do not create the lock file by hand.")
        print("For a Package Manager failure (\"Cannot read properties of null (reading 'severity')\",")
        print("or \"Error fetching package list offline\"), run: python3 Tools/diagnose_unity_packages.py")
        print("See Documentation/PACKAGE_RESOLUTION_TROUBLESHOOTING.md — the real error is in the Editor log.")

    print("Ignored local key files must stay untracked:")
    for name in ("google-tiles-key.local.txt", "google-places-key.local.txt"):
        path = ROOT / "Assets" / "WorldPvp" / "StreamingAssets" / name
        print(f"  {path.relative_to(ROOT)}: {'present (gitignored)' if path.exists() else 'not present'}")
    return 0 if LOCK.is_file() and not missing else 1


if __name__ == "__main__":
    sys.exit(main())
