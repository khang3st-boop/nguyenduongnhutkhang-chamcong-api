"""Kiểm thử toàn bộ API chấm công. Cần: API chạy với Smtp__Host=127.0.0.1 Smtp__Port=2525 Smtp__Security=None.
Chạy: python scripts/verify_api.py [http://localhost:5300]"""
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

sys.path.insert(0, os.path.dirname(__file__))
import smtp_sink  # noqa: E402

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5300").rstrip("/")
ADMIN_EMAIL = os.environ.get("ADMIN_EMAIL", "admin@test.local")
ADMIN_PASS = os.environ.get("ADMIN_PASS", "Admin@12345")
results = []
SMS = []  # (số nhận, nội dung) do máy chủ Twilio giả nhận được


def start_fake_twilio(port=2526):
    import threading
    import urllib.parse
    from http.server import BaseHTTPRequestHandler, HTTPServer

    class H(BaseHTTPRequestHandler):
        def do_POST(self):
            q = urllib.parse.parse_qs(self.rfile.read(int(self.headers.get("Content-Length", 0))).decode())
            SMS.append((q.get("To", [""])[0], q.get("Body", [""])[0]))
            self.send_response(201)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(b'{"sid":"SMtest"}')

        def log_message(self, *a):
            pass

    srv = HTTPServer(("127.0.0.1", port), H)
    threading.Thread(target=srv.serve_forever, daemon=True).start()


def sms_otp(to, since):
    for _ in range(40):
        for t, body in SMS[since:]:
            if t == to:
                m = re.search(r"(\d{6})", body)
                if m:
                    return m.group(1)
        time.sleep(0.25)
    raise RuntimeError(f"không nhận được SMS OTP cho {to}; SMS={SMS}")


def call(method, path, body=None, token=None):
    req = urllib.request.Request(BASE + path, method=method, data=None if body is None else json.dumps(body).encode())
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, raw


def check(name, got, want, extra=""):
    ok = got in want if isinstance(want, (list, tuple, set)) else got == want
    results.append((ok, name, got, want))
    print(("PASS " if ok else "FAIL ") + f"{name}: {got} (mong đợi {want}) {extra if not ok else ''}")


def otp_for(email, since):
    for _ in range(40):
        for to, body in smtp_sink.MAILS[since:]:
            if to == email.lower():
                m = re.search(r"\b(\d{6})\b", body)
                if m:
                    return m.group(1)
        time.sleep(0.25)
    raise RuntimeError("không nhận được OTP cho " + email)


def login_flow(user, pw, email):
    n = len(smtp_sink.MAILS)
    s, b = call("POST", "/api/Auth/login", {"username": user, "password": pw})
    check(f"login {user} (gửi OTP)", s, 200, b)
    code = otp_for(email, n)
    s, b = call("POST", "/api/Auth/verify-otp", {"username": user, "code": code})
    check(f"verify-otp {user}", s, 200, b)
    return b["token"]


def main():
    smtp_sink.start(2525)
    start_fake_twilio(2526)
    t = str(int(time.time()))[-6:]
    mgr, memail = f"mgr{t}", f"mgr{t}@example.com"
    emp, emp2 = f"emp{t}", f"emq{t}"

    s, b = call("GET", "/health")
    check("GET /health", s, 200, b)
    s, _ = call("GET", "/api/Workplaces")
    check("không token → 401", s, 401)

    # ---- Auth ----
    s, b = call("POST", "/api/Auth/register", {"username": "ab", "password": "x", "fullName": "", "email": "bad"})
    check("register dữ liệu sai → 400", s, 400)
    n = len(smtp_sink.MAILS)
    s, b = call("POST", "/api/Auth/register", {"username": mgr, "password": "Pass@123", "fullName": "Quản Lý Test", "email": memail, "role": "manager", "phone": "0912345678"})
    check("register manager (có SĐT)", s, 200, b)
    s, b = call("POST", "/api/Auth/register", {"username": mgr, "password": "Pass@123", "fullName": "Trùng", "email": "other@example.com", "role": "manager"})
    check("register trùng username → 400", s, 400)
    s, b = call("POST", "/api/Auth/login", {"username": mgr, "password": "Pass@123"})
    check("login khi chưa xác minh email → 403", s, 403, b)
    s, b = call("POST", "/api/Auth/verify-email", {"username": mgr, "code": "000000"})
    check("verify-email sai mã → 400", s, 400)
    code = otp_for(memail, n)
    s, b = call("POST", "/api/Auth/verify-email", {"username": mgr, "code": code})
    check("verify-email đúng", s, 200, b)
    s, b = call("POST", "/api/Auth/login", {"username": mgr, "password": "Pass@123"})
    check("manager chưa được duyệt → 403 pendingApproval", s, 403, b)
    at = login_flow(ADMIN_EMAIL, ADMIN_PASS, ADMIN_EMAIL)
    s, b = call("GET", "/api/Approvals", token=at)
    check("GET Approvals (admin)", s, 200, b)
    pend = [x for x in (b if isinstance(b, list) else []) if x.get("userName") == mgr]
    check("manager nằm trong danh sách chờ duyệt", len(pend), 1)
    s, b = call("POST", f"/api/Approvals/{pend[0]['id']}/approve", token=at)
    check("admin duyệt manager", s, 204, b)
    s, b = call("POST", "/api/Auth/login", {"username": mgr, "password": "sai"})
    check("login sai mật khẩu → 401", s, 401)
    time.sleep(31)  # giãn cách gửi lại OTP 30 giây
    mt = login_flow(mgr, "Pass@123", memail)
    s, b = call("POST", "/api/Auth/verify-otp", {"username": mgr, "code": "123456"})
    check("verify-otp sai → 401", s, 401)
    s, b = call("GET", "/api/Auth/me", token=mt)
    check("GET me (manager)", s, 200, b)
    check("me.role = Manager", b.get("role") if isinstance(b, dict) else None, "Manager")
    s, b = call("POST", "/api/Auth/google", {"idToken": "khong-hop-le"})
    check("google idToken sai → 4xx (503 nếu chưa cấu hình)", s, [400, 401, 503], b)
    s, b = call("POST", "/api/Auth/resend-email-code", {"username": mgr})
    check("resend-email-code", s, 200)

    s, b = call("POST", "/api/Auth/register", {"username": mgr + "p", "password": "Pass@123", "fullName": "Sai SĐT", "email": "p" + memail, "role": "manager", "phone": "abc"})
    check("register SĐT sai → 400", s, 400)

    # ---- Company ----
    s, b = call("GET", "/api/Company", token=mt)
    check("GET Company", s, 200, b)
    s, b = call("PUT", "/api/Company", {"name": "Công ty Kiểm Thử"}, mt)
    check("PUT Company", s, [200, 204], b)
    s, b = call("POST", "/api/Company/invite", token=mt)
    check("POST invite", s, 200, b)
    invite = b.get("code") if isinstance(b, dict) else None

    # ---- Workplaces ----
    lat, lng = 10.7769, 106.7009
    wp = {"name": "Văn phòng", "latitude": lat, "longitude": lng, "radiusMeters": 100, "startMinute": 0, "endMinute": 1439, "graceMinutes": 10}
    s, b = call("POST", "/api/Workplaces", {**wp, "radiusMeters": 5}, mt)
    check("tạo workplace bán kính <30 → 400", s, 400)
    s, b = call("POST", "/api/Workplaces", wp, mt)
    check("POST Workplace", s, [200, 201], b)
    wid = b["id"]
    s, b = call("GET", "/api/Workplaces", token=mt)
    check("GET Workplaces", s, 200)
    s, b = call("GET", f"/api/Workplaces/{wid}", token=mt)
    check("GET Workplace/{id}", s, 200)
    s, b = call("PUT", f"/api/Workplaces/{wid}", {**wp, "name": "Văn phòng chính"}, mt)
    check("PUT Workplace", s, [200, 204], b)
    s, b = call("GET", f"/api/Workplaces/{wid}/qr", token=mt)
    check("GET qr", s, 200, b)
    payload = b["payload"]
    s, b = call("GET", "/api/Workplaces/99999/qr", token=mt)
    check("qr workplace không tồn tại → 404", s, 404)

    # ---- Employees + đăng ký bằng mã mời ----
    s, b = call("POST", "/api/Employees", {"username": emp, "password": "Pass@123", "fullName": "Nhân Viên A", "position": "Dev"}, mt)
    check("POST Employee", s, [200, 201], b)
    eid = b["id"]
    s, b = call("GET", "/api/Employees", token=mt)
    check("GET Employees", s, 200)
    s, b = call("GET", f"/api/Employees/{eid}", token=mt)
    check("GET Employee/{id}", s, 200)
    s, b = call("PUT", f"/api/Employees/{eid}", {"fullName": "Nhân Viên A2", "position": "Lead"}, mt)
    check("PUT Employee", s, [200, 204], b)
    s, b = call("POST", "/api/Auth/register", {"username": emp2, "password": "Pass@123", "fullName": "Nhân Viên B", "role": "employee", "inviteCode": "000000"})
    check("register nhân viên mã mời sai → 400", s, 400)
    s, b = call("POST", "/api/Auth/register", {"username": emp2, "password": "Pass@123", "fullName": "Nhân Viên B", "role": "employee", "inviteCode": invite})
    check("register nhân viên thiếu email → 400", s, 400)
    e2mail = f"emq{t}@example.com"
    n = len(smtp_sink.MAILS)
    s, b = call("POST", "/api/Auth/register", {"username": emp2, "password": "Pass@123", "fullName": "Nhân Viên B", "email": e2mail, "role": "employee", "inviteCode": invite})
    check("register nhân viên bằng mã mời", s, 200, b)
    s, b = call("POST", "/api/Auth/verify-email", {"username": emp2, "code": otp_for(e2mail, n)})
    check("verify-email nhân viên", s, 200, b)
    s, b = call("POST", "/api/Auth/register", {"username": emp2 + "x", "password": "Pass@123", "fullName": "C", "role": "employee", "inviteCode": invite})
    check("mã mời dùng lại → 400", s, 400)

    # nhân viên không email → OTP về email quản lý
    et = login_flow(emp, "Pass@123", memail)
    s, b = call("GET", "/api/Auth/me", token=et)
    check("me.role = Employee", b.get("role") if isinstance(b, dict) else None, "Employee")

    # ---- Dịch (item f), phòng họp video, đổi thông tin, gửi lại OTP ----
    s, b = call("POST", "/api/Translate", {"texts": ["Hello"], "target": "vi"}, et)
    check("POST Translate", s, [200, 502], b)
    s, b = call("POST", "/api/Translate", {"texts": ["Hello"], "target": "xx"}, et)
    check("Translate ngôn ngữ sai → 400", s, 400)
    s, b = call("POST", "/api/Translate", {"texts": ["Hello"], "target": "vi"})
    check("Translate không token → 401", s, 401)
    s, b = call("GET", "/api/Rooms", token=et)
    check("GET Rooms (có Phòng chung)", s, 200, b)
    gen = b[0]["channel"] if isinstance(b, list) and b else ""
    s, b = call("POST", "/api/Rooms", {"name": "Phòng dự án"}, et)
    check("POST Room", s, 200, b)
    rid, rch = b["id"], b["channel"]
    s, b = call("PUT", f"/api/Rooms/{rid}", {"name": "Phòng dự án 2"}, et)
    check("PUT Room (người tạo)", s, 204, b)
    s, b = call("POST", "/api/Rooms/ping", {"channel": rch}, et)
    check("Rooms ping", s, 204, b)
    s, b = call("POST", "/api/Rooms/ping", {"channel": "team-999999-x"}, et)
    check("Rooms ping kênh công ty khác → 403", s, 403)
    s, b = call("POST", "/api/Rooms/leave", {"channel": rch}, et)
    check("Rooms leave", s, 204, b)
    s, b = call("DELETE", f"/api/Rooms/{rid}", token=et)
    check("DELETE Room", s, 204, b)
    s, b = call("POST", "/api/Auth/me/request-change", {"fullName": "Nhân Viên A3"}, et)
    check("request-change chỉ đổi tên (không cần OTP)", s, 200, b)
    s, b = call("POST", "/api/Auth/resend-login-otp", {"username": emp})
    check("resend-login-otp", s, [200, 429], b)

    # ---- Đăng nhập bằng SMS OTP ----
    time.sleep(31)
    n = len(SMS)
    s, b = call("POST", "/api/Auth/login", {"username": mgr, "password": "Pass@123", "channel": "sms"})
    check("login SMS (quản lý)", s, 200, b)
    check("channel = sms", b.get("channel") if isinstance(b, dict) else None, "sms")
    code = sms_otp("+84912345678", n)
    s, b = call("POST", "/api/Auth/verify-otp", {"username": mgr, "code": code})
    check("verify SMS OTP", s, 200, b)
    time.sleep(31)
    n = len(SMS)
    s, b = call("POST", "/api/Auth/login", {"username": emp, "password": "Pass@123", "channel": "sms"})
    check("login SMS (nhân viên dùng SĐT quản lý)", s, 200, b)
    code = sms_otp("+84912345678", n)
    s, b = call("POST", "/api/Auth/verify-otp", {"username": emp, "code": code})
    check("verify SMS OTP nhân viên", s, 200, b)
    s, b = call("GET", "/api/Auth/me", token=mt)
    check("me.phone = +84912345678", b.get("phone") if isinstance(b, dict) else None, "+84912345678")

    # ---- Phân quyền ----
    s, _ = call("GET", "/api/Employees", token=et)
    check("employee gọi Employees → 403", s, 403)
    s, _ = call("POST", "/api/Workplaces", wp, et)
    check("employee tạo workplace → 403", s, 403)
    s, _ = call("POST", "/api/Attendance/scan", {"payload": payload, "latitude": lat, "longitude": lng}, mt)
    check("manager quét QR → 403", s, 403)
    s, _ = call("GET", "/api/Admin/stats", token=mt)
    check("manager gọi Admin → 403", s, 403)

    # ---- Scan / Attendance ----
    s, b = call("GET", "/api/Attendance/today", token=et)
    check("today (chưa vào ca)", s, 200, b)
    check("today.working = False", b.get("working") if isinstance(b, dict) else None, False)
    s, b = call("POST", "/api/Attendance/scan", {"payload": "rac", "latitude": lat, "longitude": lng}, et)
    check("scan payload rác → 400", s, 400, b)
    s, b = call("POST", "/api/Attendance/scan", {"payload": f"CC.{wid}.1.abcdef0123456789", "latitude": lat, "longitude": lng}, et)
    check("scan QR sai chữ ký/hết hạn → 400", s, 400, b)
    s, b = call("GET", f"/api/Workplaces/{wid}/qr", token=mt)
    s, b = call("POST", "/api/Attendance/scan", {"payload": b["payload"], "latitude": lat + 0.01, "longitude": lng}, et)
    check("scan ngoài bán kính (~1.1km) → 403", s, 403, b)
    s, b = call("GET", f"/api/Workplaces/{wid}/qr", token=mt)
    s, b = call("POST", "/api/Attendance/scan", {"payload": b["payload"], "latitude": lat, "longitude": lng}, et)
    check("scan VÀO ca", s, 200, b)
    check("action = in", b.get("action") if isinstance(b, dict) else None, "in")
    s, b = call("GET", f"/api/Workplaces/{wid}/qr", token=mt)
    s, b = call("POST", "/api/Attendance/scan", {"payload": b["payload"], "latitude": lat, "longitude": lng}, et)
    check("scan RA ngay → 429", s, 429, b)
    s, b = call("GET", "/api/Attendance/today", token=et)
    check("today.working = True", b.get("working") if isinstance(b, dict) else None, True)

    # ---- Location ----
    s, b = call("POST", "/api/Location", {"latitude": lat, "longitude": lng, "accuracy": 8.5, "battery": 77}, et)
    check("POST Location", s, [200, 201, 204], b)
    s, b = call("POST", "/api/Location", {"latitude": 200, "longitude": lng}, et)
    check("POST Location sai toạ độ → 400", s, 400)
    s, _ = call("POST", "/api/Location", {"latitude": lat, "longitude": lng}, mt)
    check("manager POST Location → 403", s, 403)
    s, b = call("GET", "/api/Location/team", token=mt)
    check("GET Location/team", s, 200, b)
    check("team có 1 người", len(b) if isinstance(b, list) else -1, 1)
    s, b = call("GET", f"/api/Location/history/{eid}", token=mt)
    check("GET Location/history", s, 200, b)

    # ---- Attendance (quản lý) ----
    month = time.strftime("%Y-%m")
    s, b = call("GET", f"/api/Attendance?month={month}", token=mt)
    check("GET Attendance?month", s, 200, b)
    s, b = call("GET", f"/api/Attendance?month={month}&userId={eid}", token=mt)
    check("GET Attendance?userId", s, 200)
    s, b = call("GET", f"/api/Attendance/report?month={month}", token=mt)
    check("GET report", s, 200, b)
    s, b = call("GET", "/api/Attendance/live", token=mt)
    check("GET live", s, 200, b)
    check("live.working = 1", b.get("working") if isinstance(b, dict) else None, 1)
    s, b = call("GET", f"/api/Attendance/mine?month={month}", token=et)
    check("GET mine", s, 200, b)
    s, b = call("POST", "/api/Attendance", {"userId": eid, "workplaceId": wid, "checkIn": "2025-01-02T01:00:00Z", "checkOut": "2025-01-02T10:00:00Z", "note": "bù công"}, mt)
    check("POST Attendance thủ công", s, [200, 201], b)
    aid = b["id"]
    s, b = call("POST", "/api/Attendance", {"userId": eid, "workplaceId": wid, "checkIn": "2025-01-02T10:00:00Z", "checkOut": "2025-01-02T01:00:00Z"}, mt)
    check("POST Attendance giờ ra < giờ vào → 400", s, 400)
    s, b = call("PUT", f"/api/Attendance/{aid}", {"checkIn": "2025-01-02T02:00:00Z", "checkOut": "2025-01-02T11:00:00Z", "note": "sửa"}, mt)
    check("PUT Attendance", s, [200, 204], b)
    s, b = call("DELETE", f"/api/Attendance/{aid}", token=mt)
    check("DELETE Attendance", s, [200, 204], b)

    print("... đợi 61 giây để quét RA ca")
    time.sleep(61)
    s, b = call("GET", f"/api/Workplaces/{wid}/qr", token=mt)
    s, b = call("POST", "/api/Attendance/scan", {"payload": b["payload"], "latitude": lat, "longitude": lng}, et)
    check("scan RA ca", s, 200, b)
    check("action = out", b.get("action") if isinstance(b, dict) else None, "out")

    # ---- Chat + Video ----
    s, b = call("POST", "/api/Chat", {"text": "Xin chào cả nhóm"}, mt)
    check("POST Chat", s, [200, 201], b)
    mid = b.get("id") if isinstance(b, dict) else None
    s, b = call("GET", "/api/Chat", token=et)
    check("GET Chat (employee)", s, 200, b)
    s, b = call("PUT", f"/api/Chat/{mid}", {"text": "đã sửa"}, mt)
    check("PUT Chat", s, [200, 204], b)
    s, b = call("PUT", f"/api/Chat/{mid}", {"text": "hack"}, et)
    check("sửa tin của người khác → 403/404", s, [403, 404])
    s, b = call("POST", "/api/Chat/call", token=mt)
    check("POST Chat/call", s, 200, b)
    channel = b.get("channel") if isinstance(b, dict) else "x"
    s, b = call("POST", "/api/Video/token", {"channel": channel}, et)
    check("POST Video/token", s, 200, b)
    s, b = call("POST", "/api/Video/token", {"channel": "team-99999-abc"}, et)
    check("Video token kênh công ty khác → 403", s, 403)
    s, b = call("DELETE", f"/api/Chat/{mid}", token=mt)
    check("DELETE Chat", s, [200, 204], b)

    # ---- Admin ----
    n = len(smtp_sink.MAILS)
    s, b = call("POST", "/api/Auth/login", {"username": ADMIN_EMAIL, "password": ADMIN_PASS})
    if s == 200:
        at = None
        s2, b2 = call("POST", "/api/Auth/login", {"username": ADMIN_EMAIL, "password": ADMIN_PASS, "channel": "sms"})
        check("admin chưa có SĐT, login SMS → 400", s2, 400, b2)
        try:
            code = otp_for(ADMIN_EMAIL, n)
            s, b = call("POST", "/api/Auth/verify-otp", {"username": ADMIN_EMAIL, "code": code})
            check("admin verify-otp", s, 200, b)
            at = b.get("token")
        except Exception as e:
            check("admin OTP", str(e), "ok")
        if at:
            s, b = call("GET", "/api/Admin/stats", token=at)
            check("GET Admin/stats", s, 200, b)
            s, b = call("GET", f"/api/Admin/users?q={emp}", token=at)
            check("GET Admin/users", s, 200)
            s, b = call("GET", "/api/Admin/attendance", token=at)
            check("GET Admin/attendance", s, 200, b)
            s, _ = call("PUT", f"/api/Admin/users/{eid}/lock?locked=true", token=at)
            check("Admin khóa nhân viên", s, 204)
            s, b = call("POST", "/api/Auth/login", {"username": emp, "password": "Pass@123"})
            check("nhân viên bị khóa login → 403", s, 403, b)
            s, _ = call("PUT", f"/api/Admin/users/{eid}/lock?locked=false", token=at)
            check("Admin mở khóa", s, 204)
            s, _ = call("DELETE", f"/api/Admin/users/{eid}", token=at)
            check("Admin xóa nhân viên", s, 204)
    else:
        check("admin login (bước 1)", s, 200, b)

    # ---- Dọn dẹp ----
    s, b = call("DELETE", f"/api/Workplaces/{wid}", token=mt)
    check("DELETE Workplace", s, [200, 204], b)

    bad = [r for r in results if not r[0]]
    print(f"\nTổng: {len(results)} | đạt: {len(results) - len(bad)} | lỗi: {len(bad)}")
    for _, name, got, want in bad:
        print(f"  ✗ {name}: {got} (mong đợi {want})")
    sys.exit(1 if bad else 0)


main()
