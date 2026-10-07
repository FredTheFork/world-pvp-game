#!/usr/bin/env python3
"""Small HTTP-level smoke tests for the local Unity WebGL static server."""

from __future__ import annotations

import sys
import tempfile
import threading
import unittest
from functools import partial
from http.server import ThreadingHTTPServer
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Tools"))
from serve_webgl_local import WorldPvpHandler  # noqa: E402


class LocalWebGlServerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_directory.name)
        (self.root / "index.html").write_text("phase-three-build", encoding="utf-8")
        build = self.root / "Build"
        build.mkdir()
        (build / "Test.wasm.br").write_bytes(b"brotli-placeholder")
        handler = partial(WorldPvpHandler, directory=str(self.root))
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base_url = "http://127.0.0.1:" + str(self.server.server_port)

    def tearDown(self) -> None:
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)
        self.temp_directory.cleanup()

    def test_opaque_invite_path_returns_the_webgl_index_without_redirect(self) -> None:
        with urlopen(self.base_url + "/join/7H29F", timeout=3) as response:
            self.assertEqual(response.status, 200)
            self.assertEqual(response.read().decode("utf-8"), "phase-three-build")
            self.assertEqual(response.headers.get("Cross-Origin-Opener-Policy"), "same-origin")
            self.assertEqual(response.headers.get("Cross-Origin-Embedder-Policy"), "require-corp")

    def test_brotli_wasm_has_expected_content_type_and_encoding(self) -> None:
        with urlopen(self.base_url + "/Build/Test.wasm.br", timeout=3) as response:
            self.assertEqual(response.status, 200)
            self.assertEqual(response.headers.get("Content-Type"), "application/wasm")
            self.assertEqual(response.headers.get("Content-Encoding"), "br")

    def test_malformed_invite_code_does_not_fall_back_to_index(self) -> None:
        with self.assertRaises(HTTPError) as caught:
            urlopen(self.base_url + "/join/7H-29F", timeout=3)
        self.assertEqual(caught.exception.code, 404)


if __name__ == "__main__":
    unittest.main(verbosity=2)
