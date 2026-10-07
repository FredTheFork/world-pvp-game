#!/usr/bin/env python3
"""Serve a Unity WebGL build locally with Cesium-required isolation/Brotli headers."""

from __future__ import annotations

import argparse
import mimetypes
import re
import sys
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit

JOIN_PATH = re.compile(r"^/join/[A-Za-z0-9]{4,32}/?$")


class WorldPvpHandler(SimpleHTTPRequestHandler):
    server_version = "WorldPvpLocalWeb/1.0"

    def translate_path(self, path: str) -> str:
        request_path = unquote(urlsplit(path).path)
        if JOIN_PATH.fullmatch(request_path):
            path = "/index.html"
        return super().translate_path(path)

    def guess_type(self, path: str) -> str:
        clean_path = urlsplit(path).path
        if clean_path.endswith(".wasm.br"):
            return "application/wasm"
        if clean_path.endswith((".framework.js.br", ".loader.js.br", ".js.br")):
            return "application/javascript; charset=utf-8"
        if clean_path.endswith(".data.br"):
            return "application/octet-stream"
        if clean_path.endswith(".symbols.json.br"):
            return "application/json"
        if clean_path.endswith(".br"):
            return "application/octet-stream"
        return mimetypes.guess_type(clean_path)[0] or "application/octet-stream"

    def end_headers(self) -> None:
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        self.send_header("Cross-Origin-Resource-Policy", "cross-origin")
        request_path = urlsplit(self.path).path
        if request_path.endswith(".br"):
            self.send_header("Content-Encoding", "br")
        super().end_headers()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("Build/WebGL"), help="Unity WebGL build directory")
    parser.add_argument("--host", default="127.0.0.1", help="Default is local-only; use 0.0.0.0 only on a trusted network")
    parser.add_argument("--port", type=int, default=8000)
    args = parser.parse_args()

    root = args.root.expanduser().resolve()
    if not (root / "index.html").is_file():
        print(f"Unity WebGL index.html not found in: {root}", file=sys.stderr)
        print("Build it from Tools → World PvP → Phase 3 → Build WebGL (Brotli + Threads).", file=sys.stderr)
        return 2

    handler = partial(WorldPvpHandler, directory=str(root))
    server = ThreadingHTTPServer((args.host, args.port), handler)
    print(f"Serving {root}")
    print(f"Open http://{args.host}:{args.port}/ in a browser.")
    print(f"Opaque invite routes are served by the same build: http://{args.host}:{args.port}/join/7H29F")
    print("This is a static-file server only; UGS hosts the session/Relay. Ctrl+C stops local hosting.")
    if args.host == "0.0.0.0":
        print("WARNING: the server is visible on network interfaces; only use this on a trusted LAN.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopping local WebGL static server.")
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
