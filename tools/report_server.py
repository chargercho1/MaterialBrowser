"""Logs the keystrokes the input probe page reports, so a headless harness can
tell whether real keyboard input reached the page.

    python tools/report_server.py <port> <logfile>
"""
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse, parse_qs

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8799
LOG = sys.argv[2] if len(sys.argv) > 2 else "report.log"


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        query = parse_qs(urlparse(self.path).query)
        line = "%s len=%s text=%s\n" % (
            self.path.split("?")[0],
            (query.get("len") or ["-"])[0],
            (query.get("text") or [""])[0],
        )
        with open(LOG, "a", encoding="utf-8") as handle:
            handle.write(line)
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()

    def log_message(self, *args):
        pass


HTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
