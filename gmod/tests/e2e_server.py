"""A signing-enforcing protocol v1 server for the end-to-end test.

Checks every request's signature, clock window and nonce the way Ticket Tool
does, signs replies, runs its clock 1000 s ahead so the client has to correct
for skew, hands out one player.lookup, and records what it receives at
GET /__log (test only). Prints its port on stdout, then serves until killed.
"""

import hashlib
import hmac
import json
import sys
import threading
import time
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

KEY = "ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
SECRET = hmac.new(KEY.encode(), b"ticket-tool plugin signing v1", hashlib.sha256).hexdigest().encode()
BASE = "/api/plugin/v1"
SKEW = 1000
STEAM_ID = sys.argv[1] if len(sys.argv) > 1 else "76561197960287930"

lock = threading.Lock()
log = {"accepted": [], "rejected": []}
nonces = set()
handed_out = {"done": False}


def now():
    return int(time.time()) + SKEW


def sign(method, endpoint, ts, nonce, body):
    canonical = "\n".join(["v1", method, endpoint, str(ts), nonce, hashlib.sha256(body).hexdigest()])
    return "v1=" + hmac.new(SECRET, canonical.encode(), hashlib.sha256).hexdigest()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def send(self, status, payload, endpoint=None, nonce=None):
        body = json.dumps(payload, separators=(",", ":")).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        if endpoint and nonce:
            ts = now()
            self.send_header("X-TT-Timestamp", str(ts))
            self.send_header("X-TT-Nonce", nonce)
            self.send_header("X-TT-Signature", sign("RESPONSE", endpoint, ts, nonce, body))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/__log":
            with lock:
                self.send(200, log)
        else:
            self.send(404, {"ok": False})

    def do_POST(self):
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length)
        endpoint = self.path[len(BASE):] if self.path.startswith(BASE) else None
        h = self.headers
        nonce = h.get("X-TT-Nonce") or ""

        def reject(error, status=401, **extra):
            with lock:
                log["rejected"].append({"endpoint": endpoint, "error": error})
            self.send(status, {"ok": False, "error": error, **extra}, endpoint, nonce)

        if endpoint is None:
            return self.send(404, {"ok": False, "error": "not_found"})
        if h.get("X-TT-Key-Id") != "0123456789abcdef":
            return self.send(401, {"ok": False, "error": "invalid_key"})
        if h.get("Content-Type", "").split(";")[0] != "application/json" or h.get("X-TT-Protocol") != "1":
            return reject("missing_headers", 400)
        try:
            ts = int(h.get("X-TT-Timestamp"))
        except (TypeError, ValueError):
            return reject("missing_headers")
        if abs(ts - now()) > 300:
            return reject("stale_timestamp", serverTime=now())
        if not (16 <= len(nonce) <= 64) or not all(c.isalnum() or c in "_-" for c in nonce):
            return reject("bad_nonce")
        if not hmac.compare_digest(h.get("X-TT-Signature", ""), sign("POST", endpoint, ts, nonce, body)):
            return reject("bad_signature")
        with lock:
            if nonce in nonces:
                return reject("replayed")
            nonces.add(nonce)
            log["accepted"].append({"endpoint": endpoint, "body": body.decode(), "plugin": h.get("X-TT-Plugin")})

        if endpoint == "/hello":
            return self.send(200, {"ok": True, "protocol": 1, "pluginType": "gmod", "key": {"name": "E2E"},
                                   "server": {"id": "1", "name": "E2E Discord"}, "poll": {"waitSeconds": 1}},
                             endpoint, nonce)
        if endpoint == "/poll":
            requests = []
            with lock:
                if not handed_out["done"]:
                    handed_out["done"] = True
                    expires = datetime.fromtimestamp(now() + 5, timezone.utc)
                    requests.append({
                        "id": "e2e-1", "type": "player.lookup",
                        "expiresAt": expires.strftime("%Y-%m-%dT%H:%M:%S.000Z"),
                        "player": {"uuid": None, "steamId": STEAM_ID, "discordId": None, "name": "Gabe"},
                        "include": ["profile", "bans"],
                    })
            if not requests:
                time.sleep(1)
            return self.send(200, {"ok": True, "requests": requests}, endpoint, nonce)
        if endpoint in ("/respond", "/goodbye"):
            return self.send(200, {"ok": True}, endpoint, nonce)
        return self.send(404, {"ok": False, "error": "not_found"}, endpoint, nonce)


server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
print(server.server_address[1], flush=True)
server.serve_forever()
