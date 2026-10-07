#!/usr/bin/env python3
"""Copy the static-host routing/isolation configuration beside a Unity WebGL build."""

from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("build_dir", nargs="?", type=Path, default=ROOT / "Build/WebGL")
    parser.add_argument("--provider", choices=("vercel",), default="vercel")
    args = parser.parse_args()

    build_dir = args.build_dir.expanduser().resolve()
    if not (build_dir / "index.html").is_file():
        print(f"Unity WebGL index.html not found in: {build_dir}", file=sys.stderr)
        return 2

    if args.provider == "vercel":
        source = ROOT / "WebDeploy/Vercel/vercel.json"
        destination = build_dir / "vercel.json"
        shutil.copyfile(source, destination)
        print(f"Copied {source.relative_to(ROOT)} to {destination}")
        oversized = []
        for path in build_dir.rglob("*"):
            if path.is_file() and path.stat().st_size > 100 * 1024 * 1024:
                oversized.append((path, path.stat().st_size))
        if oversized:
            print("WARNING: These files exceed Vercel Hobby's documented 100 MB static upload limit:")
            for path, size in oversized:
                print(f"  {path.relative_to(build_dir)}: {size / (1024 * 1024):.1f} MiB")
            print("Use the local laptop server or another host with a suitable file-size limit.")
        else:
            print("No individual asset exceeds 100 MiB; still verify account/plan rules and deployment limits.")

    print("The config enables COOP/COEP isolation, Brotli MIME/encoding, and /join/{code} rewrites.")
    print("The route contains only a join code; the HTML template uses root-relative build assets.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
