"""SMTP nội bộ tối giản (chỉ để kiểm thử): nhận thư từ MailKit của server và lưu lại trong MAILS."""
import base64
import re
import socketserver
import threading

MAILS = []  # (người nhận viết thường, nội dung đã giải mã)
CRLF = "\r\n"


class _Smtp(socketserver.StreamRequestHandler):
    def _w(self, text):
        self.wfile.write((text + CRLF).encode())
        self.wfile.flush()

    def handle(self):
        self._w("220 sink ESMTP")
        to, in_data, buf = "", False, []
        while True:
            line = self.rfile.readline()
            if not line:
                return
            t = line.decode(errors="ignore").rstrip(CRLF)
            if in_data:
                if t == ".":
                    in_data = False
                    raw = "\n".join(buf)
                    body = raw
                    if "base64" in raw.lower() and "\n\n" in raw:
                        try:
                            body = base64.b64decode("".join(raw.split("\n\n", 1)[1].split())).decode(errors="ignore")
                        except Exception:
                            pass
                    MAILS.append((to, body))
                    buf = []
                    self._w("250 OK")
                else:
                    buf.append(t)
                continue
            u = t.upper()
            if u.startswith("EHLO"):
                self.wfile.write(("250-sink" + CRLF + "250 AUTH PLAIN LOGIN" + CRLF).encode())
                self.wfile.flush()
            elif u.startswith("AUTH"):
                self._w("235 OK")
            elif u.startswith("RCPT TO"):
                to = re.search(r"<(.+?)>", t).group(1).lower()
                self._w("250 OK")
            elif u == "DATA":
                in_data = True
                self._w("354 go")
            elif u == "QUIT":
                self._w("221 bye")
                return
            else:
                self._w("250 OK")


class _Srv(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def start(port=2525):
    srv = _Srv(("127.0.0.1", port), _Smtp)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    return srv
