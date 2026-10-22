"""Kiểm thử luồng API của KidGuard. OTP được chèn thẳng vào SQL (cùng thuật toán băm của server)
để không phụ thuộc hộp thư; mọi bước còn lại đi qua HTTP thật. Chạy: python -I scripts/verify_api.py"""
import sys as _s; _s.stdout.reconfigure(encoding="utf-8")
import hashlib, hmac, json, subprocess, sys, urllib.request, urllib.error, uuid

BASE = "http://localhost:5300"
SERVER = r"NHUTKHANG\KHANGCFV123"
DB = "nguyenduongnhutkhang_app"
API_DIR = r"C:\Users\MY PC\Source\Repos\nguyenduongnhutkhang_app\api"


def secret():
    out = subprocess.run(["dotnet", "user-secrets", "list"], cwd=API_DIR, capture_output=True, text=True).stdout
    for line in out.splitlines():
        if line.startswith("JWTKey:Secret"):
            return line.split(" = ", 1)[1].strip()
    raise SystemExit("Không đọc được JWTKey:Secret")


SECRET = secret()


def sql(q):
    r = subprocess.run(["sqlcmd", "-S", SERVER, "-d", DB, "-E", "-h", "-1", "-W", "-Q", "SET NOCOUNT ON;" + q],
                       capture_output=True, text=True)
    return r.stdout.strip()


def call(method, path, body=None, token=None, raw=None):
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    req = urllib.request.Request(BASE + path, data=data, method=method)
    if body is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, timeout=20) as r:
            txt = r.read().decode()
            return r.status, (json.loads(txt) if txt else None)
    except urllib.error.HTTPError as e:
        txt = e.read().decode()
        try:
            return e.code, json.loads(txt)
        except Exception:
            return e.code, txt


failures = []


def check(name, cond, info=""):
    print(("PASS " if cond else "FAIL ") + name, "" if cond else info)
    if not cond:
        failures.append(name)


import re, time
import smtp_sink

MAILS = smtp_sink.MAILS
smtp_sink.start()


def otp_for(email):
    """Lấy mã OTP 6 số từ email mới nhất gửi tới địa chỉ này (SMTP thật qua MailKit)."""
    for _ in range(40):
        for to, body in reversed(MAILS):
            if to == email.lower():
                m = re.search(r"<b>(\d{6})</b>", body)
                if m:
                    return m.group(1)
        time.sleep(0.25)
    raise SystemExit("Không nhận được email OTP cho " + email)


def mail_count(email):
    return sum(1 for to, _ in MAILS if to == email.lower())


def get_email(username):
    return sql(f"select Email from AspNetUsers where UserName='{username}'")


def login(username, password, email=None):
    email = email or get_email(username)
    before = mail_count(email)
    s, r = call("POST", "/api/Auth/login", {"username": username, "password": password})
    if s != 200:
        return s, r
    while mail_count(email) == before:
        time.sleep(0.2)
    return call("POST", "/api/Auth/verify-otp", {"username": username, "code": otp_for(email)})


tag = uuid.uuid4().hex[:6]
pname = "par_" + tag
# --- Đăng ký: email xác minh thật qua SMTP ---
pemail = f"{pname}@example.com"
s, r = call("POST", "/api/Auth/register", {"username": pname, "email": pemail, "password": "Parent@123", "fullName": "Phu Huynh Test"})
check("register gửi OTP xác minh", s == 200 and r.get("requiresEmailVerification"), (s, r))
code = otp_for(pemail)
s, r = call("POST", "/api/Auth/login", {"username": pname, "password": "Parent@123"})
check("login chặn khi email chưa xác minh", s == 403 and r.get("needsEmailVerification"), (s, r))
s, r = call("POST", "/api/Auth/verify-email", {"username": pname, "code": "000000"})
check("verify-email sai mã bị từ chối", s == 400, (s, r))
code = otp_for(pemail)
s, r = call("POST", "/api/Auth/verify-email", {"username": pname, "code": code})
check("verify-email đúng mã", s == 200, (s, r))
s, r = call("POST", "/api/Auth/login", {"username": pname, "password": "sai"})
check("login sai mật khẩu 401", s == 401)
s, r = login(pname, "Parent@123")
check("login + OTP trả JWT role Parent", s == 200 and r["user"]["role"] == "Parent", (s, r))
P = r["token"]
s, r = call("POST", "/api/Auth/verify-otp", {"username": pname, "code": otp_for(pemail)})
check("OTP dùng lại bị từ chối", s == 401)
s, r = call("POST", "/api/Auth/google", {"idToken": "x"})
check("google: không cấu hình/không hợp lệ bị từ chối", s in (401, 503), (s, r))

# --- CRUD trẻ ---
cname = "kid_" + tag
s, r = call("POST", "/api/Children", {"username": cname, "password": "Kid@12345", "fullName": "Be Test"}, P)
check("tạo trẻ", s == 201, (s, r))
kid_id = r["id"]
s, r = call("PUT", f"/api/Children/{kid_id}", {"fullName": "Be Test Sua", "password": None, "email": None}, P)
check("sửa trẻ", s == 204, (s, r))
s, r = call("GET", "/api/Children", token=P)
check("danh sách trẻ", s == 200 and any(k["fullName"] == "Be Test Sua" for k in r))

# --- Vùng an toàn ---
s, r = call("POST", "/api/SafeZones", {"name": "Nhà", "latitude": 10.7769, "longitude": 106.7009, "radiusMeters": 200}, P)
check("tạo vùng an toàn", s == 201, (s, r))
zid = r["id"]
s, r = call("PUT", f"/api/SafeZones/{zid}", {"name": "Nhà riêng", "latitude": 10.7769, "longitude": 106.7009, "radiusMeters": 300}, P)
check("sửa vùng", s == 204)

# --- Đăng nhập trẻ (OTP về email phụ huynh) + vị trí + geofence ---
s, r = login(cname, "Kid@12345", pemail)
check("trẻ login + OTP", s == 200 and r["user"]["role"] == "Child", (s, r))
K = r["token"]
s, r = call("POST", "/api/SafeZones", {"name": "x", "latitude": 0, "longitude": 0, "radiusMeters": 100}, K)
check("trẻ không được tạo vùng", s == 403)
s, r = call("POST", "/api/Location", {"latitude": 10.7770, "longitude": 106.7010, "accuracy": 5, "battery": 80}, K)
check("trẻ gửi vị trí trong vùng", s == 200 and r["events"] == [], (s, r))
s, r = call("POST", "/api/Location", {"latitude": 10.80, "longitude": 106.75, "accuracy": 5, "battery": 79}, K)
check("rời vùng sinh sự kiện ExitZone", s == 200 and r["events"] and r["events"][0]["type"] == "ExitZone", (s, r))
s, r = call("GET", "/api/Location/family", token=P)
check("phụ huynh thấy vị trí mới nhất", s == 200 and abs(r[0]["latitude"] - 10.80) < 1e-6, (s, r))
s, r = call("GET", f"/api/Location/history/{kid_id}", token=P)
check("lịch sử vị trí", s == 200 and len(r) == 2)

# --- SOS ---
boundary = "----b" + tag
def multipart(fields):
    out = b""
    for k, v in fields.items():
        out += f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"\r\n\r\n{v}\r\n".encode()
    return out + f"--{boundary}--\r\n".encode()

req = urllib.request.Request(BASE + "/api/Alerts/sos", data=multipart({"Latitude": "10.80", "Longitude": "106.75", "Battery": "70", "Note": "can giup"}), method="POST")
req.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
req.add_header("Authorization", "Bearer " + K)
with urllib.request.urlopen(req) as resp:
    sos = json.loads(resp.read())
check("SOS tạo cảnh báo", sos.get("status") is True, sos)
s, r = call("GET", "/api/Alerts", token=P)
types = [a["Type"] if "Type" in a else a["type"] for a in r]
check("phụ huynh thấy SOS và ExitZone", "SOS" in types and "ExitZone" in types, r)
sos_alert = next(a for a in r if a["type"] == "SOS")
check("SOS thông minh có vùng gần nhất + pin", "Nhà riêng" in sos_alert["message"] and "70%" in sos_alert["message"], sos_alert)
s, r = call("PUT", f"/api/Alerts/{sos_alert['id']}/resolve", token=P)
check("đánh dấu đã xử lý", s == 204)

# --- Chat + cuộc gọi ---
s, r = call("POST", "/api/Chat", {"text": "Con về chưa?"}, P)
check("gửi tin nhắn", s == 200, (s, r))
msg_id = r["id"]
s, r = call("PUT", f"/api/Chat/{msg_id}", {"text": "Con đang ở đâu?"}, P)
check("sửa tin nhắn", s == 204)
s, r = call("POST", "/api/Chat/call", token=K)
check("trẻ bắt đầu cuộc gọi", s == 200 and r["channel"].startswith("family-"), (s, r))
chan = r["channel"]
s, r = call("GET", "/api/Chat", token=P)
check("khung chat có tin call", s == 200 and any(m["kind"] == "call" for m in r), r)
s, r = call("POST", "/api/Video/token", {"channel": chan}, P)
check("video token: 503 khi chưa có AppId hoặc trả token", s in (200, 503), (s, r))
s, r = call("POST", "/api/Video/token", {"channel": "family-99999-x"}, P)
check("video token: không vào phòng gia đình khác", s in (403, 503), (s, r))
s, r = call("DELETE", f"/api/Chat/{msg_id}", token=P)
check("xóa tin nhắn", s == 204)

# --- Nhật ký dịch ---
s, r = call("POST", "/api/Translations", {"kind": "photo", "sourceLang": "en", "targetLang": "vi", "sourceText": "Hello", "translatedText": "Xin chào"}, K)
check("trẻ lưu lịch sử dịch", s == 200, (s, r))
tid = r["id"]
s, r = call("GET", f"/api/Translations?childId={kid_id}&q=Xin", token=P)
check("phụ huynh xem + tìm lịch sử dịch", s == 200 and len(r) == 1, (s, r))
s, r = call("DELETE", f"/api/Translations/{tid}", token=P)
check("xóa lịch sử dịch", s == 204)

# --- Phân quyền ---
s, r = call("GET", "/api/Admin/stats", token=P)
check("phụ huynh không vào được Admin", s == 403)
s, r = call("GET", "/api/Children", token=K)
check("trẻ không vào được Children", s == 403)
s, r = call("GET", "/api/Children")
check("không token → 401", s == 401)

# --- Admin ---
s, r = login("admin", "Admin@123456")
check("admin login + OTP", s == 200 and r["user"]["role"] == "Admin", (s, r))
A = r["token"]
s, r = call("GET", "/api/Admin/stats", token=A)
check("admin xem thống kê", s == 200 and r["families"] >= 1, (s, r))
s, r = call("GET", f"/api/Admin/users?q={pname}", token=A)
check("admin tìm người dùng", s == 200 and len(r) == 1, (s, r))
s, r = call("PUT", f"/api/Admin/users/{kid_id}/lock?locked=true", token=A)
check("admin khóa trẻ", s == 204)
s, r = call("GET", "/api/Location/family", token=K)
check("token trẻ bị vô hiệu sau khi khóa", s == 401, (s, r))
s, r = call("PUT", f"/api/Admin/users/{kid_id}/lock?locked=false", token=A)

# --- Dọn dẹp ---
s, r = call("DELETE", f"/api/Children/{kid_id}", token=P)
check("xóa trẻ", s == 204)
s, r = call("DELETE", f"/api/SafeZones/{zid}", token=P)
check("xóa vùng", s == 204)
pid = sql(f"select Id from AspNetUsers where UserName='{pname}'")
s, r = call("DELETE", f"/api/Admin/users/{pid}", token=A)
check("admin xóa phụ huynh", s == 204)

print(f"\n{'ĐẠT TẤT CẢ' if not failures else 'THẤT BẠI: ' + ', '.join(failures)}")
sys.exit(1 if failures else 0)
