#!/usr/bin/env python3
"""Minimal GABP client for RimBridgeServer (brrainz.rimbridgeserver).

Wire format (SPEC/1.0/transport.md): Content-Length framed JSON over a plain
TCP socket — the bridge LISTENS and the client dials 127.0.0.1:<port>, which
is why curl gets nothing: the port is open but it is not an HTTP server.

    devtools/bridge/gabp.py <player.log | port token> [method] [json-params]

With no method it does session/hello then tools/list and prints the surface.
The port and token are announced in the INSTANCE'S OWN Player.log
("GABP server running standalone on port N" / "Bridge token: HEX"), so
passing the log path is the normal way in — from_log polls until the
announcement appears, surviving a slow modlist boot.

Promoted from the 2026-08-27 bridge-session salvage
(~/Movies/apparel-painter-bridge-2026-08-27); main() is now guarded so
drivers can `from gabp import Bridge, connect`.
"""
import json
import os
import re
import socket
import sys
import time
import uuid

HOST = "127.0.0.1"


class Bridge:
    def __init__(self, port, token, wait=600):
        # The bridge only starts listening after play-data load — minutes on
        # the studio list, and stalled entirely while the window is
        # backgrounded — so the dial retries until the port wakes up.
        deadline = time.time() + wait
        while True:
            try:
                # Generous read timeout: start_debug_game_ready generates a
                # whole map before it answers.
                self.sock = socket.create_connection((HOST, int(port)), timeout=600)
                break
            except OSError:
                if time.time() >= deadline:
                    raise SystemExit(f"bridge not listening on {port} after {wait}s")
                time.sleep(2)
        self.buf = b""
        self.call("session/hello", {"token": token, "bridgeVersion": "1.0.0"})

    def call(self, method, params=None):
        msg = {"v": "gabp/1", "id": str(uuid.uuid4()), "type": "request",
               "method": method, "params": params or {}}
        body = json.dumps(msg).encode()
        self.sock.sendall((f"Content-Length: {len(body)}\r\n"
                           "Content-Type: application/json\r\n\r\n").encode() + body)
        return self.recv()

    def _fill(self, n):
        while len(self.buf) < n:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise EOFError("bridge closed the connection")
            self.buf += chunk

    def recv(self):
        # Headers, then exactly Content-Length bytes. Anything left over is
        # the start of the next message, so it stays in the buffer.
        while b"\r\n\r\n" not in self.buf:
            self._fill(len(self.buf) + 1)
        head, _, rest = self.buf.partition(b"\r\n\r\n")
        length = 0
        for line in head.decode(errors="replace").split("\r\n"):
            if line.lower().startswith("content-length:"):
                length = int(line.split(":", 1)[1].strip())
        self.buf = rest
        self._fill(length)
        body, self.buf = self.buf[:length], self.buf[length:]
        return json.loads(body)

    def tool(self, name, args=None, ok=True):
        """Call one bridge tool. ok=False returns {'error': ...} instead of
        exiting, for resolve-with-fallback flows."""
        r = self.call("tools/call", {"name": name, "arguments": args or {}})
        if r.get("error"):
            if ok:
                raise SystemExit(f"{name}: {json.dumps(r['error'])[:400]}")
            return {"error": r["error"]}
        return r["result"]


def from_log(path, timeout=240):
    """Poll a Player.log for the bridge announcement; return (port, token)."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            text = open(path, errors="replace").read()
        except OSError:
            text = ""
        m = re.search(r"GABP server running standalone on port (\d+)", text)
        t = re.search(r"Bridge token: ([0-9a-f]+)", text)
        if m and t:
            return m.group(1), t.group(1)
        time.sleep(2)
    raise SystemExit(f"no bridge announcement in {path} after {timeout}s")


def connect(argv):
    """Shared driver entry: <player.log | port token>, returns (Bridge, rest)."""
    if argv and os.path.isfile(argv[0]):
        port, token = from_log(argv[0])
        rest = argv[1:]
    elif len(argv) >= 2:
        port, token = argv[0], argv[1]
        rest = argv[2:]
    else:
        raise SystemExit("usage: <player.log | port token> [...]")
    return Bridge(port, token), rest


def main():
    b, rest = connect(sys.argv[1:])
    print("session up")
    if rest:
        params = json.loads(rest[1]) if len(rest) > 1 else {}
        print(json.dumps(b.call(rest[0], params), indent=2)[:4000])
        return
    tools = b.call("tools/list")
    items = (tools.get("result") or {}).get("tools") or []
    print(f"tools/list -> {len(items)} tools")
    for t in items:
        print("  " + str(t.get("name", t.get("title", "?"))))


if __name__ == "__main__":
    main()
