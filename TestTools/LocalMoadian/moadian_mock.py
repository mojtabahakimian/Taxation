#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Local mock of the Iranian Moadian (سامانه مودیان) tax API.

Implements the same wire protocol as tax-collect-data SDK 0.0.34:
  POST  self-tsp/sync/GET_SERVER_INFORMATION
  POST  self-tsp/sync/GET_TOKEN
  POST  self-tsp/async/normal-enqueue          (RSA+AES-GCM encrypted packets)
  POST  self-tsp/sync/INQUIRY_BY_REFERENCE_NUMBER
  POST  self-tsp/sync/INQUIRY_BY_UID

Payload crypto (mirrors DefaultEncryptor / PacketCodec):
    aesKey        = 32 random bytes
    symmetricKey  = base64( RSA-OAEP-SHA256( hexUpper(aesKey) ) )
    iv            = hexUpper(16 random bytes)
    data          = base64( AES-GCM( xor(jsonUtf8, aesKey), aesKey, iv, tag=128 ) )

Validation intentionally reproduces the real system's documented behaviour,
including the distinction between error codes (first digit 0) and warning
codes (first digit 1) -- a warning does NOT reject the invoice.

Run:  python moadian_mock.py [--port 9090]
Base URL for the app:  http://127.0.0.1:9090/
"""

import argparse
import base64
import json
import os
import re
import sys
import threading
import time
import uuid
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

# --------------------------------------------------------------------------
# state
# --------------------------------------------------------------------------

LOCK = threading.RLock()
STATE = {
    "invoices": {},       # taxid -> record
    "by_reference": {},   # referenceNumber -> taxid
    "by_uid": {},         # uid -> taxid
    "inno_seen": {},      # (fiscalId, inno) -> taxid
}

KEY_ID = "mock-key-0001"
RSA_KEY = rsa.generate_private_key(public_exponent=65537, key_size=2048)
RSA_PUB_B64 = base64.b64encode(
    RSA_KEY.public_key().public_bytes(
        encoding=serialization.Encoding.DER,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )
).decode()

VERBOSE = True

# آخرین بسته‌ای که رمزگشایی شد -- برای تست قالب JSON روی سیم
LAST_PACKET = {"envelope": None, "packet": None, "invoice": None}

# همه صورتحساب‌های رمزگشایی‌شده این نشست، کلید = taxid
# برای تست‌های golden لازم است تا payload دقیقِ هر فاکتور را بشود بیرون کشید.
PAYLOADS = {}


def log(*a):
    if VERBOSE:
        print("[mock]", *a, flush=True)


def now_ms():
    return int(time.time() * 1000)


# --------------------------------------------------------------------------
# crypto
# --------------------------------------------------------------------------

def xor_blocks(data: bytes, key: bytes) -> bytes:
    """Same algorithm as PacketCodec.Xor -- repeats the shorter array."""
    small, big = (data, key) if len(data) < len(key) else (key, data)
    out = bytearray(len(big))
    blocks = -(-len(big) // len(small))
    for i in range(blocks):
        for j in range(len(small)):
            idx = i * len(small) + j
            if idx >= len(big):
                break
            out[idx] = small[j] ^ big[idx]
    return bytes(out)


def decrypt_packet(packet: dict):
    """Returns the packet's plaintext data (already json-parsed), or None."""
    sym = packet.get("symmetricKey")
    iv_hex = packet.get("iv")
    payload = packet.get("data")

    if not sym or not iv_hex:
        # not encrypted
        return payload

    aes_hex = RSA_KEY.decrypt(
        base64.b64decode(sym),
        padding.OAEP(
            mgf=padding.MGF1(algorithm=hashes.SHA256()),
            algorithm=hashes.SHA256(),
            label=None,
        ),
    ).decode()
    aes_key = bytes.fromhex(aes_hex)
    iv = bytes.fromhex(iv_hex)

    cipher_bytes = base64.b64decode(payload)
    clear_xored = AESGCM(aes_key).decrypt(iv, cipher_bytes, None)
    clear = xor_blocks(clear_xored, aes_key)
    return json.loads(clear.decode("utf-8"))


# --------------------------------------------------------------------------
# validation -- mirrors the documented Moadian rules
# --------------------------------------------------------------------------

ERR = {
    "0300101": "شماره منحصر به فرد مالیاتی تکراری یا نامعتبر است",
    "0300601": "صورتحساب مرجع نامعتبر است",
    "0101104": "شناسه/شماره ملی خریدار با الگوی تعیین شده مطابقت ندارد",
    "0101204": "شماره اقتصادی خریدار با الگوی تعیین شده مطابقت ندارد",
    "0101504": "کد شعبه خریدار با الگوی تعیین شده مطابقت ندارد",
    "0200201": "تاریخ صدور صورتحساب خارج از بازه مجاز است",
    "0300801": "موضوع صورتحساب (ins) نامعتبر است",
    "0300401": "نوع صورتحساب (inty) نامعتبر است",
    "0103705": "مبلغ واحد کالا/خدمت نامعتبر است",
    "0102705": "مجموع صورتحساب با مجموع اقلام همخوانی ندارد",
    "0103605": "تعداد/مقدار کالا/خدمت نامعتبر است",
    "0104805": "مبلغ مالیات بر ارزش افزوده با فرمول همخوانی ندارد",
    "0105305": "مبلغ کل کالا/خدمت با اجزایش همخوانی ندارد",
    "0104005": "مبلغ قبل از تخفیف با تعداد × مبلغ واحد همخوانی ندارد",
    "0102205": "مجموع قبل از تخفیف با اقلام همخوانی ندارد",
    "0102305": "مجموع تخفیفات با اقلام همخوانی ندارد",
    "0102405": "مجموع پس از تخفیف با اقلام همخوانی ندارد",
    "0102505": "مجموع مالیات با اقلام همخوانی ندارد",
    "0200202": "تاریخ صدور صورتحساب ثبت نشده است",
    "0103302": "صورتحساب هیچ قلمی ندارد",
    "0101004": "نوع شخص خریدار نامعتبر است",
    "0103301": "شناسه کالا/خدمت نامعتبر است",
    "0102804": "روش تسویه (setm) نامعتبر است",
    "0102903": "مبلغ پرداختی نقدی (cap) با روش تسویه سازگار نیست",
    "0103003": "مبلغ نسیه (insp) با روش تسویه سازگار نیست",
}
WARN = {
    "1300501": "سریال صورتحساب با صورتحساب مرجع یکسان نیست",
    "1100101": "کد پستی خریدار ثبت نشده است",
}

TAXID_RE = re.compile(r"^[A-Za-z0-9]{22}$")


def _num(v):
    try:
        return float(v or 0)
    except (TypeError, ValueError):
        return 0.0


def _digits(s, n):
    return bool(s) and s.isdigit() and len(s) == n


def validate_invoice(fiscal_id: str, inv: dict):
    """Returns (errors, warnings) as lists of {code, message, errorType}."""
    errors, warnings = [], []
    header = inv.get("header") or {}
    bodies = inv.get("body") or []

    def err(code, extra=""):
        errors.append({"code": code,
                       "message": ERR.get(code, "خطا") + (" " + extra if extra else ""),
                       "errorType": "ERROR"})

    def warn(code, extra=""):
        warnings.append({"code": code,
                         "message": WARN.get(code, "هشدار") + (" " + extra if extra else ""),
                         "errorType": "WARNING"})

    taxid = (header.get("taxid") or "").strip()
    inno = (header.get("inno") or "").strip()
    irtaxid = (header.get("irtaxid") or "").strip()
    ins = header.get("ins")
    inty = header.get("inty")
    inp = header.get("inp")
    tob = header.get("tob")
    tinb = (header.get("tinb") or "").strip()
    bid = (header.get("bid") or "").strip()
    bbc = (header.get("bbc") or "").strip()
    indatim = header.get("indatim") or 0

    # --- شماره منحصر به فرد مالیاتی -------------------------------------
    if not TAXID_RE.match(taxid):
        err("0300101", f"({taxid})")
    elif taxid in STATE["invoices"]:
        err("0300101", "(تکراری)")

    # --- تاریخ ----------------------------------------------------------
    if not indatim:
        err("0200202")
    else:
        age_days = (now_ms() - int(indatim)) / 86400000.0
        if age_days > 12:
            err("0200201", f"(سن صورتحساب: {age_days:.1f} روز)")
        elif age_days < -1:
            err("0200201", "(تاریخ در آینده)")

    # --- خریدار (فقط الگوی اول، و نه صادرات/بورس) -----------------------
    if inty == 1 and inp not in (7, 11):
        if tinb:
            expected = 14 if tob in (1, 4) else 11
            if not _digits(tinb, expected):
                err("0101204", f"({tinb} - انتظار {expected} رقم)")
        elif tob in (1, 4):
            expected_bid = 10 if tob == 1 else 12
            if not _digits(bid, expected_bid):
                err("0101104", f"({bid} - انتظار {expected_bid} رقم)")
            if not _digits((header.get("bpc") or "").strip(), 10):
                warn("1100101")
        else:
            err("0101204", "(ثبت نشده)")

    if inty == 1 and inp not in (7, 11) and tob not in (1, 2, 3, 4):
        err("0101004", f"(tob={tob})")

    if bbc and not _digits(bbc, 4):
        err("0101504", f"({bbc})")

    # --- تراز سرصفحه با اقلام ---------------------------------------------
    # هر پنج جمع کنترل می‌شوند، و شرط «اگر صفر نبود» عمدا برداشته شده:
    # صفر کردن جمع کل در حالی که اقلام غیرصفرند، خودش یک ناسازگاری است.
    if bodies:
        sums = {
            "tsstam": ("tbill", "0102705", "مجموع صورتحساب"),
            "prdis":  ("tprdis", "0102205", "مجموع قبل از تخفیف"),
            "dis":    ("tdis", "0102305", "مجموع تخفیفات"),
            "adis":   ("tadis", "0102405", "مجموع پس از تخفیف"),
            "vam":    ("tvam", "0102505", "مجموع مالیات"),
        }
        for body_field, (head_field, code, label) in sums.items():
            summed = sum(_num(b.get(body_field)) for b in bodies)
            declared = _num(header.get(head_field))
            if abs(declared - summed) > 1:
                err(code, f"({label}: سرصفحه {declared:.0f} / اقلام {summed:.0f})")
                break

    # --- نوع صورتحساب (جدول ۹) و روش تسویه (جدول ۲۴ ص۴۵) ---
    if inty not in (1, 2, 3):
        err("0300401", f"(inty={inty})")

    setm = header.get("setm")
    cap = _num(header.get("cap"))
    insp = _num(header.get("insp"))
    tbill_h = _num(header.get("tbill"))
    tvam_h = _num(header.get("tvam"))
    todam_h = _num(header.get("todam"))
    basis = tbill_h - tvam_h - todam_h

    if setm not in (1, 2, 3):
        err("0102804", f"(setm={setm})")
    elif setm == 1:                                   # نقدی
        if insp > 0:
            err("0103003", "(در تسویه نقدی، مبلغ نسیه باید صفر باشد)")
    elif setm == 2:                                   # نسیه
        if cap > 0:
            err("0102903", "(در تسویه نسیه، مبلغ نقدی باید صفر باشد)")
    elif setm == 3:                                   # نقدی/نسیه
        if cap <= 0:
            err("0102903", "(در تسویه نقدی/نسیه، مبلغ نقدی باید بزرگتر از صفر باشد)")
        elif insp <= 0:
            err("0103003", "(در تسویه نقدی/نسیه، مبلغ نسیه باید بزرگتر از صفر باشد)")
        elif abs((cap + insp) - basis) > 1:
            err("0102903",
                f"(cap+insp={cap + insp:.0f} باید برابر {basis:.0f} = tbill-tvam-todam باشد)")

    # ص۱۷ : صورتحساب ابطالی اقلام بدنه لازم ندارد.
    if not bodies and ins != 3:
        err("0103302")

    for b in bodies:
        sstid = (b.get("sstid") or "").strip()
        if not sstid or not sstid.isdigit() or len(sstid) not in (13, 10, 5):
            err("0103301", f"({sstid})")
            break

    # --- کنترل سطح قلم: مقدار، مبلغ واحد، مالیات، مبلغ کل ---
    for i, b in enumerate(bodies, 1):
        am = _num(b.get("am"))
        fee = _num(b.get("fee"))
        prdis = _num(b.get("prdis"))
        dis = _num(b.get("dis"))
        adis = _num(b.get("adis"))
        vra = _num(b.get("vra"))
        vam = _num(b.get("vam"))
        tsstam = _num(b.get("tsstam"))

        if am <= 0:
            err("0103605", f"(قلم {i}: {am})")
            break
        if fee <= 0:
            err("0103705", f"(قلم {i}: {fee})")
            break
        if abs(prdis - int(am * fee)) > 1:
            err("0104005", f"(قلم {i}: قبل از تخفیف {prdis:.0f} در برابر {int(am * fee)})")
            break
        if abs(adis - (prdis - dis)) > 1:
            err("0105305", f"(قلم {i}: بعد از تخفیف {adis:.0f} در برابر {prdis - dis:.0f})")
            break
        expected_vam = int(adis * vra / 100)          # برش، مثل خود برنامه
        if abs(vam - expected_vam) > 1:
            err("0104805", f"(قلم {i}: {vam:.0f} در برابر {expected_vam})")
            break
        # ص۷۳ جدول ۵۳ ردیف ۱ :  Os = Ks + Is + Ks2 + Ks3
        #   Ks = مالیات بر ارزش افزوده (vam)
        #   Is = مبلغ بعد از تخفیف (adis)
        #   Ks2 = سایر مالیات و عوارض (odam)
        #   Ks3 = سایر وجوه قانونی (olam)
        odam = _num(b.get("odam"))
        olam = _num(b.get("olam"))
        expected_tsstam = adis + vam + odam + olam
        if abs(tsstam - expected_tsstam) > 1:
            err("0105305",
                f"(قلم {i}: مبلغ کل {tsstam:.0f} در برابر {expected_tsstam:.0f}"
                + (f" — عوارض {odam:.0f} و وجوه قانونی {olam:.0f} جا افتاده" if odam or olam else "")
                + ")")
            break

    # --- صورتحساب ارجاعی: اصلاحی / ابطالی / برگشتی ----------------------
    if ins in (2, 3, 4):
        ref = STATE["invoices"].get(irtaxid)
        if not irtaxid:
            err("0300601", "(شماره مالیاتی مرجع ثبت نشده)")
        elif ref is None:
            err("0300601", "(صورتحساب مرجع یافت نشد)")
        else:
            # ص۱۷ بند ۲ : از صورتحساب ابطالی نمی‌توان به عنوان مرجع استفاده کرد.
            if ref["ins"] == 3:
                err("0300601", "(صورتحساب ابطالی نمی‌تواند مرجع باشد)")

            # ص۱۶ بند ۳ : «اگر صورتحساب مرجع *خود اصلاحی/برگشت از فروش باشد*،
            # برای صدور صورتحساب ارجاعی با موضوع اصلاحی/برگشت از فروش، شروط
            # زیر برقرار باشد ...»
            #
            # یعنی شرط وضعیت فقط وقتی اعمال می‌شود که مرجع خودش ۲ یا ۴ باشد.
            # داده واقعی سندباکس همین را تایید می‌کند: اصلاحی روی یک اصلیِ
            # «در انتظار واکنش» پذیرفته شد، ولی اصلاحی روی یک اصلاحیِ
            # «در انتظار واکنش» رد شد.
            if ins in (2, 4) and ref["ins"] in (2, 4) and ref["status"] not in (
                    "CONFIRMED", "SYSTEM_CONFIRMED", "NO_REACTION_NEEDED"):
                err("0300601",
                    f"(مرجع خود اصلاحی/برگشتی است و وضعیتش {ref['status']} — "
                    "باید تایید شده/تایید سیستمی/عدم نیاز به واکنش باشد)")

            if ref["status"] == "CANCELLED":
                err("0300601", "(صورتحساب مرجع قبلا ابطال شده است)")

            # این سخت‌گیری از *سند* نمی‌آید و باید صادقانه برچسب بخورد:
            #   ص۱۵      : تنها قاعدهٔ «فقط یکی»، مخصوص ابطالی است.
            #   ص۱۶ بند۳ : شرط «ارجاعی دیگری صادر نشده باشد» مشروط است به اینکه
            #              مرجع خودش اصلاحی/برگشتی باشد.
            #   ص۳۰ ج۸ ر۴: ترکیب «اصلاحی و برگشت از فروش» را منع می‌کند، نه دو اصلاحی.
            # مبنایش یک مشاهدهٔ سندباکس (ارسال ۲۲:۴۶ روی مرجعِ اصلی) است که دادهٔ
            # آن دیگر در دسترس نیست و قابل راستی‌آزمایی مجدد نبود.
            # پس: احتمالا درست است، ولی اثبات‌نشده. اگر روزی شاهد خلافش پیدا شد،
            # این شرط باید برداشته شود — نه اینکه سند به آن نسبت داده شود.
            live = [t for t, r in STATE["invoices"].items()
                    if r["irtaxid"] == irtaxid and r["ins"] in (2, 4)
                    and r["status"] != "CANCELLED"] if ins in (2, 4) else []
            if live:
                err("0300601", f"(قبلا اصلاحی دیگری روی این مرجع ثبت شده: {live[0]})")

            # هشدار سریال
            if inno and ref["inno"] and inno != ref["inno"]:
                warn("1300501", f"({inno} / مرجع {ref['inno']})")
    elif ins == 1 and irtaxid:
        err("0300601", "(صورتحساب اصلی نباید مرجع داشته باشد)")

    if ins not in (1, 2, 3, 4):
        err("0300801", f"(ins={ins})")

    return errors, warnings


def register_invoice(fiscal_id, inv, uid, reference, errors, warnings):
    header = inv.get("header") or {}
    taxid = (header.get("taxid") or "").strip()
    rec = {
        "taxid": taxid,
        "inno": (header.get("inno") or "").strip(),
        "irtaxid": (header.get("irtaxid") or "").strip(),
        "ins": header.get("ins"),
        "uid": uid,
        "reference": reference,
        "fiscalId": fiscal_id,
        "errors": errors,
        "warnings": warnings,
        "success": not errors,
        "status": "FAILED" if errors else "IN_PROGRESS",
        "confirmationReferenceId": None if errors else uuid.uuid4().hex[:16].upper(),
        "createdAt": datetime.now().isoformat(timespec="seconds"),
    }
    with LOCK:
        STATE["by_reference"][reference] = taxid
        STATE["by_uid"][uid] = taxid
        if not errors:
            STATE["invoices"][taxid] = rec
            # ابطالی، مرجع را باطل می‌کند
            if rec["ins"] == 3 and rec["irtaxid"] in STATE["invoices"]:
                STATE["invoices"][rec["irtaxid"]]["status"] = "CANCELLED"
        else:
            STATE.setdefault("rejected", {})[reference] = rec
    return rec


def find_record(reference=None, uid=None):
    """
    صورتحساب را با کد رهگیری یا uid پیدا می‌کند.

    نکته‌ها:
      • ردشده‌ها هم کد رهگیری و uid دارند و باید پیدا شوند.
      • رکورد موفق فقط وقتی برگردانده می‌شود که همان کلید متعلق به خودش باشد،
        وگرنه ارسال تکراری (شماره مالیاتی یکسان) رکورد قبلی را برمی‌گرداند.
      • فراخواننده معمولا هر دو آرگومان را می‌دهد؛ اگر اولی نتیجه نداد باید
        دومی امتحان شود، نه اینکه فورا None برگردد.
    """
    with LOCK:
        for key, field in ((reference, "reference"), (uid, "uid")):
            if key is None:
                continue

            for r in STATE.get("rejected", {}).values():
                if r.get(field) == key:
                    return r

            index = STATE["by_reference"] if field == "reference" else STATE["by_uid"]
            taxid = index.get(key)
            rec = STATE["invoices"].get(taxid) if taxid else None
            if rec is not None and rec.get(field) == key:
                return rec
        return None


# --------------------------------------------------------------------------
# control helpers (used by the test harness)
# --------------------------------------------------------------------------

def set_status(taxid, status):
    with LOCK:
        rec = STATE["invoices"].get(taxid)
        if not rec:
            return False
        rec["status"] = status
        return True


# --------------------------------------------------------------------------
# HTTP
# --------------------------------------------------------------------------

def sync_response(packet_type, data):
    return {
        "timestamp": now_ms(),
        "result": {
            "uid": str(uuid.uuid4()),
            "packetType": packet_type,
            "data": data,
            "encryptionKeyId": None,
            "symmetricKey": None,
            "iv": None,
        },
        "errors": [],
        "signature": None,
        "signatureKeyId": None,
    }


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        pass

    def handle_one_request(self):
        try:
            super().handle_one_request()
        except (ConnectionResetError, ConnectionAbortedError):
            self.close_connection = True

    def _send(self, obj, code=200):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _read_body(self) -> bytes:
        """Handles both Content-Length and chunked bodies (HttpClient uses
        chunked for JsonContent, which has no known length)."""
        if (self.headers.get("Transfer-Encoding") or "").lower() == "chunked":
            out = bytearray()
            while True:
                line = self.rfile.readline().strip()
                if not line:
                    continue
                size = int(line.split(b";")[0], 16)
                if size == 0:
                    self.rfile.readline()          # trailing CRLF
                    break
                out += self.rfile.read(size)
                self.rfile.read(2)                 # CRLF after each chunk
            return bytes(out)

        n = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(n) if n else b""

    def _read_json(self):
        raw = self._read_body() or b"{}"
        try:
            return json.loads(raw.decode("utf-8"))
        except Exception:
            return {}

    # ---- routes ----------------------------------------------------------

    def do_GET(self):
        route = self.path.replace("\\", "/").strip("/").split("?")[0]
        if route == "__state":
            with LOCK:
                self._send({
                    "invoices": STATE["invoices"],
                    "rejected": STATE.get("rejected", {}),
                })
            return
        if route == "__payloads":
            with LOCK:
                self._send(PAYLOADS)
            return
        if route == "__last":
            with LOCK:
                self._send(LAST_PACKET)
            return
        if route == "__ping":
            self._send({"ok": True})
            return
        self._send({"error": "not found"}, 404)

    def do_POST(self):
        route = self.path.replace("\\", "/").strip("/").split("?")[0]
        route = route.rsplit("api/", 1)[-1]
        body = self._read_json()

        # --- test-control endpoints --------------------------------------
        if route == "__set_status":
            log("set_status request:", body)
            ok = set_status(body.get("taxid"), body.get("status"))
            log("set_status ->", ok)
            self._send({"ok": ok})
            return
        if route == "__drop_next_send":
            # {"mode": "empty"} -> reply with no result and no error
            # {"mode": "close"} -> close the connection without replying
            with LOCK:
                STATE["drop_next_send"] = body.get("mode", "empty")
            self._send({"ok": True})
            return
        if route == "__reset":
            with LOCK:
                STATE.pop("drop_next_send", None)
                STATE["invoices"].clear()
                STATE["by_reference"].clear()
                STATE["by_uid"].clear()
                STATE.pop("rejected", None)
                PAYLOADS.clear()
            self._send({"ok": True})
            return

        # --- protocol endpoints ------------------------------------------
        if route.endswith("GET_SERVER_INFORMATION"):
            self._send(sync_response("GET_SERVER_INFORMATION", {
                "serverTime": now_ms(),
                "publicKeys": [{
                    "key": RSA_PUB_B64,
                    "id": KEY_ID,
                    "algorithm": "RSA",
                    "purpose": 0,
                }],
            }))
            return

        if route.endswith("GET_TOKEN"):
            self._send(sync_response("GET_TOKEN", {
                "token": "mock-token-" + uuid.uuid4().hex,
                "expiresIn": now_ms() + 20 * 60 * 1000,
            }))
            return

        if route.endswith("normal-enqueue"):
            results = []
            for packet in body.get("packets", []):
                uid = packet.get("uid") or str(uuid.uuid4())
                fiscal = packet.get("fiscalId") or ""
                reference = uuid.uuid4().hex[:20].upper()
                try:
                    inv = decrypt_packet(packet)
                    with LOCK:
                        LAST_PACKET["envelope"] = {k: v for k, v in body.items()
                                                   if k != "packets"}
                        LAST_PACKET["packet"] = {k: v for k, v in packet.items()
                                                 if k != "data"}
                        LAST_PACKET["invoice"] = inv
                        try:
                            key = (inv.get("header") or {}).get("taxid")
                            if key:
                                PAYLOADS[key] = inv
                        except Exception:
                            pass
                except Exception as exc:                       # noqa: BLE001
                    log("decrypt failed:", exc)
                    results.append({"uid": uid, "referenceNumber": None,
                                    "errorCode": "0000001",
                                    "errorDetail": "unable to decrypt packet"})
                    continue

                errors, warnings = validate_invoice(fiscal, inv)
                rec = register_invoice(fiscal, inv, uid, reference, errors, warnings)
                log(("REJECT " if errors else "ACCEPT ") + str(rec["taxid"]),
                    "ins=%s" % rec["ins"],
                    "inno=%s" % rec["inno"],
                    ("codes=" + ",".join(e["code"] for e in errors)) if errors else "")
                results.append({"uid": uid, "referenceNumber": reference,
                                "errorCode": None, "errorDetail": None})

            # Lost-response simulation: the invoices above are registered (the real
            # system may have accepted them) but the client never learns it.
            with LOCK:
                drop = STATE.pop("drop_next_send", None)
            if drop == "empty":
                self._send({"timestamp": now_ms(), "result": [], "errors": [],
                            "signature": None, "signatureKeyId": None})
                return
            if drop == "close":
                self.close_connection = True
                return

            self._send({"timestamp": now_ms(), "result": results, "errors": [],
                        "signature": None, "signatureKeyId": None})
            return

        if route.endswith("INQUIRY_BY_REFERENCE_NUMBER") or route.endswith("INQUIRY_BY_UID"):
            try:
                query = decrypt_packet(body.get("packet", {}))
            except Exception:                                   # noqa: BLE001
                query = (body.get("packet") or {}).get("data")

            keys = []
            if isinstance(query, dict):
                keys = query.get("referenceNumber") or query.get("referenceNumbers") or []
            elif isinstance(query, list):
                keys = query
            if isinstance(keys, str):
                keys = [keys]

            out = []
            for k in keys:
                key = k.get("uid") if isinstance(k, dict) else k
                rec = find_record(reference=key, uid=key)
                if rec is None:
                    continue
                out.append({
                    "referenceNumber": rec["reference"],
                    "uid": rec["uid"],
                    "status": rec["status"],
                    "packetType": "INVOICE.V01",
                    "fiscalId": rec["fiscalId"],
                    "data": {
                        "confirmationReferenceId": rec["confirmationReferenceId"],
                        "error": rec["errors"],
                        "warning": rec["warnings"],
                        "success": rec["success"],
                    },
                })
            self._send(sync_response("INQUIRY_BY_REFERENCE_NUMBER", out))
            return

        log("unhandled route:", route)
        self._send({"timestamp": now_ms(), "result": None,
                    "errors": [{"detail": "unknown route " + route,
                                "errorCode": "0000404"}]}, 404)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=9090)
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args()

    global VERBOSE
    VERBOSE = not args.quiet

    class Server(ThreadingHTTPServer):
        # Windows allows two processes to bind the same port when this is on,
        # which silently splits the state between two servers.
        allow_reuse_address = False
        daemon_threads = True

    try:
        srv = Server(("127.0.0.1", args.port), Handler)
    except OSError as exc:
        print("cannot bind port %d -- another mock is probably already running (%s)"
              % (args.port, exc), file=sys.stderr)
        return 1
    print("Moadian mock listening on http://127.0.0.1:%d/" % args.port, flush=True)
    print("  base url for the app:  http://127.0.0.1:%d/" % args.port, flush=True)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main() or 0)
