#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
سرور ساختگیِ «کامل» سامانه مؤدیان — نسخهٔ دوم، کنار moadian_mock.py

⚠️ هشدار صداقت (CLAUDE.md بخش ۳ و ۱۰) — پیش از هر استفاده بخوانید:
    این سرور را ما از روی *متن اسناد* نوشته‌ایم:
      • RC_TXPS.EC_V02  «کد خطاهای سامانه مودیان» (اسفند ۱۴۰۲)
      • RC_FAQU.LS_V1.15 «سوالات متداول» (دی ۱۴۰۳)            ← برچسب «FAQ x-y»
      • RC_IITP.IS_V7.8 «دستورالعمل صدور صورتحساب الکترونیکی»  ← برچسب «IS_V7.8 pNN»
        (ارجاع‌های «V7.9» از CLAUDE.md نقل شده‌اند و صفحه‌شان را اینجا دوباره نخوانده‌ایم)
    هم‌خوانیِ کد برنامه با این سرور فقط نشان می‌دهد که کد با «خوانشِ ما از اسناد»
    سازگار است — *نه* اینکه سامانهٔ واقعی همین رفتار را دارد. هر جا سند دوپهلو
    بوده، انتخابی کرده‌ایم که در ثابت AMBIGUITIES پایین همین فایل فهرست شده و
    نباید به‌عنوان واقعیت نقل شود. قاعده‌هایی که عیناً در سند نیستند برچسب
    source="derived" دارند. حکم واقعی سامانه فقط در گروه ۲۷ (دادهٔ مروارید) است.

What is new compared with moadian_mock.py (same wire protocol, same crypto —
the crypto/HTTP plumbing is imported from moadian_mock):

  1. Error codes are *built* from the EC_V02 7-digit scheme by `code()`, corrected by
     production TAXDTL data (15,889 rows): kind-2 codes carry detail "01"
     (0200201, not the document's 02002), min-value rules use detail 05, and the
     out-of-pattern warnings (14800, 14029, 14030, ...) seen on every accepted row
     are reproduced. Real-data counts are in each rule's `source`.
  2. Every message carries a `source` tag and a `rule` id; the full rule table is
     in RULES (and served at GET __rules).
  3. A taxpayer-dashboard (کارپوشه) state machine with the official v2
     `inquiry-invoice-status` enum names, a fake clock, buyer actions and the
     30-day system approval.

  4. Fault injection via POST __config (fault_http_500_next, fault_timeout_next +
     fault_timeout_seconds, fault_drop_result_next, fault_duplicate_reference; all
     off by default and after __reset) and GET __stats (requests_by_route,
     packets_per_send_request, faults_fired, registered_despite_fault).

Run:  python3 moadian_mock_full.py [--port 9190] [--quiet]
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
from decimal import Decimal, ROUND_DOWN, InvalidOperation
from http.server import ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import moadian_mock as mm  # noqa: E402  -- crypto, key pair, HTTP plumbing

# ==========================================================================
# configuration
# ==========================================================================

# FAQ 4-10: «از هر شماره مالیاتی مرجع تنها یک بار استفاده می‌شود»
SINGLE_USE_REFERENCE = True
DEADLINE_DAYS = 12                   # FAQ 4-12: از ۱۴۰۳/۰۸/۱۵ حداکثر ۱۲ روز
REACTION_DAYS = 30                   # FAQ 11-2 / 11-6: ۳۰ روز تا تایید سیستمی
AMOUNT_TOLERANCE = Decimal(0)        # FAQ 9-4: برش، صفر رقم اعشار → دقیق
MAX_MESSAGES = 50                    # EC_V02 p14/p16, FAQ 10-9
DAY_MS = 86_400_000

DEFAULT_CONFIG = {
    "deadline_days": DEADLINE_DAYS,
    "single_use_reference": SINGLE_USE_REFERENCE,
    "clock_offset_ms": 0,
    # how many inquiries answer IN_PROGRESS before SUCCESS (0 = first inquiry SUCCESS)
    "in_progress_polls": 0,
    # EC_V02 p16: «در غیر این صورت این آبجکت خالی است» — off by default because the
    # client dereferences data.error unconditionally (see report).
    "empty_data_when_in_progress": False,
    # ---- fault injection (test transport robustness; all verdict-neutral, all off) ----
    # These are NOT behaviours documented for the real system; they model what any
    # HTTP service can do (5xx, slow answer, lost answer). Each counter is
    # decremented as the fault fires; __reset turns everything off.
    # next N protocol requests (normal-enqueue / INQUIRY_*) answer HTTP 500; a send
    # hit by this is NOT registered (the server failed before processing).
    "fault_http_500_next": 0,
    # next N sends are REGISTERED first, then the answer is delayed by
    # fault_timeout_seconds (default 35 > typical 30 s HttpClient timeout). The
    # invoices are recorded in __stats.registered_despite_fault so a test can prove
    # the client must treat the outcome as UNKNOWN, not as "failed".
    "fault_timeout_next": 0,
    "fault_timeout_seconds": 35.0,
    # next N sends are REGISTERED but answer 200 with result: [] (lost result list).
    "fault_drop_result_next": 0,
    # next N sends give every packet of the request the SAME referenceNumber.
    "fault_duplicate_reference": 0,
}
CONFIG = dict(DEFAULT_CONFIG)
FAULT_KEYS = ("fault_http_500_next", "fault_timeout_next", "fault_timeout_seconds",
              "fault_drop_result_next", "fault_duplicate_reference")

VERBOSE = True


def log(*a):
    if VERBOSE:
        print("[mock-full]", *a, flush=True)


def real_now_ms():
    return int(time.time() * 1000)


def server_now_ms():
    """Server clock = real time + controllable fake offset."""
    return real_now_ms() + int(CONFIG["clock_offset_ms"])


# ==========================================================================
# EC_V02 code construction
# ==========================================================================

# vtype (digit 2) — EC_V02 table 7
EXISTENCE, JSON_STRUCT, RULE, MATCHING, OUT_OF_PATTERN, DUPLICATE = 0, 1, 2, 3, 4, 5

# digits 6-7 for JSON-structure errors
D_ADDITIONAL, D_ENUM, D_TYPE, D_REGEX, D_EXCLUSIVE, D_MINMAX, D_DECIMALS = 1, 2, 3, 4, 5, 6, 9
R01 = 1   # kind-2 detail seen on the live system (0200201)


def code(kind_is_warning, vtype, field, detail=None):
    """EC_V02 p16-27: digit1 0/1, digit2 vtype, digits3-5 field, digits6-7 detail.

    detail is present for kind 1 (JSON structure), kind 2 (rule) and kind 3 (matching).

    EC_V02 table 7 and its p15/p33 examples show kind-2 codes with 5 digits
    ("02002"); the LIVE system emits 7 digits with detail "01" ("0200201",
    154 rows in production TAXDTL). We follow the live system (CLAUDE.md §10).
    """
    if vtype in (JSON_STRUCT, RULE, MATCHING):
        if detail is None:
            raise ValueError("detail required for vtype %d" % vtype)
    elif detail is not None:
        raise ValueError("vtype %d has no detail digits" % vtype)
    s = ("1" if kind_is_warning else "0") + str(vtype) + "%03d" % int(field)
    if detail is not None:
        s += "%02d" % int(detail)
    return s


# EC_V02 table 8 (p28-31) + section codes (p32)
FIELDS = {
    "taxid": (1, "شماره منحصر به فرد مالیاتی"),
    "indatim": (2, "تاریخ و زمان صدور صورتحساب (میلادی)"),
    "indati2m": (3, "تاریخ و زمان ایجاد صورتحساب (میلادی)"),
    "inty": (4, "نوع صورتحساب"),
    "inno": (5, "سریال صورتحساب"),
    "irtaxid": (6, "شماره منحصر به فرد مالیاتی صورتحساب مرجع"),
    "inp": (7, "الگوی صورتحساب"),
    "ins": (8, "موضوع صورتحساب"),
    "tins": (9, "شماره اقتصادی فروشنده"),
    "tob": (10, "نوع شخص خریدار"),
    "bid": (11, "شناسه/شماره ملی خریدار"),
    "tinb": (12, "شماره اقتصادی خریدار"),
    "sbc": (13, "کد شعبه فروشنده"),
    "bpc": (14, "کد پستی خریدار"),
    "bbc": (15, "کد شعبه خریدار"),
    "tprdis": (22, "مجموع مبلغ قبل از کسر تخفیف"),
    "tdis": (23, "مجموع تخفیفات"),
    "tadis": (24, "مجموع مبلغ پس از کسر تخفیف"),
    "tvam": (25, "مجموع مالیات بر ارزش افزوده"),
    "todam": (26, "مجموع سایر مالیات، عوارض و وجوه قانونی"),
    "tbill": (27, "مجموع صورتحساب"),
    "setm": (28, "روش تسویه"),
    "cap": (29, "مبلغ پرداختی نقدی"),
    "insp": (30, "مبلغ نسیه"),
    "sstid": (33, "شناسه کالا/خدمت"),
    "sstt": (34, "شرح کالا/خدمت"),
    "mu": (35, "واحد اندازه‌گیری"),
    "am": (36, "تعداد/مقدار"),
    "fee": (37, "مبلغ واحد"),
    "prdis": (41, "مبلغ قبل از تخفیف"),
    "dis": (42, "مبلغ تخفیف"),
    "adis": (43, "مبلغ بعد از تخفیف"),
    "vra": (44, "نرخ مالیات بر ارزش افزوده"),
    "vam": (45, "مبلغ مالیات بر ارزش افزوده"),
    "odam": (48, "مبلغ سایر مالیات و عوارض"),
    "olam": (51, "مبلغ سایر وجوه قانونی"),
    "tsstam": (59, "مبلغ کل کالا/خدمت"),
    "tvop": (31, "مجموع سهم مالیات بر ارزش افزوده از پرداخت"),
    "tax17": (32, "مالیات موضوع ماده ۱۷"),
    "consfee": (52, "اجرت ساخت"),
    "spro": (53, "سود فروشنده"),
    "bros": (54, "حق‌العمل"),
    "tcpbs": (55, "جمع کل اجرت، حق‌العمل و سود"),
    "insr": (107, "قاعده ارسال صورتحساب"),
    "@header": (500, "سرآمد"),
    "@body": (600, "بدنه"),
    "@payment": (700, "اطلاعات پرداخت"),
    "@extension": (800, "سایر اطلاعات تکمیلی (EXTENSION)"),
}

# rial fields — FAQ 9-4: «تمامی مقادیر ریالی به جز فیلد مبلغ واحد ... ۰ رقم اعشار (قطع کردن)»
HEADER_RIAL = ("tprdis", "tdis", "tadis", "tvam", "todam", "tbill", "cap", "insp")
BODY_RIAL = ("prdis", "dis", "adis", "vam", "odam", "olam", "tsstam")
BODY_NUMERIC = ("am", "fee", "vra") + BODY_RIAL


def _template(vtype, detail, label):
    if vtype == EXISTENCE:
        if label == FIELDS["@body"][1]:
            return "صورتحساب باید حداقل یک قلم کالا داشته باشد."
        return "فیلد «%s» خالی است." % label
    if vtype == JSON_STRUCT:
        return {
            D_ADDITIONAL: "در صورتحساب ارسالی فیلد «%s» غیرمجاز است.",
            D_ENUM: "مقدار وارد شده در فیلد «%s» جز مقادیر مجاز نیست.",
            D_TYPE: "نوع مقدار وارد شده در فیلد «%s» صحیح نیست.",
            D_REGEX: "در مقدار وارد شده در فیلد «%s» الگو (regex) رعایت نشده است.",
            D_EXCLUSIVE: "در مقدار وارد شده در فیلد «%s» حد مطلق رعایت نشده است.",
            D_MINMAX: "در مقدار وارد شده در فیلد «%s» حداقل/حداکثر مقدار رعایت نشده است.",
            D_DECIMALS: "حداکثر تعداد رقم اعشار وارد شده در فیلد «%s» صحیح نیست.",
        }.get(detail, "فیلد «%s» از لحاظ ساختار JSON نامعتبر است.") % label
    if vtype == RULE:
        return "مقدار فیلد «%s» از لحاظ قواعد محاسباتی و منطقی معتبر نیست." % label
    if vtype == MATCHING:
        if detail == 2:
            return "فروشنده صورتحساب با فروشنده صورتحساب مرجع منطبق نیست."
        if detail == 3:
            return "فروشنده صورتحساب مجاز به ارسال الگوی صورتحساب ارسالی نمی‌باشد."
        return "مقدار فیلد «%s» با اطلاعات سامانه منطبق نیست." % label
    if vtype == OUT_OF_PATTERN:
        return "فیلد «%s» در الگوی صورتحساب ارسالی خارج از الگو است." % label
    if vtype == DUPLICATE:
        return "فیلد «%s» در صورتحساب ارسالی تکرار شده است." % label
    return "خطا"


# ==========================================================================
# rule registry — the single source for the report table and for messages
# ==========================================================================

RULES = {}


def _rule(rid, warning, vtype, field, detail, source, cond):
    """field=None means the field number is supplied at hit time (generic rule)."""
    RULES[rid] = {
        "id": rid,
        "warning": warning,
        "vtype": vtype,
        "field": field,
        "detail": detail,
        "code": (code(warning, vtype, FIELDS[field][0], detail) if field
                 else ("1" if warning else "0") + str(vtype) + "xxx"
                 + ("%02d" % detail if detail is not None else "")),
        "source": source + ("" if vtype != RULE or "real data" in source
                            else "; 7-digit code format derived from real 0200201"),
        "condition": cond,
    }


# --- generic JSON-structure rules (field chosen at hit time) ---------------
_rule("TYPE", False, JSON_STRUCT, None, D_TYPE, "EC_V02 p19 row5",
      "numeric field holds a non-number (string/bool/object)")
_rule("RIAL_DECIMALS", False, JSON_STRUCT, None, D_DECIMALS, "FAQ 9-4; EC_V02 p23 row11",
      "rial field (all except fee) has a fractional part")
_rule("MISSING", False, EXISTENCE, None, None, "EC_V02 p23 row12",
      "mandatory field is empty")

# --- taxid ------------------------------------------------------------------
_rule("TAXID_FORMAT", False, JSON_STRUCT, "taxid", D_REGEX, "EC_V02 p19 row6; FAQ 10-7 (22 chars)",
      "taxid is not ^[A-Z0-9]{22}$")
_rule("TAXID_DUP", False, MATCHING, "taxid", 1, "EC_V02 p32; FAQ 10-7; real data (0300101 x113, cause not recorded)",
      "taxid already registered in the dashboard")

# --- indatim ----------------------------------------------------------------
_rule("INDATIM_FUTURE", False, RULE, "indatim", R01,
      "real data (0200201 x154 in production); this sub-cause from EC_V02 p33, FAQ 10-10",
      "indatim later than server time (no tolerance)")
_rule("INDATIM_DEADLINE", False, RULE, "indatim", R01,
      "real data (0200201 x154; 52 of them with 00107); EC_V02 p33; FAQ 4-12",
      "server time - indatim > deadline_days (default 12) and insr != 1")
_rule("INSR_MISSING", False, EXISTENCE, "insr", None,
      "real data (00107 x52, always together with 0200201)",
      "out-of-deadline invoice with insr empty (emitted together with INDATIM_DEADLINE)")
_rule("INDATIM_BEFORE_REF", False, RULE, "indatim", R01,
      "real data (0200201 x154 in production); this sub-cause from EC_V02 p33, FAQ 10-10",
      "referral (ins 2/3/4) indatim earlier than reference indatim")

# --- header enums -----------------------------------------------------------
_rule("INTY_ENUM", False, JSON_STRUCT, "inty", D_ENUM, "EC_V02 p18 row4",
      "inty not in 1..3")
_rule("INS_ENUM", False, JSON_STRUCT, "ins", D_ENUM, "EC_V02 p18 row4",
      "ins not in 1..4")
_rule("INS1_IRTAXID", True, OUT_OF_PATTERN, "irtaxid", None,
      "EC_V02 p27 row20 says kind 4 is a WARNING; applying it to ins=1+irtaxid is derived",
      "original invoice (ins=1) carries an irtaxid")
_rule("SETM_ENUM", False, JSON_STRUCT, "setm", D_ENUM, "EC_V02 p18 row4",
      "setm not in 1..3")

# --- buyer ------------------------------------------------------------------
_rule("TOB_ENUM", False, JSON_STRUCT, "tob", D_ENUM, "EC_V02 p18 row4",
      "tob not in 1..4")
_rule("TINB_MISSING", False, EXISTENCE, "tinb", None,
      "EC_V02 p23 row12; real data (Morvarid 65, production 1)",
      "type 1, tob 2/3 (or tob 1/4 with neither tinb nor bid): tinb empty")
_rule("TINB_REGEX", False, JSON_STRUCT, "tinb", D_REGEX,
      "EC_V02 p19 row6; real data (Morvarid 42; production 273 x ^\\d{14}$ + 32 x ^\\d{11}$)",
      "tinb not 11 digits (tob 2/3) / not 14 digits (tob 1/4)")
_rule("BID_REGEX", False, JSON_STRUCT, "bid", D_REGEX,
      "EC_V02 p19 row6 (lengths 10/11/12 derived)",
      "bid not 10 digits (tob 1) / 12 digits (tob 4) / 11 digits (tob 2, type 2 only)")
_rule("BPC_MISSING", True, JSON_STRUCT, "bpc", D_ADDITIONAL, "derived",
      "type 1, tob 1/4 identified by bid: bpc missing (warning)")
_rule("BPC_REGEX", False, JSON_STRUCT, "bpc", D_REGEX, "EC_V02 p19 row6 (length 10 derived)",
      "bpc present but not 10 digits")
_rule("BBC_REGEX", False, JSON_STRUCT, "bbc", D_REGEX, "real data (0101504 x36, regex ^\\d{4}$)",
      "bbc present but not 4 digits")

# --- body -------------------------------------------------------------------
_rule("BODY_EMPTY", False, EXISTENCE, "@body", None, "EC_V02 p24 row13",
      "ins != 3 and no body items")
_rule("SSTID_REGEX", False, JSON_STRUCT, "sstid", D_REGEX, "EC_V02 p19 row6 (13 digits)",
      "sstid not 13 digits")
_rule("AM_MIN", False, JSON_STRUCT, "am", D_EXCLUSIVE, "real data (0103605 x9, exclusiveMinimum > 0)",
      "am <= 0")
_rule("FEE_MIN", False, JSON_STRUCT, "fee", D_EXCLUSIVE, "real data (0103705 x58, exclusiveMinimum > 0)",
      "fee <= 0")
_rule("PRDIS_MIN", False, JSON_STRUCT, "prdis", D_EXCLUSIVE, "real data (0104105 x58, exclusiveMinimum > 0)",
      "prdis <= 0")
_rule("TPRDIS_MIN", False, JSON_STRUCT, "tprdis", D_EXCLUSIVE, "real data (0102205 x2, exclusiveMinimum > 0)",
      "tprdis <= 0")
_rule("PRDIS", False, RULE, "prdis", R01, "FAQ 9-4 (truncate)",
      "prdis != trunc(am*fee)")
_rule("ADIS", False, RULE, "adis", R01, "IS_V7.8 field definitions (page not re-verified)",
      "adis != prdis - dis")
_rule("VAM", False, RULE, "vam", R01, "FAQ 9-4 (truncate)",
      "vam != trunc(adis*vra/100)")
_rule("TSSTAM", False, RULE, "tsstam", R01, "V7.9 p73 table 53 row 1 (per CLAUDE.md)",
      "tsstam != adis + vam + odam + olam")

# --- header sums ------------------------------------------------------------
for _f, _b in (("tprdis", "prdis"), ("tdis", "dis"), ("tadis", "adis"), ("tvam", "vam")):
    _rule("SUM_" + _f.upper(), False, RULE, _f, R01, "IS_V7.8 field definitions (page not re-verified)",
          "%s != sum(%s)" % (_f, _b))
_rule("SUM_TODAM", False, RULE, "todam", R01, "IS_V7.8 field definitions (page not re-verified)",
      "todam != sum(odam) + sum(olam)")
_rule("SUM_TBILL", False, RULE, "tbill", R01, "IS_V7.8 field definitions (page not re-verified)",
      "tbill != sum(tsstam)")

# --- settlement -------------------------------------------------------------
_rule("CAPINSP_SUM", False, RULE, "cap", R01, "FAQ 10-5/10-6; V7.9 p46-47 (per CLAUDE.md)",
      "setm=3: cap + insp != tbill - tvam - todam")

# --- referrals --------------------------------------------------------------
_rule("REF_NOT_FOUND", False, MATCHING, "irtaxid", 1, "EC_V02 p33; FAQ 10-12; real data (0300601 x42, sub-cause not recorded)",
      "irtaxid not an accepted invoice in the dashboard")
_rule("REF_IS_CANCELLATION", False, MATCHING, "irtaxid", 1, "IS_V7.8 p16 item 2; FAQ 4-11",
      "reference is itself a cancellation (ins=3)")
_rule("REF_CANCELED", False, MATCHING, "irtaxid", 1, "EC_V02 p33; FAQ 4-9(b)",
      "reference dashboard status is CANCELED")
_rule("REF_REFERRAL_STATUS", False, MATCHING, "irtaxid", 1,
      "FAQ 4-3 note 3; IS_V7.8 p15 item 4; EC_V02 p33",
      "new ins 2/4 on a reference that is itself ins 2/4 and not in "
      "APPROVED/SYSTEMIC_APPROVED/NO_NEED_REACTION/IMPOSSIBLE_REACTION")
_rule("REF_SINGLE_USE", False, MATCHING, "irtaxid", 1, "FAQ 4-10",
      "new ins 2/4 while another live referral (accepted, not REJECTED/CANCELED) "
      "already uses this irtaxid [switch: single_use_reference]")
_rule("CANCEL_TARGET_STATUS", False, MATCHING, "irtaxid", 1,
      "FAQ 4-9; IS_V7.8 p16 item 1 (role interpretation, see AMBIGUITIES)",
      "cancellation of an ins 2/4 invoice whose status is REJECTED")
_rule("CANCEL_CONSUMED", False, MATCHING, "irtaxid", 1, "FAQ 4-9(a)(b); IS_V7.8 p16 item 1",
      "cancellation of a reference that has a referral outside AWAITING_REACTION/REJECTED")
_rule("SELLER_MISMATCH", False, MATCHING, "tins", 2, "EC_V02 p25 row16",
      "tins differs from the reference's tins")
_rule("INTY_MISMATCH", False, MATCHING, "inty", 1, "FAQ 4-3/4-4/4-5",
      "ins 2/4: inty differs from reference")
_rule("INP_MISMATCH", False, MATCHING, "inp", 1, "FAQ 4-3/4-4/4-5",
      "ins 2/4: inp differs from reference")
_rule("TOB_MISMATCH", False, MATCHING, "tob", 1, "FAQ 4-3 note 1; FAQ 4-4 note 1",
      "ins 2/4: tob differs from reference")
_rule("TINB_MISMATCH", False, MATCHING, "tinb", 1, "FAQ 4-3 note 1; FAQ 4-4 note 1; EC_V02 p33",
      "ins 2/4: tinb differs from reference")
_rule("BID_MISMATCH", False, MATCHING, "bid", 1, "FAQ 4-3 note 1; FAQ 4-4 note 1",
      "ins 2/4: bid differs from reference")
_rule("CORR_NEW_SSTID", False, MATCHING, "sstid", 1, "FAQ 4-6; IS_V7.8 p15 item 1/2a",
      "ins 2: an sstid not present in the reference")
_rule("CORR_VRA", False, MATCHING, "vra", 1, "IS_V7.8 p15 item 1",
      "ins 2: vra of a matched item differs from the reference")
_rule("RET_NEW_SSTID", False, MATCHING, "sstid", 1, "derived (FAQ 4-4: sold items minus returned)",
      "ins 4: an item not present in the reference")
_rule("RET_AM_UP", False, MATCHING, "am", 1, "FAQ 4-4 note 2",
      "ins 4: an item's am greater than the reference's")
_rule("RET_NO_DECREASE", False, MATCHING, "am", 1, "FAQ 4-4 note 2",
      "ins 4: no item decreased (and none removed)")
_rule("RET_ALL", False, MATCHING, "am", 2, "FAQ 4-4 note 4 (detail digits 02 derived)",
      "ins 4: every quantity is 0 / all items removed -> must issue a cancellation")
_rule("RET_VRA", False, MATCHING, "vra", 1, "FAQ 4-4 note 3; EC_V02 p33",
      "ins 4: vra changed")
_rule("RET_FEE", False, MATCHING, "fee", 1, "FAQ 4-4 note 5",
      "ins 4: fee changed")
_rule("INNO_SERIAL", True, MATCHING, "inno", 1,
      "real data (1300501 on 14,487 of 14,641 accepted rows, originals included); "
      "cause INFERRED: inno vs the 10-hex serial inside taxid",
      "inno (case-insensitive) != taxid[11:21], the serial part of the taxid (warning, accepted)")

# --- out-of-pattern warnings (kind 4): verdict-neutral ------------------------
_rule("OOP_EXTENSION", True, OUT_OF_PATTERN, "@extension", None,
      "real data (14800 on 100% of 14,641 accepted rows; client sends extension=[{}])",
      "extension list present and non-empty")
_rule("OOP_CAP", True, OUT_OF_PATTERN, "cap", None, "real data (14029 x14,505)",
      "cap present (even 0) and setm != 3")
_rule("OOP_INSP", True, OUT_OF_PATTERN, "insp", None, "real data (14030 x14,487)",
      "insp present (even 0) and setm != 3")
_rule("OOP_INDATI2M", True, OUT_OF_PATTERN, "indati2m", None,
      "real data (14003 x9,009 + x5,496, two label variants); condition 'insr != 1' inferred",
      "indati2m present and insr != 1")
for _f in ("consfee", "spro", "bros", "tcpbs"):
    _rule("OOP_" + _f.upper(), True, OUT_OF_PATTERN, _f, None,
          "real data (14%03d per line); condition 'inp != 3' inferred (gold pattern)" % FIELDS[_f][0],
          "%s present in a body item and inp != 3" % _f)
_rule("OOP_CANCEL_HEADER", True, OUT_OF_PATTERN, None, None,
      "real data (14022-14027, 14032 x537 each); attribution to ins=3 INFERRED",
      "ins=3 carries tprdis/tdis/tadis/tvam/todam/tbill/tax17 (header is fetched from reference)")

OVERFLOW_CODE = "00000"
OVERFLOW_SOURCE = "EC_V02 p16; FAQ 10-9"


# ==========================================================================
# state
# ==========================================================================

LOCK = threading.RLock()
STATE = {
    "invoices": {},       # taxid -> record (accepted only = the dashboard)
    "rejected": {},       # referenceNumber -> record
    "by_reference": {},   # referenceNumber -> taxid
    "by_uid": {},         # uid -> taxid
}
PAYLOADS = {}
LAST_PACKET = {"envelope": None, "packet": None, "invoice": None}
DUP_REFS = {}         # referenceNumber -> [taxid, ...] (fault_duplicate_reference)


def _new_stats():
    return {"requests_by_route": {}, "packets_per_send_request": [], "faults_fired": {},
            # invoices the server registered although the client got no usable answer
            # (timeout / dropped result): {taxid, reference, uid, fault}
            "registered_despite_fault": []}


STATS = _new_stats()


def _count_route(route):
    with LOCK:
        STATS["requests_by_route"][route] = STATS["requests_by_route"].get(route, 0) + 1


def take_fault(name):
    """Consume one shot of a counted fault. Returns True if it fires now."""
    with LOCK:
        n = int(CONFIG.get(name) or 0)
        if n <= 0:
            return False
        CONFIG[name] = n - 1
        STATS["faults_fired"][name] = STATS["faults_fired"].get(name, 0) + 1
        return True

# dashboard enum — v2 `inquiry-invoice-status`
AWAITING = "AWAITING_REACTION"
APPROVED = "APPROVED"
REJECTED = "REJECTED"
SYSTEMIC = "SYSTEMIC_APPROVED"
NO_NEED = "NO_NEED_REACTION"
IMPOSSIBLE = "IMPOSSIBLE_REACTION"
CANCELED = "CANCELED"
DASHBOARD_STATUSES = (AWAITING, APPROVED, REJECTED, SYSTEMIC, NO_NEED, IMPOSSIBLE, CANCELED)

# statuses in which an ins 2/4 invoice may serve as a reference (FAQ 4-3 note 3)
REFERENCE_OK = (APPROVED, SYSTEMIC, NO_NEED, IMPOSSIBLE)
# a referral in one of these is "final": its reference is deemed cancelled (FAQ 11-6, 4-9b)
CONSUMING = (APPROVED, SYSTEMIC, NO_NEED, IMPOSSIBLE)

LEGACY_STATUS = {
    "CONFIRMED": APPROVED,
    "SYSTEM_CONFIRMED": SYSTEMIC,
    "NO_REACTION_NEEDED": NO_NEED,
    "CANCELLED": CANCELED,
}
PROCESSING_STATUSES = ("SUCCESS", "FAILED", "IN_PROGRESS", "TIMEOUT")


def reset_state():
    with LOCK:
        for k in ("invoices", "rejected", "by_reference", "by_uid"):
            STATE[k].clear()
        PAYLOADS.clear()
        DUP_REFS.clear()
        STATS.clear()
        STATS.update(_new_stats())
        LAST_PACKET.update({"envelope": None, "packet": None, "invoice": None})
        CONFIG.clear()
        CONFIG.update(DEFAULT_CONFIG)
        CONFIG["single_use_reference"] = SINGLE_USE_REFERENCE
        CONFIG["deadline_days"] = DEADLINE_DAYS


# ==========================================================================
# value helpers
# ==========================================================================

BAD = object()      # sentinel: value present but of the wrong type


def _s(v):
    """Normalised string (None -> '')."""
    if v is None:
        return ""
    return str(v).strip()


def _int(v):
    if isinstance(v, bool) or v is None:
        return None
    if isinstance(v, int):
        return v
    if isinstance(v, Decimal) and v == v.to_integral_value():
        return int(v)
    return None


def trunc(x):
    """FAQ 9-4: rial values truncated to 0 decimals («روش قطع کردن»)."""
    return x.to_integral_value(rounding=ROUND_DOWN)


def _digits(s, *lengths):
    return bool(s) and s.isdigit() and len(s) in lengths


# ==========================================================================
# validation
# ==========================================================================

class Ctx:
    def __init__(self):
        self.errors, self.warnings, self.notes = [], [], []

    def hit(self, rid, extra="", field=None, line=None):
        r = RULES[rid]
        fname = field or r["field"]
        fno, label = FIELDS[fname]
        c = code(r["warning"], r["vtype"], fno, r["detail"])
        msg = _template(r["vtype"], r["detail"], label)
        if line is not None:
            extra = ("قلم %d" % line) + ((": " + extra) if extra else "")
        m = {
            "code": c,
            "message": msg + ((" (" + extra + ")") if extra else ""),
            "errorType": "WARNING" if r["warning"] else "ERROR",
            "rule": rid,
            "source": r["source"],
        }
        (self.warnings if r["warning"] else self.errors).append(m)
        return m

    def rules_hit(self):
        return [m["rule"] for m in self.errors + self.warnings]


def _num(ctx, obj, key, line=None):
    """Decimal, None (absent) or BAD (wrong type -> TYPE error recorded)."""
    v = obj.get(key)
    if v is None:
        return None
    if isinstance(v, bool) or not isinstance(v, (int, Decimal, float)):
        ctx.hit("TYPE", "%s=%r" % (key, v), field=key, line=line)
        return BAD
    try:
        return Decimal(str(v)) if isinstance(v, float) else Decimal(v)
    except InvalidOperation:
        ctx.hit("TYPE", "%s=%r" % (key, v), field=key, line=line)
        return BAD


def _z(v):
    """None -> 0 for arithmetic; BAD stays BAD."""
    return Decimal(0) if v is None else v


def _ok(*vals):
    return all(v is not BAD for v in vals)


def _neq(a, b):
    return abs(a - b) > AMOUNT_TOLERANCE


def check_taxid(ctx, h):
    taxid = _s(h.get("taxid"))
    if not taxid:
        ctx.hit("MISSING", field="taxid")
    elif not re.fullmatch(r"[A-Z0-9]{22}", taxid):
        ctx.hit("TAXID_FORMAT", taxid)
    elif taxid in STATE["invoices"]:
        ctx.hit("TAXID_DUP", "تکراری: " + taxid)


def check_indatim(ctx, h, now):
    v = h.get("indatim")
    if v is None or v == "" or v == 0:
        ctx.hit("MISSING", field="indatim")
        return None
    ms = _int(v)
    if ms is None:
        ctx.hit("TYPE", "indatim=%r" % (v,), field="indatim")
        return None
    if ms > now:                                     # no tolerance
        ctx.hit("INDATIM_FUTURE", "%d ms جلوتر از زمان سرور" % (ms - now))
    elif now - ms > int(CONFIG["deadline_days"]) * DAY_MS:
        insr = h.get("insr")
        if _int(insr) != 1:          # insr=1: article-9 path, late sending declared
            ctx.hit("INDATIM_DEADLINE", "سن صورتحساب %.2f روز، مهلت %s روز"
                    % ((now - ms) / DAY_MS, CONFIG["deadline_days"]))
            if insr is None or _s(insr) == "":
                ctx.hit("INSR_MISSING")
    return ms


def check_enums(ctx, h):
    ins = h.get("ins")
    if ins is None:
        ctx.hit("MISSING", field="ins")
    elif _int(ins) not in (1, 2, 3, 4):
        ctx.hit("INS_ENUM", "ins=%r" % (ins,))
    elif ins == 1 and _s(h.get("irtaxid")):
        ctx.hit("INS1_IRTAXID", _s(h.get("irtaxid")))

    if not _s(h.get("tins")):
        ctx.hit("MISSING", field="tins")          # FAQ 4-7: required even for ins=3

    if ins == 3:
        return                                    # type/pattern fetched from reference

    inty = h.get("inty")
    if inty is None:
        ctx.hit("MISSING", field="inty")
    elif _int(inty) not in (1, 2, 3):
        ctx.hit("INTY_ENUM", "inty=%r" % (inty,))


def check_buyer(ctx, h):
    if h.get("ins") == 3:
        return
    inty, inp = _int(h.get("inty")), _int(h.get("inp"))
    tob_raw = h.get("tob")
    tob = _int(tob_raw)
    tinb, bid, bpc, bbc = (_s(h.get(k)) for k in ("tinb", "bid", "bpc", "bbc"))

    if bbc and not _digits(bbc, 4):
        ctx.hit("BBC_REGEX", bbc)

    strict = inty == 1 and inp not in (7, 11)
    if strict:
        if tob_raw is None:
            ctx.hit("MISSING", field="tob")
            return
        if tob not in (1, 2, 3, 4):
            ctx.hit("TOB_ENUM", "tob=%r" % (tob_raw,))
            return
        if tob in (2, 3):
            if not tinb:
                ctx.hit("TINB_MISSING")
            elif not _digits(tinb, 11):
                ctx.hit("TINB_REGEX", "%s — انتظار ۱۱ رقم" % tinb)
            return
        # tob 1 / 4
        if tinb:
            if not _digits(tinb, 14):
                ctx.hit("TINB_REGEX", "%s — انتظار ۱۴ رقم" % tinb)
        elif bid:
            n = 10 if tob == 1 else 12
            if not _digits(bid, n):
                ctx.hit("BID_REGEX", "%s — انتظار %d رقم" % (bid, n))
            if not bpc:
                ctx.hit("BPC_MISSING")
        else:
            ctx.hit("TINB_MISSING", "نه شماره اقتصادی و نه شناسه ملی")
        if bpc and not _digits(bpc, 10):
            ctx.hit("BPC_REGEX", bpc)
        return

    # FAQ 9-6: type 2 (and, derived, type 1 export/bourse): optional but well-formed
    if tob_raw is not None and tob not in (1, 2, 3, 4):
        ctx.hit("TOB_ENUM", "tob=%r" % (tob_raw,))
        tob = None
    if tinb:
        allowed = {1: (14,), 4: (14,), 2: (11,), 3: (11,)}.get(tob, (11, 14))
        if not _digits(tinb, *allowed):
            ctx.hit("TINB_REGEX", "%s — نوع دوم، اختیاری ولی باید معتبر باشد" % tinb)
    if bid:
        allowed = {1: (10,), 2: (11,), 4: (12,)}.get(tob, (10, 11, 12))
        if not _digits(bid, *allowed):
            ctx.hit("BID_REGEX", "%s — نوع دوم، اختیاری ولی باید معتبر باشد" % bid)
    if bpc and not _digits(bpc, 10):
        ctx.hit("BPC_REGEX", bpc)


def check_body(ctx, h, bodies):
    """Line math + header sums. Returns the parsed items (for referral checks)."""
    items = []
    if h.get("ins") == 3:
        return items                      # IS_V7.8 p16: body fetched from reference
    if not bodies:
        ctx.hit("BODY_EMPTY")
        return items

    sums = {k: Decimal(0) for k in BODY_RIAL}
    sums_ok = True
    for i, b in enumerate(bodies, 1):
        if not isinstance(b, dict):
            ctx.hit("TYPE", "قلم %d" % i, field="@body")
            sums_ok = False
            continue
        sstid = _s(b.get("sstid"))
        if not sstid:
            ctx.hit("MISSING", field="sstid", line=i)
        elif not _digits(sstid, 13):
            ctx.hit("SSTID_REGEX", sstid, line=i)

        v = {k: _num(ctx, b, k, line=i) for k in BODY_NUMERIC}
        items.append({"sstid": sstid, "am": v["am"], "fee": v["fee"], "vra": v["vra"]})

        for k in BODY_RIAL:
            if v[k] not in (None, BAD) and v[k] != trunc(v[k]):
                ctx.hit("RIAL_DECIMALS", "%s=%s" % (k, v[k]), field=k, line=i)

        am, fee = v["am"], v["fee"]
        if am is None:
            ctx.hit("MISSING", field="am", line=i)
        elif am is not BAD and am <= 0:
            ctx.hit("AM_MIN", str(am), line=i)
        if fee is None:
            ctx.hit("MISSING", field="fee", line=i)
        elif fee is not BAD and fee <= 0:
            ctx.hit("FEE_MIN", str(fee), line=i)
        if v["prdis"] not in (None, BAD) and v["prdis"] <= 0:
            ctx.hit("PRDIS_MIN", str(v["prdis"]), line=i)

        prdis, dis, adis = _z(v["prdis"]), _z(v["dis"]), _z(v["adis"])
        vra, vam, odam, olam = _z(v["vra"]), _z(v["vam"]), _z(v["odam"]), _z(v["olam"])
        tsstam = _z(v["tsstam"])

        if _ok(am, fee, prdis) and am is not None and fee is not None:
            exp = trunc(am * fee)
            if _neq(prdis, exp):
                ctx.hit("PRDIS", "%s در برابر trunc(%s×%s)=%s" % (prdis, am, fee, exp), line=i)
        if _ok(prdis, dis, adis) and _neq(adis, prdis - dis):
            ctx.hit("ADIS", "%s در برابر %s" % (adis, prdis - dis), line=i)
        if _ok(adis, vra, vam):
            exp = trunc(adis * vra / 100)
            if _neq(vam, exp):
                ctx.hit("VAM", "%s در برابر trunc(%s×%s/100)=%s" % (vam, adis, vra, exp), line=i)
        if _ok(adis, vam, odam, olam, tsstam):
            exp = adis + vam + odam + olam
            if _neq(tsstam, exp):
                ctx.hit("TSSTAM", "%s در برابر %s" % (tsstam, exp), line=i)

        for k in BODY_RIAL:
            if v[k] is BAD:
                sums_ok = False
            else:
                sums[k] += _z(v[k])

    hv = {k: _num(ctx, h, k) for k in HEADER_RIAL}
    if hv["tprdis"] not in (None, BAD) and hv["tprdis"] <= 0:
        ctx.hit("TPRDIS_MIN", str(hv["tprdis"]))
    for k in HEADER_RIAL:
        if hv[k] not in (None, BAD) and hv[k] != trunc(hv[k]):
            ctx.hit("RIAL_DECIMALS", "%s=%s" % (k, hv[k]), field=k)

    if sums_ok:
        pairs = (("tprdis", sums["prdis"], "SUM_TPRDIS"),
                 ("tdis", sums["dis"], "SUM_TDIS"),
                 ("tadis", sums["adis"], "SUM_TADIS"),
                 ("tvam", sums["vam"], "SUM_TVAM"),
                 ("todam", sums["odam"] + sums["olam"], "SUM_TODAM"),
                 ("tbill", sums["tsstam"], "SUM_TBILL"))
        for hk, expected, rid in pairs:
            if hv[hk] is BAD:
                continue
            if _neq(_z(hv[hk]), expected):
                ctx.hit(rid, "سرآمد %s / اقلام %s" % (_z(hv[hk]), expected))
    return items


def check_settlement(ctx, h):
    if h.get("ins") == 3:
        return
    setm_raw = h.get("setm")
    setm = _int(setm_raw)
    if setm_raw is None:
        ctx.hit("MISSING", field="setm")
        return
    if setm not in (1, 2, 3):
        ctx.hit("SETM_ENUM", "setm=%r" % (setm_raw,))
        return
    if _int(h.get("inty")) == 2 and setm != 1:
        # FAQ 3-2 says type-2 «کل مبلغ صورتحساب نقدی تلقی می‌شود» — not a validation
        # rule anywhere we could find, so we accept and only record it.
        ctx.notes.append("inty=2 with setm=%d accepted (FAQ 3-2 treats type 2 as cash; "
                         "no rejection rule found)" % setm)

    probe = Ctx()          # type errors on these were already reported by check_body
    cap, insp = _num(probe, h, "cap"), _num(probe, h, "insp")
    tbill, tvam, todam = (_num(probe, h, k) for k in ("tbill", "tvam", "todam"))
    if not _ok(cap, insp, tbill, tvam, todam):
        return
    if setm == 3:
        if cap is None:
            ctx.hit("MISSING", field="cap")
        if insp is None:
            ctx.hit("MISSING", field="insp")
        if cap is not None and insp is not None:
            basis = _z(tbill) - _z(tvam) - _z(todam)
            if _neq(cap + insp, basis):
                ctx.hit("CAPINSP_SUM", "cap+insp=%s، tbill-tvam-todam=%s" % (cap + insp, basis))
    # setm 1/2: cap/insp are out of pattern on the live system (14029/14030,
    # warnings) — not validated. See check_out_of_pattern.


def _live_referrals(irtaxid, exclude_taxid=None):
    return [r for r in STATE["invoices"].values()
            if r["irtaxid"] == irtaxid and r["ins"] in (2, 3, 4)
            and r["taxid"] != exclude_taxid
            and r["invoiceStatus"] not in (REJECTED, CANCELED)]


def _match_items(new_items, ref_items):
    """Pair items by sstid, k-th occurrence with k-th occurrence (see AMBIGUITIES)."""
    pools = {}
    for it in ref_items:
        pools.setdefault(it["sstid"], []).append(it)
    used = {k: 0 for k in pools}
    pairs, unmatched = [], []
    for idx, it in enumerate(new_items, 1):
        pool = pools.get(it["sstid"])
        if pool and used[it["sstid"]] < len(pool):
            pairs.append((idx, it, pool[used[it["sstid"]]]))
            used[it["sstid"]] += 1
        else:
            unmatched.append((idx, it))
    removed = sum(len(pool) - used[k] for k, pool in pools.items())
    return pairs, unmatched, removed


def check_reference(ctx, h, items, indatim_ms):
    ins = h.get("ins")
    if ins not in (2, 3, 4):
        return None
    irtaxid = _s(h.get("irtaxid"))
    if not irtaxid:
        ctx.hit("MISSING", field="irtaxid")
        return None
    ref = STATE["invoices"].get(irtaxid)
    if ref is None:
        ctx.hit("REF_NOT_FOUND", irtaxid)
        return None

    if ref["ins"] == 3:
        ctx.hit("REF_IS_CANCELLATION", irtaxid)
    elif ref["invoiceStatus"] == CANCELED:
        ctx.hit("REF_CANCELED", irtaxid)
    elif ins in (2, 4):
        if ref["ins"] in (2, 4) and ref["invoiceStatus"] not in REFERENCE_OK:
            ctx.hit("REF_REFERRAL_STATUS", "مرجع ins=%s با وضعیت %s"
                    % (ref["ins"], ref["invoiceStatus"]))
        if CONFIG["single_use_reference"]:
            live = _live_referrals(irtaxid)
            if live:
                ctx.hit("REF_SINGLE_USE", "ارجاعی زندهٔ قبلی: %s (%s)"
                        % (live[0]["taxid"], live[0]["invoiceStatus"]))
    else:  # ins == 3
        if ref["ins"] in (2, 4) and ref["invoiceStatus"] not in (AWAITING,) + REFERENCE_OK:
            ctx.hit("CANCEL_TARGET_STATUS", "ارجاعی با وضعیت %s" % ref["invoiceStatus"])
        blocking = [r for r in STATE["invoices"].values()
                    if r["irtaxid"] == irtaxid and r["ins"] in (2, 4)
                    and r["invoiceStatus"] not in (AWAITING, REJECTED, CANCELED)]
        if blocking:
            ctx.hit("CANCEL_CONSUMED", "ارجاعی %s با وضعیت %s"
                    % (blocking[0]["taxid"], blocking[0]["invoiceStatus"]))

    if _s(h.get("tins")) and _s(h.get("tins")) != ref["tins"]:
        ctx.hit("SELLER_MISMATCH", "%s / مرجع %s" % (_s(h.get("tins")), ref["tins"]))

    if indatim_ms is not None and ref["indatim"] is not None and indatim_ms < ref["indatim"]:
        ctx.hit("INDATIM_BEFORE_REF", "%d ms پیش از مرجع" % (ref["indatim"] - indatim_ms))

    if ins == 3:
        return ref

    # --- ins 2 / 4: type, pattern, buyer unchanged ---------------------------
    if _int(h.get("inty")) != ref["inty"]:
        ctx.hit("INTY_MISMATCH", "%r / مرجع %r" % (h.get("inty"), ref["inty"]))
    if _int(h.get("inp")) != ref["inp"]:
        ctx.hit("INP_MISMATCH", "%r / مرجع %r" % (h.get("inp"), ref["inp"]))
    if _int(h.get("tob")) != ref["tob"]:
        ctx.hit("TOB_MISMATCH", "%r / مرجع %r" % (h.get("tob"), ref["tob"]))
    if _s(h.get("tinb")) != ref["tinb"]:
        ctx.hit("TINB_MISMATCH", "%r / مرجع %r" % (_s(h.get("tinb")), ref["tinb"]))
    if _s(h.get("bid")) != ref["bid"]:
        ctx.hit("BID_MISMATCH", "%r / مرجع %r" % (_s(h.get("bid")), ref["bid"]))

    pairs, unmatched, removed = _match_items(items, ref["items"])

    if ins == 2:
        for idx, it in unmatched:
            ctx.hit("CORR_NEW_SSTID", it["sstid"], line=idx)
        for idx, it, r in pairs:
            if _ok(it["vra"]) and _z(it["vra"]) != _z(r["vra"]):
                ctx.hit("CORR_VRA", "%s / مرجع %s" % (it["vra"], r["vra"]), line=idx)
        return ref

    # --- ins 4: sales return ---------------------------------------------------
    for idx, it in unmatched:
        ctx.hit("RET_NEW_SSTID", it["sstid"], line=idx)
    decreased = removed > 0
    for idx, it, r in pairs:
        am, ram = it["am"], r["am"]
        if _ok(am, ram) and am is not None and ram is not None:
            if am > ram:
                ctx.hit("RET_AM_UP", "%s > مرجع %s" % (am, ram), line=idx)
            elif am < ram:
                decreased = True
        if _ok(it["vra"]) and _z(it["vra"]) != _z(r["vra"]):
            ctx.hit("RET_VRA", "%s / مرجع %s" % (it["vra"], r["vra"]), line=idx)
        if _ok(it["fee"]) and _z(it["fee"]) != _z(r["fee"]):
            ctx.hit("RET_FEE", "%s / مرجع %s" % (it["fee"], r["fee"]), line=idx)
    all_gone = (not items) or all(_ok(it["am"]) and _z(it["am"]) == 0 for it in items)
    if all_gone:
        ctx.hit("RET_ALL", "همهٔ اقلام برگشت خورده — باید صورتحساب ابطالی صادر شود")
    elif not decreased:
        ctx.hit("RET_NO_DECREASE")
    return ref


def check_inno_serial(ctx, h):
    """1300501 — real data: on ~99% of accepted ORIGINAL invoices. Our inference:
    the system compares inno with the 10-hex serial inside taxid (FAQ 10-7 layout
    6+5+10+1); the client draws that serial from Random.Shared, so they differ."""
    inno, taxid = _s(h.get("inno")), _s(h.get("taxid"))
    if inno and re.fullmatch(r"[A-Z0-9]{22}", taxid) and inno.upper() != taxid[11:21]:
        ctx.hit("INNO_SERIAL", "%s / سریال درون شماره مالیاتی %s" % (inno, taxid[11:21]))


CANCEL_OOP_FIELDS = ("tprdis", "tdis", "tadis", "tvam", "todam", "tbill", "tax17")


def check_out_of_pattern(ctx, inv, h, bodies):
    """Kind-4 warnings seen on the live system. Never affect the verdict."""
    ext = inv.get("extension")
    if isinstance(ext, list) and ext:
        ctx.hit("OOP_EXTENSION", "%d مورد" % len(ext))
    if _int(h.get("setm")) != 3:
        if h.get("cap") is not None:
            ctx.hit("OOP_CAP", "setm=%r" % (h.get("setm"),))
        if h.get("insp") is not None:
            ctx.hit("OOP_INSP", "setm=%r" % (h.get("setm"),))
    if h.get("indati2m") is not None and _int(h.get("insr")) != 1:
        ctx.hit("OOP_INDATI2M")
    if h.get("ins") == 3:
        for f in CANCEL_OOP_FIELDS:
            if h.get(f) is not None:
                ctx.hit("OOP_CANCEL_HEADER", "ابطالی", field=f)
        return
    if _int(h.get("inp")) != 3:
        for i, b in enumerate(bodies, 1):
            if isinstance(b, dict):
                for f in ("consfee", "spro", "bros", "tcpbs"):
                    if b.get(f) is not None:
                        ctx.hit("OOP_" + f.upper(), line=i)


def cap_messages(errors, warnings):
    """EC_V02 p14/p16, FAQ 10-9: at most 50, errors first; overflow marker 00000."""
    if len(errors) + len(warnings) <= MAX_MESSAGES:
        return errors, warnings
    kept_e = errors[:MAX_MESSAGES]
    kept_w = warnings[:MAX_MESSAGES - len(kept_e)]
    kept_w.append({
        "code": OVERFLOW_CODE,
        "message": "تعداد پیام‌های خطا/هشدار بیشتر از ۵۰ مورد است و امکان نمایش بیشتر از این تعداد وجود ندارد",
        "errorType": "WARNING",
        "rule": "OVERFLOW",
        "source": OVERFLOW_SOURCE,
    })
    return kept_e, kept_w


def validate_invoice(inv, now=None):
    """Returns (errors, warnings, notes). Caller must hold LOCK."""
    now = server_now_ms() if now is None else now
    ctx = Ctx()
    h = inv.get("header") or {}
    bodies = inv.get("body") or []

    check_taxid(ctx, h)
    indatim_ms = check_indatim(ctx, h, now)
    check_enums(ctx, h)
    check_buyer(ctx, h)
    items = check_body(ctx, h, bodies)
    check_settlement(ctx, h)
    check_reference(ctx, h, items, indatim_ms)
    check_inno_serial(ctx, h)
    check_out_of_pattern(ctx, inv, h, bodies)

    errors, warnings = cap_messages(ctx.errors, ctx.warnings)
    return errors, warnings, ctx.notes


# ==========================================================================
# dashboard state machine
# ==========================================================================

def _history(rec, new, why):
    rec.setdefault("history", []).append(
        {"at": server_now_ms(), "from": rec.get("invoiceStatus"), "to": new, "why": why})


def set_dashboard(rec, new, why):
    """Change a dashboard status and apply the cascades."""
    if rec.get("invoiceStatus") == new:
        return
    _history(rec, new, why)
    rec["invoiceStatus"] = new
    # FAQ 11-6 / 4-9(b): a referral that became final makes its reference cancelled
    if rec["ins"] in (2, 4) and new in CONSUMING:
        ref = STATE["invoices"].get(rec["irtaxid"])
        if ref is not None and ref["invoiceStatus"] != CANCELED:
            set_dashboard(ref, CANCELED, "referral %s -> %s" % (rec["taxid"], new))


def initial_dashboard(h):
    if h.get("ins") == 3:
        return NO_NEED
    if _int(h.get("inty")) == 1 and _s(h.get("tinb")):
        return AWAITING
    return NO_NEED


def apply_time(now=None):
    """AWAITING older than 30 days -> SYSTEMIC_APPROVED (FAQ 11-2, 11-6)."""
    now = server_now_ms() if now is None else now
    with LOCK:
        for rec in sorted(STATE["invoices"].values(), key=lambda r: r["receivedAt"]):
            if rec["invoiceStatus"] == AWAITING and now - rec["receivedAt"] >= REACTION_DAYS * DAY_MS:
                set_dashboard(rec, SYSTEMIC, "30 days without buyer reaction")


def register_invoice(fiscal_id, inv, uid, reference, errors, warnings, notes, now=None):
    now = server_now_ms() if now is None else now
    h = inv.get("header") or {}
    taxid = _s(h.get("taxid"))
    items = []
    if h.get("ins") != 3:
        for b in inv.get("body") or []:
            if isinstance(b, dict):
                items.append({"sstid": _s(b.get("sstid")),
                              "am": _num(Ctx(), b, "am"), "fee": _num(Ctx(), b, "fee"),
                              "vra": _num(Ctx(), b, "vra")})
    rec = {
        "taxid": taxid,
        "inno": _s(h.get("inno")),
        "irtaxid": _s(h.get("irtaxid")),
        "ins": h.get("ins"),
        "inty": _int(h.get("inty")),
        "inp": _int(h.get("inp")),
        "tins": _s(h.get("tins")),
        "tob": _int(h.get("tob")),
        "tinb": _s(h.get("tinb")),
        "bid": _s(h.get("bid")),
        "indatim": _int(h.get("indatim")),
        "uid": uid,
        "reference": reference,
        "fiscalId": fiscal_id,
        "errors": errors,
        "warnings": warnings,
        "notes": notes,
        "success": not errors,
        "status": "FAILED" if errors else "IN_PROGRESS",   # processing status
        "invoiceStatus": None,                              # dashboard status
        "receivedAt": now,
        "polls": 0,
        "items": items,
        "confirmationReferenceId": None if errors else uuid.uuid4().hex[:16].upper(),
        "createdAt": datetime.now().isoformat(timespec="seconds"),
    }
    with LOCK:
        STATE["by_reference"][reference] = taxid
        STATE["by_uid"][uid] = taxid
        if errors:
            STATE["rejected"][reference] = rec
            return rec
        STATE["invoices"][taxid] = rec
        if rec["ins"] == 3:
            ref = STATE["invoices"].get(rec["irtaxid"])
            if ref is not None:
                set_dashboard(ref, CANCELED, "cancelled by %s" % taxid)
                # IS_V7.8 p16 item 3: unapproved referrals of a cancelled reference are void
                for r in list(STATE["invoices"].values()):
                    if (r["irtaxid"] == ref["taxid"] and r["ins"] in (2, 4)
                            and r["invoiceStatus"] in (AWAITING, REJECTED)):
                        set_dashboard(r, CANCELED, "reference %s cancelled" % ref["taxid"])
            set_dashboard(rec, NO_NEED, "cancellation")
        else:
            set_dashboard(rec, initial_dashboard(h), "registered")
    return rec


def buyer_action(taxid, action, now=None):
    now = server_now_ms() if now is None else now
    with LOCK:
        apply_time(now)
        rec = STATE["invoices"].get(taxid)
        if rec is None:
            return {"ok": False, "status": None, "error": "NOT_FOUND"}
        if action not in ("approve", "reject"):
            return {"ok": False, "status": rec["invoiceStatus"], "error": "BAD_ACTION"}
        if rec["invoiceStatus"] != AWAITING:
            return {"ok": False, "status": rec["invoiceStatus"], "error": "NOT_AWAITING_REACTION"}
        if now - rec["receivedAt"] >= REACTION_DAYS * DAY_MS:
            return {"ok": False, "status": rec["invoiceStatus"], "error": "REACTION_PERIOD_OVER"}
        set_dashboard(rec, APPROVED if action == "approve" else REJECTED, "buyer " + action)
        return {"ok": True, "status": rec["invoiceStatus"], "error": None}


def set_status(taxid, status):
    """Test control: legacy names are mapped; processing statuses set `status`."""
    with LOCK:
        rec = STATE["invoices"].get(taxid)
        if not rec or not status:
            return False
        status = LEGACY_STATUS.get(status, status)
        if status in PROCESSING_STATUSES:
            rec["status"] = status
            return True
        if status not in DASHBOARD_STATUSES:
            return False
        set_dashboard(rec, status, "__set_status")
        return True


def advance_days(days):
    with LOCK:
        CONFIG["clock_offset_ms"] = int(CONFIG["clock_offset_ms"]) + int(round(float(days) * DAY_MS))
        apply_time()
        return server_now_ms()


def invoice_status(tax_ids):
    """Mimics v2 inquiry-invoice-status."""
    apply_time()
    out = []
    with LOCK:
        for t in tax_ids:
            rec = STATE["invoices"].get(t)
            out.append({
                "taxId": t,
                "invoiceStatus": rec["invoiceStatus"] if rec else None,
                "article6Status": "NOT_EXCEEDED" if rec else None,
                "error": None if rec else "NOT_FOUND",
            })
    return out


def find_record(reference=None, uid=None):
    with LOCK:
        for key, field in ((reference, "reference"), (uid, "uid")):
            if key is None:
                continue
            for r in STATE["rejected"].values():
                if r.get(field) == key:
                    return r
            index = STATE["by_reference"] if field == "reference" else STATE["by_uid"]
            taxid = index.get(key)
            rec = STATE["invoices"].get(taxid) if taxid else None
            if rec is not None and rec.get(field) == key:
                return rec
        return None


def poll_processing(rec):
    """IN_PROGRESS -> SUCCESS after `in_progress_polls` inquiries."""
    with LOCK:
        if rec["status"] == "IN_PROGRESS":
            if rec["polls"] >= int(CONFIG["in_progress_polls"]):
                rec["status"] = "SUCCESS"
            rec["polls"] += 1
        return rec["status"]


# ==========================================================================
# crypto (Decimal-preserving variant of moadian_mock.decrypt_packet)
# ==========================================================================

def decrypt_packet_text(packet):
    """Returns the packet's plaintext JSON *text*."""
    sym, iv_hex, payload = packet.get("symmetricKey"), packet.get("iv"), packet.get("data")
    if not sym or not iv_hex:
        return payload if isinstance(payload, str) else json.dumps(payload)
    aes_hex = mm.RSA_KEY.decrypt(
        base64.b64decode(sym),
        padding.OAEP(mgf=padding.MGF1(algorithm=hashes.SHA256()),
                     algorithm=hashes.SHA256(), label=None),
    ).decode()
    aes_key = bytes.fromhex(aes_hex)
    clear_xored = AESGCM(aes_key).decrypt(bytes.fromhex(iv_hex), base64.b64decode(payload), None)
    return mm.xor_blocks(clear_xored, aes_key).decode("utf-8")


def _json_default(o):
    if isinstance(o, Decimal):
        return int(o) if o == o.to_integral_value() else float(o)
    if o is BAD:
        return "<BAD>"
    raise TypeError(repr(o))


# ==========================================================================
# HTTP
# ==========================================================================

def _envelope(result):
    return {"timestamp": server_now_ms(), "result": result, "errors": [],
            "signature": None, "signatureKeyId": None}


def sync_response(packet_type, data):
    return _envelope({"uid": str(uuid.uuid4()), "packetType": packet_type, "data": data,
                      "encryptionKeyId": None, "symmetricKey": None, "iv": None})


def enqueue(body, fault=None):
    """fault: None | 'timeout' | 'drop_result' — the invoices are registered in all
    cases; the fault only changes what the client gets back (see Handler)."""
    results = []
    shared_ref = uuid.uuid4().hex[:20].upper() if take_fault("fault_duplicate_reference") else None
    for packet in body.get("packets", []):
        uid = packet.get("uid") or str(uuid.uuid4())
        fiscal = packet.get("fiscalId") or ""
        reference = shared_ref or uuid.uuid4().hex[:20].upper()
        try:
            text = decrypt_packet_text(packet)
            inv_plain = json.loads(text)
            inv = json.loads(text, parse_float=Decimal)
        except Exception as exc:                                  # noqa: BLE001
            log("decrypt failed:", exc)
            results.append({"uid": uid, "referenceNumber": None, "errorCode": "0000001",
                            "errorDetail": "unable to decrypt packet"})
            continue
        with LOCK:
            LAST_PACKET["envelope"] = {k: v for k, v in body.items() if k != "packets"}
            LAST_PACKET["packet"] = {k: v for k, v in packet.items() if k != "data"}
            LAST_PACKET["invoice"] = inv_plain
            key = (inv_plain.get("header") or {}).get("taxid") if isinstance(inv_plain, dict) else None
            if key:
                PAYLOADS[key] = inv_plain
            apply_time()
            errors, warnings, notes = validate_invoice(inv)
            rec = register_invoice(fiscal, inv, uid, reference, errors, warnings, notes)
            if shared_ref:
                DUP_REFS.setdefault(shared_ref, []).append(rec)
            if fault:
                STATS["registered_despite_fault"].append(
                    {"taxid": rec["taxid"], "reference": reference, "uid": uid, "fault": fault,
                     "accepted": not errors})
        log(("REJECT " if errors else "ACCEPT ") + rec["taxid"], "ins=%s" % rec["ins"],
            ("codes=" + ",".join(e["code"] for e in errors + warnings)) if errors or warnings else "",
            rec["invoiceStatus"] or "")
        results.append({"uid": uid, "referenceNumber": reference, "errorCode": None, "errorDetail": None})
    return _envelope(results)


def inquiry(body):
    try:
        query = mm.decrypt_packet(body.get("packet", {}))
    except Exception:                                             # noqa: BLE001
        query = (body.get("packet") or {}).get("data")
    keys = []
    if isinstance(query, dict):
        keys = query.get("referenceNumber") or query.get("referenceNumbers") or query.get("uid") or []
    elif isinstance(query, list):
        keys = query
    if isinstance(keys, str):
        keys = [keys]
    out = []
    recs = []
    for k in keys:
        key = k.get("uid") if isinstance(k, dict) else k
        with LOCK:
            dup = list(DUP_REFS.get(key) or [])
        if dup:
            recs.extend(dup)            # one shared reference -> every packet behind it
            continue
        rec = find_record(reference=key, uid=key)
        if rec is not None:
            recs.append(rec)
    for rec in recs:
        status = poll_processing(rec)
        data = {"confirmationReferenceId": rec["confirmationReferenceId"],
                "error": rec["errors"], "warning": rec["warnings"], "success": rec["success"]}
        if status == "IN_PROGRESS" and CONFIG["empty_data_when_in_progress"]:
            data = {}
        out.append({"referenceNumber": rec["reference"], "uid": rec["uid"], "status": status,
                    "packetType": "INVOICE.V01" if status == "IN_PROGRESS" else "receive_invoice_confirm",
                    "fiscalId": rec["fiscalId"], "data": data})
    return sync_response("INQUIRY_BY_REFERENCE_NUMBER", out)


def _route(path):
    route = urlsplit(path.replace("\\", "/")).path.strip("/")
    route = route.rsplit("api/", 1)[-1]
    if "__" in route:
        route = route[route.index("__"):]
    return route


class Handler(mm.Handler):
    def _send(self, obj, code=200):
        body = json.dumps(obj, ensure_ascii=False, default=_json_default).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _send_safe(self, obj, code=200):
        """The client may have hung up already (timeout fault)."""
        try:
            self._send(obj, code)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            self.close_connection = True

    def _fault_500(self, route):
        log("FAULT http 500 on", route)
        return self._send_safe({
            "timestamp": server_now_ms(), "result": None,
            "errors": [{"errorCode": "0000500", "code": "500",
                        "detail": "خطای داخلی سرور (fault_http_500_next)",
                        "message": "Internal Server Error"}],
            "signature": None, "signatureKeyId": None}, 500)

    def do_GET(self):
        route = _route(self.path)
        _count_route(route)
        if route == "__stats":
            with LOCK:
                return self._send(json.loads(json.dumps(STATS)))
        if route == "__ping":
            return self._send({"ok": True, "server": "moadian_mock_full"})
        if route == "__state":
            apply_time()
            with LOCK:
                return self._send({"invoices": STATE["invoices"], "rejected": STATE["rejected"]})
        if route == "__payloads":
            with LOCK:
                return self._send(PAYLOADS)
        if route == "__last":
            with LOCK:
                return self._send(LAST_PACKET)
        if route == "__clock":
            return self._send({"serverTime": server_now_ms(), "realTime": real_now_ms(),
                               "offsetMs": CONFIG["clock_offset_ms"]})
        if route == "__invoice_status":
            qs = parse_qs(urlsplit(self.path).query)
            ids = []
            for v in qs.get("taxIds", []) + qs.get("taxId", []):
                ids.extend(x for x in v.split(",") if x)
            return self._send(invoice_status(ids))
        if route == "__rules":
            return self._send({"rules": RULES, "ambiguities": AMBIGUITIES, "config": CONFIG})
        if route == "__config":
            return self._send(CONFIG)
        return self._send({"error": "not found"}, 404)

    def do_POST(self):
        route = _route(self.path)
        body = self._read_json()
        if not isinstance(body, dict):
            body = {}
        _count_route(route)

        if route == "__reset":
            reset_state()
            return self._send({"ok": True})
        if route == "__set_status":
            ok = set_status(body.get("taxid"), body.get("status"))
            rec = STATE["invoices"].get(body.get("taxid") or "")
            return self._send({"ok": ok, "invoiceStatus": rec["invoiceStatus"] if rec else None})
        if route == "__buyer_action":
            return self._send(buyer_action(body.get("taxid"), body.get("action")))
        if route == "__advance_days":
            try:
                t = advance_days(body.get("days", 0))
            except (TypeError, ValueError):
                return self._send({"ok": False, "error": "bad days"}, 400)
            return self._send({"ok": True, "serverTime": t})
        if route == "__config":
            with LOCK:
                for k in ("deadline_days", "single_use_reference", "clock_offset_ms",
                          "in_progress_polls", "empty_data_when_in_progress") + FAULT_KEYS:
                    if k in body and body[k] is not None:
                        CONFIG[k] = type(DEFAULT_CONFIG[k])(body[k])
                apply_time()
                return self._send(dict(CONFIG, ok=True))

        if route.endswith("GET_SERVER_INFORMATION"):
            return self._send(sync_response("GET_SERVER_INFORMATION", {
                "serverTime": server_now_ms(),
                "publicKeys": [{"key": mm.RSA_PUB_B64, "id": mm.KEY_ID,
                                "algorithm": "RSA", "purpose": 0}]}))
        if route.endswith("GET_TOKEN"):
            return self._send(sync_response("GET_TOKEN", {
                "token": "mock-token-" + uuid.uuid4().hex,
                "expiresIn": server_now_ms() + 20 * 60 * 1000}))
        if route.endswith("normal-enqueue"):
            packets = body.get("packets")
            with LOCK:
                STATS["packets_per_send_request"].append(len(packets) if isinstance(packets, list) else 0)
            if take_fault("fault_http_500_next"):
                return self._fault_500(route)
            if take_fault("fault_timeout_next"):
                env = enqueue(body, fault="timeout")
                delay = float(CONFIG["fault_timeout_seconds"])
                log("FAULT timeout: registered, answering after %.1f s" % delay)
                time.sleep(delay)
                return self._send_safe(env)
            if take_fault("fault_drop_result_next"):
                env = enqueue(body, fault="drop_result")
                env["result"] = []
                log("FAULT drop_result: registered, answering result=[]")
                return self._send_safe(env)
            return self._send(enqueue(body))
        if route.endswith("INQUIRY_BY_REFERENCE_NUMBER") or route.endswith("INQUIRY_BY_UID"):
            if take_fault("fault_http_500_next"):
                return self._fault_500(route)
            return self._send(inquiry(body))

        log("unhandled route:", route)
        return self._send({"timestamp": server_now_ms(), "result": None,
                           "errors": [{"detail": "unknown route " + route, "errorCode": "0000404"}]}, 404)


# ==========================================================================
# choices made where the documents are ambiguous — NOT facts about the system
# ==========================================================================

AMBIGUITIES = [
    "Code length: EC_V02's examples (p15, p33) show kind-2 codes with 5 digits ('02002'), "
    "but production TAXDTL has 0200201 x154 and never 02002. We emit 7 digits with detail "
    "'01' for EVERY kind-2 rule; only 0200201 is actually observed — the others "
    "(0204101, 0202701, ...) are extrapolated from it.",
    "Min-value detail: EC_V02 p21-22 maps both exclusiveMinimum and exclusiveMaximum to 05 "
    "and min/max to 06; production shows 05 for am/fee/prdis/tprdis, so these are "
    "exclusiveMinimum > 0. Other fields' bounds are unknown.",
    "1300501: production shows it on ~99% of accepted ORIGINAL invoices, so the old reading "
    "'inno != reference inno' cannot explain it. No document text says what inno is compared "
    "with (EC_V02 p15 only shows the message) — the old rule was removed. Our replacement "
    "(inno != 10-hex serial inside taxid, FAQ 10-7 layout) is an INFERENCE that fits the "
    "client (random taxid serial) but is not confirmed.",
    "Deadline + insr: production has 00107 x52, always with 0200201. We emit 0200201+00107 "
    "when insr is empty, 0200201 alone when insr is present but != 1 (our choice), and "
    "nothing for the deadline when insr == 1 (article-9 path; V7.9 table 86 per CLAUDE.md, "
    "not re-read). The ~102 other 0200201 rows have no recorded cause; we attribute them to "
    "future date / before-reference, which is unverified.",
    "Out-of-pattern conditions are inferred from counts, not from documents: 14003 only when "
    "insr != 1; 14052-14055 only when inp != 3 (gold pattern); 14022-14027/14032 only on "
    "ins=3. The ~500-700 counts of 14028/14031/14004/14007/14010/14012 do not all equal 537, "
    "so they are NOT simulated. 1301001/1301401 (tob/bpc vs system data) and transport 4212 "
    "(old API signature) are not simulated.",
    "ins=1 + irtaxid: brief asked for error 0400601; EC_V02 p27 row 20 defines kind 4 "
    "('out of pattern') as a WARNING with no detail digits, so we emit warning 14006 "
    "(accepted). Whether the real system even applies kind 4 to this case is unknown.",
    "Overflow marker 00000: EC_V02 p16 calls it a 'warning', FAQ 10-9 calls it an 'error'. "
    "We put it in the warnings array so it never flips the verdict.",
    "Referral indatim earlier than reference: EC_V02 p33 says 'اصلاحی', FAQ 10-10 says "
    "'ارجاعی'. We apply it to ins 2, 3 and 4.",
    "Which statuses 'consume' a reference: FAQ 4-9(b) names APPROVED; FAQ 11-6 says issuing "
    "any referral cancels the reference. We use APPROVED, SYSTEMIC_APPROVED, NO_NEED_REACTION "
    "and IMPOSSIBLE_REACTION (NO_NEED consumes at issuance, e.g. every type-2 referral).",
    "Cancelling an ins 2/4 invoice: FAQ 4-9 allows 'reference with any status' or 'referral "
    "AWAITING'. We treat an ins 2/4 invoice in APPROVED/SYSTEMIC/NO_NEED/IMPOSSIBLE as being "
    "in the reference role (cancellable), AWAITING as referral role (cancellable), and "
    "REJECTED as neither (rejected 0300601).",
    "FAQ 4-10 single use vs FAQ 4-9: a cancellation of a reference with an AWAITING referral "
    "is explicitly allowed by 4-9, so single-use is enforced only for new ins 2/4; a referral "
    "that is REJECTED or CANCELED does not count as 'live' (otherwise a buyer rejection would "
    "dead-end the original).",
    "IS_V7.8 p15 item 4 lists 3 statuses for a referral-as-reference; FAQ 4-3 note 3 adds "
    "IMPOSSIBLE_REACTION. We use the FAQ's 4.",
    "Sales-return item matching: by sstid, k-th occurrence to k-th occurrence (the same sstid "
    "may appear on several lines). An item missing from the return counts as fully returned.",
    "Sales return with all am=0 also triggers AM_MIN (0103605) besides RET_ALL (0303602); "
    "the real ordering/suppression is unknown. The detail digits '02' of 0303602 are ours.",
    "Buyer identified by bid only (inty=1, tob 1/4, no tinb) starts as NO_NEED_REACTION "
    "(no economic code -> no dashboard to react). FAQ 11-2 is about buyers 'with an active "
    "dashboard'; the real mapping is unknown.",
    "bpc missing -> warning 1101401 is invented (source 'derived'); the detail 01 "
    "(additionalProperties) does not really describe a missing value.",
    "setm 1/2: cap and insp are NOT validated. Production TAXDTL shows the live system marks "
    "them out of pattern (warnings 14029 x14,505 / 14030 x14,487) even when 0 is sent, so the "
    "earlier derived errors (setm=1 insp>0, setm=2 cap>0) were removed. FAQ 10-5/10-6 state "
    "cap = tbill - todam - tvam - insp without a setm restriction; we apply it only to setm=3 "
    "(setm=3 itself has zero production samples).",
    "Deadline boundary: 'حداکثر ۱۲ روز' read as age <= 12 days accepted. Real data (CLAUDE.md "
    "§5) shows type-1 acceptances up to 13 days — the mock is stricter than the real system.",
    "Type-2 buyer fields: FAQ 9-6 says optional but must be valid; the allowed lengths when tob "
    "is absent (tinb 11|14, bid 10|11|12) are ours. inty=1 with inp 7/11 uses the same "
    "lenient checks (derived).",
    "Correction vra change -> 0304401: from IS_V7.8 p15 item 1 (VAT rate not modifiable), "
    "reusing the sales-return code; not in the brief.",
    "Cancellation does not re-validate inty/inp/buyer/body (IS_V7.8 p16: fetched from the "
    "reference); only taxid, irtaxid, indatim, tins (FAQ 4-7) and the reference rules apply.",
    "Header sums are compared with 0 tolerance and a null header field counts as 0.",
    "Fault injection (__config fault_*) is NOT a documented behaviour of the real system: "
    "the 500 body shape ('errors' with errorCode 0000500), the 'registered then slow' "
    "timeout, the empty result list and the shared referenceNumber are generic transport "
    "failures invented to probe client robustness. Whether the real system can register an "
    "invoice and still fail the HTTP answer is unknown — but nothing guarantees it cannot.",
    "Processing status: accepted invoices are IN_PROGRESS in __state until the first "
    "inquiry, then SUCCESS (configurable via in_progress_polls).",
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=9190)
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args()

    global VERBOSE
    VERBOSE = not args.quiet
    mm.VERBOSE = False

    class Server(ThreadingHTTPServer):
        allow_reuse_address = False
        daemon_threads = True

    try:
        srv = Server(("127.0.0.1", args.port), Handler)
    except OSError as exc:
        print("cannot bind port %d -- another mock is probably already running (%s)"
              % (args.port, exc), file=sys.stderr)
        return 1
    print("Moadian FULL mock listening on http://127.0.0.1:%d/" % args.port, flush=True)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main() or 0)
