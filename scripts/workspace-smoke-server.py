"""Loopback-only synthetic API for the Windows UI smoke. Never contacts production."""
import argparse
import json
from datetime import date
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse


class Handler(BaseHTTPRequestHandler):
    requests_seen = []
    def log_message(self, *_args):
        pass

    def do_GET(self):
        self.respond()

    def do_POST(self):
        self.respond()

    def read_body(self):
        if self.headers.get("Transfer-Encoding", "").lower() != "chunked":
            return self.rfile.read(int(self.headers.get("Content-Length", 0)))
        chunks = []
        while True:
            size = int(self.rfile.readline().split(b";", 1)[0], 16)
            if size == 0:
                while self.rfile.readline().strip():
                    pass
                return b"".join(chunks)
            chunks.append(self.rfile.read(size))
            if self.rfile.read(2) != b"\r\n":
                raise ValueError("Invalid synthetic request chunk")

    def respond(self):
        path = urlparse(self.path).path
        self.requests_seen.append(self.command + " " + path)
        body = json.loads(self.read_body() or b"{}")
        month = date.today().strftime("%Y-%m")
        profile = {key: "Smoke Test" for key in (
            "employeeShortName", "employeeReportSignName", "authorizedFullName", "authorizedShortName",
            "employeeJobTitle", "employeeCompany", "employeeAddress", "driverLicenseNumber",
            "fuelCardNumber", "carModel", "licensePlate")}
        profile.update(employeeFullName="Workspace Smoke Engineer", employeePhone="9991112233",
                       posProjectId="smoke-pos", fuelNorm=9, fuelTypes=["АИ-95"], reportStartMonth="2020-01")
        user = {"id": "workspace-smoke", "email": "smoke@example.invalid", "role": "engineer"}
        responses = {
            "/health": {"ok": True},
            "/smoke-status": {"requests": self.requests_seen[-100:]},
            "/auth/request-code": {"success": True},
            "/auth/verify-code": {"token": "synthetic-smoke-session", "user": user},
            "/me": {"user": user},
            "/auth/logout": {"success": True},
            "/api/v2/gsm/profile": {"profile": profile, "availableFuelTypes": ["АИ-95"]},
            "/api/v2/gsm/projects": {"projects": [{"id": "smoke-pos", "name": "POS Smoke", "budgetCode": "S"}]},
            "/api/v2/routes": {"records": [{"date": month + "-01", "workType": "POS", "sent": False,
                "distanceKm": 42, "stops": [{"address": "Smoke Route Address", "requestNumber": "SMOKE-ROUTE"}]}]},
            "/api/v2/fuel": {"records": []},
            "/api/v2/gsm/odometer-suggestion": {"month": month, "startOdometer": 1000},
            "/api/v2/gsm/jobs": {"jobs": []},
            "/v1/auth/login": {"data": {"auth_key": "synthetic-so-session"}},
            "/v1/user/me": {"data": {"sys_id": "smoke-so", "username": "smoke", "first_name": "Smoke", "last_name": "SO"}},
            "/v1/list/itsm_request": {"data": {"items": [
                {"sys_id": "one", "number": "SMOKE-CLOSED", "state": "closed", "multicard_terminal_address": "Smoke Closed Address"},
                {"sys_id": "two", "number": "SMOKE-OPEN", "state": "in_progress", "multicard_terminal_address": "Must Stay Hidden"}
            ], "total": 2}},
        }
        status = 200
        if path == "/api/v2/gsm/report":
            if body != {"month": month}:
                status, result = 400, {"error": "INVALID_SMOKE_BODY"}
            else:
                result = {"month": month, "success": True, "generated": True, "sent": True,
                          "status": "SENT", "outputFileName": "Smoke.xlsx", "sentToEmail": user["email"]}
        elif path in responses:
            result = responses[path]
        else:
            status, result = 404, {"error": "UNEXPECTED_SMOKE_PATH"}
        payload = json.dumps(result).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("port", type=int)
    args = parser.parse_args()
    ThreadingHTTPServer(("127.0.0.1", args.port), Handler).serve_forever()
