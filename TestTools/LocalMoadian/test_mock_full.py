#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Self-test for moadian_mock_full.py.

    python3 TestTools/LocalMoadian/test_mock_full.py

Exercises every rule directly (no HTTP), the dashboard state machine, the FAQ 4-9
cancellation matrix, FAQ 4-10 on/off, 30-day system approval, clock-ahead 0200201,
the 50-message cap, plus end-to-end HTTP tests that encrypt a packet the way the
C# client does. At the end it checks that every rule in RULES was hit at least once.

Reminder (CLAUDE.md §3/§10): green here proves the mock matches *our reading* of
the documents and of the production TAXDTL counts, not the real system.

Builders: `invoice()` makes a CLEAN invoice (no warnings at all: taxid serial ==
inno, cap/insp only for setm=3, no extension/indati2m). `client_shaped()` makes
what the C# client actually sends, and must reproduce the warning profile seen
on 100% of accepted production rows (14800, 14029, 14030, 14003, 1300501).
"""

import base64
import json
import os
import random
import string
import sys
import threading
import unittest
import urllib.error
import urllib.request
from decimal import Decimal, ROUND_DOWN

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import moadian_mock_full as M  # noqa: E402

from cryptography.hazmat.primitives import hashes, serialization  # noqa: E402
from cryptography.hazmat.primitives.asymmetric import padding  # noqa: E402
from cryptography.hazmat.primitives.ciphers.aead import AESGCM  # noqa: E402

M.VERBOSE = False
M.mm.VERBOSE = False

DAY = M.DAY_MS
HIT_RULES = set()
_orig_hit = M.Ctx.hit


def _tracking_hit(self, rid, *a, **kw):
    HIT_RULES.add(rid)
    return _orig_hit(self, rid, *a, **kw)


M.Ctx.hit = _tracking_hit

# ---------------------------------------------------------------------------
# builders
# ---------------------------------------------------------------------------


def new_taxid(serial=None):
    """6 memory id + 5 hex date + 10 hex serial + 1 check (FAQ 10-7 layout)."""
    if serial is None:
        serial = "".join(random.choice("0123456789ABCDEF") for _ in range(10))
    date = "".join(random.choice("0123456789ABCDEF") for _ in range(5))
    return "A11216" + date + serial + random.choice("0123456789")


def T(x):
    return Decimal(x).to_integral_value(rounding=ROUND_DOWN)


def line(am=10, fee=100000, vra=9, dis=0, odam=0, olam=0, sstid="2820000000000"):
    am, fee = Decimal(str(am)), Decimal(str(fee))
    prdis = T(am * fee)
    adis = prdis - dis
    vam = T(adis * Decimal(vra) / 100)
    return {"sstid": sstid, "sstt": "کالای آزمایشی", "mu": "1627", "am": am, "fee": fee,
            "prdis": prdis, "dis": Decimal(dis), "adis": adis, "vra": Decimal(vra), "vam": vam,
            "odam": Decimal(odam), "olam": Decimal(olam), "tsstam": adis + vam + odam + olam}


def invoice(ins=1, irtaxid=None, lines=None, inty=1, tob=2, tinb="12345678901", bid=None,
            bpc=None, setm=1, inp=1, indatim=None, inno=None, tins="11111111111",
            cap=None, insp=None, **extra):
    lines = [line()] if lines is None else lines
    if inno is None:
        inno = "1405" + "".join(random.choice("0123456789") for _ in range(6))
    h = {
        "taxid": new_taxid(serial=inno),
        "indatim": indatim if indatim is not None else M.server_now_ms() - 60_000,
        "indati2m": None, "inty": inty, "inno": inno, "irtaxid": irtaxid, "inp": inp,
        "ins": ins, "tins": tins, "tob": tob, "tinb": tinb, "bid": bid, "bpc": bpc,
        "tprdis": sum((b["prdis"] for b in lines), Decimal(0)),
        "tdis": sum((b["dis"] for b in lines), Decimal(0)),
        "tadis": sum((b["adis"] for b in lines), Decimal(0)),
        "tvam": sum((b["vam"] for b in lines), Decimal(0)),
        "todam": sum((b["odam"] + b["olam"] for b in lines), Decimal(0)),
        "tbill": sum((b["tsstam"] for b in lines), Decimal(0)),
        "setm": setm, "cap": cap, "insp": insp,
    }
    h.update(extra)
    return {"header": h, "body": lines, "payments": []}


def client_shaped(**kw):
    """What the C# client sends: random taxid serial, cap/insp always serialized,
    indati2m set, extension=[{}] (TaxService.SendInvoices adds new InvoiceExtension())."""
    inv = invoice(**kw)
    h = inv["header"]
    h["taxid"] = new_taxid()
    h.setdefault("insr", None)
    if h["cap"] is None:
        h["cap"] = Decimal(0)
    if h["insp"] is None:
        h["insp"] = Decimal(0)
    h["indati2m"] = h["indatim"]
    inv["extension"] = [{}]
    return inv


def submit(inv):
    """Same path as the HTTP enqueue, minus crypto."""
    with M.LOCK:
        M.apply_time()
        errors, warnings, notes = M.validate_invoice(inv)
        ref = M.uuid.uuid4().hex[:20].upper()
        return M.register_invoice("A11216", inv, M.uuid.uuid4().hex, ref, errors, warnings, notes)


def codes(rec):
    return [m["code"] for m in rec["errors"] + rec["warnings"]]


def dash(taxid):
    return M.STATE["invoices"][taxid]["invoiceStatus"]


def referral_of(ref_inv, ins, lines=None, **kw):
    h = ref_inv["header"]
    args = dict(inty=h["inty"], tob=h["tob"], tinb=h["tinb"], bid=h["bid"], bpc=h["bpc"],
                inp=h["inp"], inno=h["inno"], tins=h["tins"])
    args.update(kw)
    if lines is None:
        lines = [dict(b) for b in ref_inv["body"]]
    return invoice(ins=ins, irtaxid=h["taxid"], lines=lines, **args)


def cancellation_of(ref_inv, **kw):
    h = ref_inv["header"]
    inv = {"header": {"taxid": new_taxid(), "indatim": M.server_now_ms() - 1000,
                      "irtaxid": h["taxid"], "ins": 3, "tins": kw.get("tins", h["tins"]),
                      "inno": h["inno"]},
           "body": []}
    inv["header"].update({k: v for k, v in kw.items() if k != "tins"})
    return inv


class Base(unittest.TestCase):
    def setUp(self):
        M.reset_state()

    def ok(self, inv, *expect_warnings):
        rec = submit(inv)
        self.assertTrue(rec["success"], "expected accept, got %s" % codes(rec))
        for w in expect_warnings:
            self.assertIn(w, codes(rec))
        return rec

    def bad(self, inv, code, rule=None):
        rec = submit(inv)
        self.assertFalse(rec["success"], "expected reject with %s" % code)
        self.assertIn(code, codes(rec), codes(rec))
        if rule:
            self.assertIn(rule, [m["rule"] for m in rec["errors"]])
        return rec

    def original(self, **kw):
        inv = invoice(**kw)
        self.ok(inv)
        return inv


# ---------------------------------------------------------------------------
# 1. code construction
# ---------------------------------------------------------------------------

class T01_Code(unittest.TestCase):
    def test_real_system_examples(self):
        c = M.code
        self.assertEqual(c(False, 0, 12), "00012")
        self.assertEqual(c(False, 1, 12, 4), "0101204")
        self.assertEqual(c(False, 2, 2, 1), "0200201")     # live 7-digit form (production x154)
        self.assertEqual(c(False, 0, 107), "00107")
        self.assertEqual(c(False, 1, 41, 5), "0104105")
        self.assertEqual(c(False, 1, 22, 5), "0102205")
        self.assertEqual(c(True, 4, 800), "14800")
        self.assertEqual(c(True, 4, 29), "14029")
        self.assertEqual(c(False, 3, 1, 1), "0300101")
        self.assertEqual(c(False, 3, 6, 1), "0300601")
        self.assertEqual(c(False, 3, 44, 1), "0304401")
        self.assertEqual(c(False, 3, 9, 2), "0300902")
        self.assertEqual(c(False, 0, 600), "00600")
        self.assertEqual(c(True, 3, 5, 1), "1300501")

    def test_detail_presence_enforced(self):
        with self.assertRaises(ValueError):
            M.code(False, 1, 12)
        with self.assertRaises(ValueError):
            M.code(False, 2, 2)                 # kind 2 now needs its detail digits
        with self.assertRaises(ValueError):
            M.code(True, 4, 800, 1)
        with self.assertRaises(ValueError):
            M.code(False, 0, 12, 1)

    def test_every_message_has_source(self):
        M.reset_state()
        inv = invoice(tinb="12", setm=9, irtaxid=new_taxid())
        inv["body"][0]["prdis"] += 1
        rec = submit(inv)
        self.assertTrue(rec["errors"] and rec["warnings"])
        for m in rec["errors"] + rec["warnings"]:
            self.assertTrue(m.get("source"), m)
            self.assertIn(m["errorType"], ("ERROR", "WARNING"))
            self.assertEqual(m["code"][0], "1" if m["errorType"] == "WARNING" else "0")

    def test_registry_codes_consistent(self):
        for r in M.RULES.values():
            self.assertEqual(r["code"][0], "1" if r["warning"] else "0", r)
            self.assertTrue(r["source"], r)


# ---------------------------------------------------------------------------
# 2. header basics
# ---------------------------------------------------------------------------

class T02_Header(Base):
    def test_baseline_valid(self):
        rec = self.ok(invoice())
        self.assertEqual(rec["errors"], [])
        self.assertEqual(rec["warnings"], [])

    def test_taxid(self):
        self.bad(invoice(taxid=""), "00001")
        self.bad(invoice(taxid="a11216" + "0" * 16), "0100104")
        self.bad(invoice(taxid="A11216" + "0" * 15), "0100104")
        inv = self.original()
        dup = invoice(taxid=inv["header"]["taxid"])
        self.bad(dup, "0300101", "TAXID_DUP")

    def test_ins_and_tins(self):
        self.bad(invoice(ins=5), "0100802")
        self.bad(invoice(ins=None), "00008")
        self.bad(invoice(tins=""), "00009")
        rec = self.ok(invoice(irtaxid=new_taxid()), "14006")   # warning, accepted
        self.assertEqual(rec["warnings"][0]["rule"], "INS1_IRTAXID")

    def test_inty(self):
        self.bad(invoice(inty=4), "0100402")
        self.bad(invoice(inty=None), "00004")


class T03_Indatim(Base):
    def test_missing(self):
        self.bad(invoice(indatim=0), "00002")

    def test_type(self):
        self.bad(invoice(indatim="yesterday"), "0100203")

    def test_future_no_tolerance(self):
        now = M.server_now_ms()
        self.bad(invoice(indatim=now + 1500), "0200201", "INDATIM_FUTURE")
        self.ok(invoice(indatim=M.server_now_ms()))

    def test_clock_ahead_via_offset(self):
        # client clock ahead of the server by 1 minute
        M.CONFIG["clock_offset_ms"] = -60_000
        self.bad(invoice(indatim=M.real_now_ms()), "0200201", "INDATIM_FUTURE")

    def test_deadline(self):
        now = M.server_now_ms()
        self.ok(invoice(indatim=now - 12 * DAY + 60_000))
        rec = self.bad(invoice(indatim=now - 12 * DAY - 60_000), "0200201", "INDATIM_DEADLINE")
        self.assertIn("00107", codes(rec))                   # production: 00107 always with 0200201

    def test_deadline_insr(self):
        now = M.server_now_ms()
        late = now - 20 * DAY
        rec = self.ok(invoice(indatim=late, insr=1))         # article-9 path declared
        self.assertNotIn("0200201", codes(rec))
        self.assertNotIn("00107", codes(rec))
        rec = self.bad(invoice(indatim=late, insr=2), "0200201", "INDATIM_DEADLINE")
        self.assertNotIn("00107", codes(rec))                # insr present -> not "empty"
        rec = self.bad(invoice(indatim=late, insr=""), "00107", "INSR_MISSING")

    def test_00107_only_for_deadline(self):
        rec = self.bad(invoice(indatim=M.server_now_ms() + 5000), "0200201", "INDATIM_FUTURE")
        self.assertNotIn("00107", codes(rec))
        self.assertNotIn("00107", codes(self.ok(invoice())))

    def test_deadline_configurable(self):
        M.CONFIG["deadline_days"] = 21
        self.ok(invoice(indatim=M.server_now_ms() - 20 * DAY))

    def test_deadline_applies_to_referrals_too(self):
        base = self.original()
        M.set_status(base["header"]["taxid"], "APPROVED")
        M.advance_days(13)
        c = referral_of(base, 2, indatim=base["header"]["indatim"])
        rec = self.bad(c, "0200201", "INDATIM_DEADLINE")
        self.assertIn("00107", codes(rec))

    def test_referral_before_reference(self):
        base = self.original(indatim=M.server_now_ms() - 1000)
        for ins in (2, 4, 3):
            M.reset_state()
            base = self.original(indatim=M.server_now_ms() - 1000)
            if ins == 3:
                inv = cancellation_of(base, indatim=base["header"]["indatim"] - 1)
            else:
                lines = [line(am=9)] if ins == 4 else None
                inv = referral_of(base, ins, lines=lines, indatim=base["header"]["indatim"] - 1)
            rec = self.bad(inv, "0200201", "INDATIM_BEFORE_REF")
            self.assertNotIn("00107", codes(rec))


# ---------------------------------------------------------------------------
# 3. buyer
# ---------------------------------------------------------------------------

class T04_Buyer(Base):
    def test_tob(self):
        self.bad(invoice(tob=7), "0101002")
        self.bad(invoice(tob=None), "00010")

    def test_legal_person(self):
        self.bad(invoice(tob=2, tinb=None), "00012")
        self.bad(invoice(tob=2, tinb="1234567890"), "0101204")
        self.bad(invoice(tob=3, tinb="12345678901234"), "0101204")
        self.bad(invoice(tob=2, tinb="021-5555555"), "0101204")    # phone number (Morvarid)
        self.ok(invoice(tob=3, tinb="12345678901"))

    def test_natural_person_tinb(self):
        self.ok(invoice(tob=1, tinb="12345678901234"))
        self.bad(invoice(tob=1, tinb="12345678901"), "0101204")
        self.ok(invoice(tob=4, tinb="12345678901234"))

    def test_natural_person_bid(self):
        self.ok(invoice(tob=1, tinb=None, bid="0012345678", bpc="1234567890"))
        self.bad(invoice(tob=1, tinb=None, bid="001234567", bpc="1234567890"), "0101104")
        self.ok(invoice(tob=4, tinb=None, bid="123456789012", bpc="1234567890"))
        self.bad(invoice(tob=4, tinb=None, bid="0012345678", bpc="1234567890"), "0101104")
        self.bad(invoice(tob=1, tinb=None, bid=None), "00012")

    def test_bpc(self):
        rec = self.ok(invoice(tob=1, tinb=None, bid="0012345678", bpc=None), "1101401")
        self.assertEqual(rec["errors"], [])
        self.bad(invoice(tob=1, tinb=None, bid="0012345678", bpc="123"), "0101404")

    def test_bbc(self):
        self.bad(invoice(bbc="12"), "0101504")
        self.ok(invoice(bbc="1234"))

    def test_export_and_bourse_optional(self):
        self.ok(invoice(inp=7, tob=None, tinb=None))
        self.ok(invoice(inp=11, tob=None, tinb=None))

    def test_type2_optional_but_valid(self):
        self.ok(invoice(inty=2, tob=None, tinb=None))
        self.ok(invoice(inty=2, tob=None, tinb="12345678901"))
        self.bad(invoice(inty=2, tob=None, tinb="12-345"), "0101204")
        self.bad(invoice(inty=2, tob=2, tinb="12345678901234"), "0101204")
        self.bad(invoice(inty=2, tob=None, tinb=None, bid="123"), "0101104")
        self.bad(invoice(inty=2, tob=9, tinb=None), "0101002")
        self.bad(invoice(inty=2, tob=None, tinb=None, bpc="12"), "0101404")


# ---------------------------------------------------------------------------
# 4. line math and header sums
# ---------------------------------------------------------------------------

class T05_LineMath(Base):
    def test_prdis_truncates(self):
        l = line(am="1.5", fee=33333)           # 49999.5 -> 49999
        self.assertEqual(l["prdis"], 49999)
        self.ok(invoice(lines=[l]))
        l2 = line(am="1.5", fee=33333)
        l2["prdis"] = Decimal(50000)            # rounded
        l2["adis"] = l2["prdis"]
        l2["vam"] = T(l2["adis"] * 9 / 100)
        l2["tsstam"] = l2["adis"] + l2["vam"]
        self.bad(invoice(lines=[l2]), "0204101")

    def test_prdis_exact(self):
        inv = invoice()
        inv["body"][0]["prdis"] += 1
        self.bad(inv, "0204101")

    def test_adis(self):
        inv = invoice()
        inv["body"][0]["adis"] -= 1
        self.bad(inv, "0204301")

    def test_vam_truncates(self):
        l = line(am=3, fee=33333, vra=9)        # adis 99999 -> 8999.91 -> 8999
        self.assertEqual(l["vam"], 8999)
        self.ok(invoice(lines=[l]))
        l["vam"] = Decimal(9000)
        l["tsstam"] = l["adis"] + l["vam"]
        self.bad(invoice(lines=[l]), "0204501")

    def test_tsstam_includes_odam_olam(self):
        l = line(odam=500, olam=300)
        self.ok(invoice(lines=[l]))
        l2 = line(odam=500, olam=300)
        l2["tsstam"] -= 800                     # the production shortcut
        self.bad(invoice(lines=[l2]), "0205901")

    def test_rial_decimals_but_fee_allowed(self):
        self.ok(invoice(lines=[line(am=2, fee="100.5")]))      # fee may have decimals
        inv = invoice()
        inv["body"][0]["dis"] = Decimal("0.5")
        self.bad(inv, "0104209")
        inv = invoice()
        inv["header"]["tbill"] = inv["header"]["tbill"] + Decimal("0.25")
        self.bad(inv, "0102709")

    def test_am_fee_bounds(self):
        self.bad(invoice(lines=[line(am=0)]), "0103605")
        self.bad(invoice(lines=[line(fee=0)]), "0103705")
        inv = invoice()
        inv["body"][0]["am"] = None
        self.bad(inv, "00036")
        inv = invoice()
        inv["body"][0]["fee"] = None
        self.bad(inv, "00037")

    def test_prdis_tprdis_exclusive_minimum(self):
        l = line(am="0.00001", fee=1)          # trunc(0.00001) = 0
        self.assertEqual(l["prdis"], 0)
        rec = self.bad(invoice(lines=[l]), "0104105", "PRDIS_MIN")
        self.assertIn("0102205", codes(rec))
        self.assertIn("TPRDIS_MIN", [m["rule"] for m in rec["errors"]])
        rec = self.ok(invoice())
        self.assertNotIn("0104105", codes(rec))
        self.assertNotIn("0102205", codes(rec))
        # production detail is 05 (exclusiveMinimum), never 06
        for rid in ("AM_MIN", "FEE_MIN", "PRDIS_MIN", "TPRDIS_MIN"):
            self.assertEqual(M.RULES[rid]["code"][-2:], "05", rid)

    def test_min_skipped_for_cancellation(self):
        base = self.original()
        can = cancellation_of(base)
        can["body"] = [line(am=0, fee=0)]
        can["header"]["tprdis"] = Decimal(0)
        self.ok(can)

    def test_type_error(self):
        inv = invoice()
        inv["body"][0]["am"] = "10"
        self.bad(inv, "0103603")

    def test_tolerance_is_zero(self):
        self.assertEqual(M.AMOUNT_TOLERANCE, 0)


class T06_HeaderSums(Base):
    def test_each_sum(self):
        for field, c in (("tprdis", "0202201"), ("tdis", "0202301"), ("tadis", "0202401"),
                         ("tvam", "0202501"), ("todam", "0202601"), ("tbill", "0202701")):
            inv = invoice(lines=[line(), line(am=3)])
            inv["header"][field] += 1
            self.bad(inv, c)

    def test_todam_is_odam_plus_olam(self):
        inv = invoice(lines=[line(odam=100, olam=50)])
        self.assertEqual(inv["header"]["todam"], 150)
        self.ok(inv)


class T07_Settlement(Base):
    def test_enum(self):
        self.bad(invoice(setm=4), "0102802")
        self.bad(invoice(setm=None), "00028")

    def test_mixed(self):
        inv = invoice(setm=3, cap=None, insp=Decimal(1000))
        self.bad(inv, "00029")
        inv = invoice(setm=3, cap=Decimal(1000), insp=None)
        self.bad(inv, "00030")
        inv = invoice(setm=3)
        basis = inv["header"]["tbill"] - inv["header"]["tvam"] - inv["header"]["todam"]
        inv["header"]["cap"], inv["header"]["insp"] = basis - 400_000, Decimal(400_000)
        self.ok(inv)
        inv = invoice(setm=3)
        inv["header"]["cap"], inv["header"]["insp"] = basis - 400_000, Decimal(400_001)
        self.bad(inv, "0202901", "CAPINSP_SUM")

    def test_mixed_uses_tbill_minus_taxes(self):
        inv = invoice(setm=3, lines=[line(odam=100)])
        h = inv["header"]
        h["cap"], h["insp"] = h["tbill"] - 1000, Decimal(1000)     # forgot to subtract taxes
        self.bad(inv, "0202901")

    def test_cash_and_credit_not_validated_only_warned(self):
        # production: 14029/14030 on ~99% of accepted rows -> out of pattern, not validated
        rec = self.ok(invoice(setm=1, insp=Decimal(5)), "14030")
        self.assertEqual(rec["errors"], [])
        rec = self.ok(invoice(setm=2, cap=Decimal(5)), "14029")
        self.assertEqual(rec["errors"], [])
        rec = self.ok(invoice(setm=1, cap=Decimal(0), insp=Decimal(0)), "14029", "14030")
        rec = self.ok(invoice(setm=1))
        self.assertEqual(rec["warnings"], [])

    def test_type2_credit_accepted_with_note(self):
        rec = self.ok(invoice(inty=2, tob=None, tinb=None, setm=2))
        self.assertTrue(rec["notes"])


class T08_Body(Base):
    def test_empty(self):
        self.bad(invoice(lines=[]), "00600")

    def test_sstid(self):
        self.bad(invoice(lines=[line(sstid="282000000000")]), "0103304")
        self.bad(invoice(lines=[line(sstid="")]), "00033")


# ---------------------------------------------------------------------------
# 5. referrals
# ---------------------------------------------------------------------------

class T09_Reference(Base):
    def test_missing_and_not_found(self):
        self.bad(invoice(ins=2, irtaxid=None), "00006")
        self.bad(invoice(ins=2, irtaxid=new_taxid()), "0300601", "REF_NOT_FOUND")

    def test_rejected_reference_is_not_found(self):
        inv = invoice(tinb="1")
        submit(inv)
        self.bad(referral_of(inv, 2), "0300601", "REF_NOT_FOUND")

    def test_reference_is_cancellation(self):
        base = self.original()
        can = cancellation_of(base)
        self.ok(can)
        self.bad(invoice(ins=2, irtaxid=can["header"]["taxid"]), "0300601", "REF_IS_CANCELLATION")

    def test_reference_canceled(self):
        base = self.original()
        self.ok(cancellation_of(base))
        self.bad(referral_of(base, 2), "0300601", "REF_CANCELED")

    def test_original_reference_any_status(self):
        for st in (M.AWAITING, M.REJECTED, M.APPROVED, M.NO_NEED):
            M.reset_state()
            base = self.original()
            M.STATE["invoices"][base["header"]["taxid"]]["invoiceStatus"] = st
            self.ok(referral_of(base, 2, lines=[line(am=11)]))

    def test_referral_as_reference_needs_final_status(self):
        for st, accept in ((M.AWAITING, False), (M.REJECTED, False), (M.APPROVED, True),
                           (M.SYSTEMIC, True), (M.NO_NEED, True), (M.IMPOSSIBLE, True)):
            for new_ins in (2, 4):
                M.reset_state()
                base = self.original()
                c1 = referral_of(base, 2, lines=[line(am=12)])
                self.ok(c1)
                M.STATE["invoices"][c1["header"]["taxid"]]["invoiceStatus"] = st
                lines = [line(am=12)] if new_ins == 2 else [line(am=11)]
                inv = referral_of(c1, new_ins, lines=lines)
                if accept:
                    self.ok(inv)
                else:
                    self.bad(inv, "0300601", "REF_REFERRAL_STATUS")

    def test_seller(self):
        base = self.original()
        self.bad(referral_of(base, 2, tins="22222222222"), "0300902")
        self.bad(cancellation_of(base, tins="22222222222"), "0300902")

    def test_type_pattern_buyer_unchanged(self):
        base = self.original(tob=1, tinb=None, bid="0012345678", bpc="1234567890")
        self.bad(referral_of(base, 2, inty=2), "0300401")
        self.bad(referral_of(base, 2, inp=2), "0300701")
        self.bad(referral_of(base, 2, tob=4, bid="123456789012"), "0301001")
        self.bad(referral_of(base, 2, tinb="12345678901234"), "0301201")
        self.bad(referral_of(base, 2, bid="0099999999"), "0301101")
        self.bad(referral_of(base, 4, lines=[line(am=5)], bid="0099999999"), "0301101")

    def test_inno_serial_warning(self):
        inv = invoice()
        inv["header"]["taxid"] = new_taxid(serial="00ABCDEF12")      # serial != inno
        rec = self.ok(inv, "1300501")
        self.assertEqual(rec["errors"], [])
        self.assertEqual(rec["warnings"][0]["rule"], "INNO_SERIAL")
        rec = self.ok(invoice(inno="00abcdef12", taxid=new_taxid(serial="00ABCDEF12")))
        self.assertNotIn("1300501", codes(rec))                       # case-insensitive match

    def test_old_reference_inno_rule_is_gone(self):
        base = self.original()
        rec = self.ok(referral_of(base, 2, inno="1405000999", lines=[line(am=11)]))
        self.assertNotIn("1300501", codes(rec))                       # own serial matches
        self.assertNotIn("INNO_REF", M.RULES)

    def test_correction_items(self):
        base = self.original()
        self.ok(referral_of(base, 2, lines=[line(am=20)]))              # increase ok
        M.reset_state()
        base = self.original()
        self.bad(referral_of(base, 2, lines=[line(sstid="2820000000001")]), "0303301")
        self.bad(referral_of(base, 2, lines=[line(vra=10)]), "0304401", "CORR_VRA")

    def test_sales_return(self):
        base = self.original(lines=[line(am=10), line(am=4, sstid="2820000000009")])
        self.bad(referral_of(base, 4, lines=[line(am=11), line(am=4, sstid="2820000000009")]),
                 "0303601", "RET_AM_UP")
        self.bad(referral_of(base, 4), "0303601", "RET_NO_DECREASE")
        self.bad(referral_of(base, 4, lines=[line(am=9, vra=10), line(am=4, sstid="2820000000009")]),
                 "0304401", "RET_VRA")
        self.bad(referral_of(base, 4, lines=[line(am=9, fee=90000), line(am=4, sstid="2820000000009")]),
                 "0303701")
        self.bad(referral_of(base, 4, lines=[line(am=9), line(am=4, sstid="2820000000008")]),
                 "0303301", "RET_NEW_SSTID")
        self.bad(referral_of(base, 4, lines=[line(am=0), line(am=0, sstid="2820000000009")]),
                 "0303602")
        self.bad(referral_of(base, 4, lines=[]), "0303602")
        self.ok(referral_of(base, 4, lines=[line(am=10)]))      # 2nd item fully returned

    def test_sales_return_decrease_ok(self):
        base = self.original(lines=[line(am=10), line(am=4, sstid="2820000000009")])
        self.ok(referral_of(base, 4, lines=[line(am=9), line(am=4, sstid="2820000000009")]))

    def test_sales_return_same_sstid_twice(self):
        base = self.original(lines=[line(am=3, fee=33333), line(am=3, fee=33333)])
        self.ok(referral_of(base, 4, lines=[line(am=3, fee=33333), line(am=2, fee=33333)]))


class T10_SingleUse(Base):
    def test_on(self):
        base = self.original()
        self.ok(referral_of(base, 2, lines=[line(am=11)]))
        self.bad(referral_of(base, 2, lines=[line(am=12)]), "0300601", "REF_SINGLE_USE")
        self.bad(referral_of(base, 4, lines=[line(am=9)]), "0300601", "REF_SINGLE_USE")

    def test_off(self):
        M.CONFIG["single_use_reference"] = False
        base = self.original()
        self.ok(referral_of(base, 2, lines=[line(am=11)]))
        self.ok(referral_of(base, 2, lines=[line(am=12)]))

    def test_rejected_referral_frees_reference(self):
        base = self.original()
        c1 = referral_of(base, 2, lines=[line(am=11)])
        self.ok(c1)
        self.assertTrue(M.buyer_action(c1["header"]["taxid"], "reject")["ok"])
        self.ok(referral_of(base, 2, lines=[line(am=12)]))

    def test_default_is_on_and_reset_restores(self):
        self.assertTrue(M.SINGLE_USE_REFERENCE)
        M.CONFIG["single_use_reference"] = False
        M.reset_state()
        self.assertTrue(M.CONFIG["single_use_reference"])


# ---------------------------------------------------------------------------
# 6. dashboard state machine
# ---------------------------------------------------------------------------

class T11_Dashboard(Base):
    def test_initial_statuses(self):
        a = self.original()
        self.assertEqual(dash(a["header"]["taxid"]), M.AWAITING)
        b = self.original(inty=2, tob=None, tinb=None)
        self.assertEqual(dash(b["header"]["taxid"]), M.NO_NEED)
        c = self.original(tob=1, tinb=None, bid="0012345678", bpc="1234567890")
        self.assertEqual(dash(c["header"]["taxid"]), M.NO_NEED)
        can = cancellation_of(a)
        self.ok(can)
        self.assertEqual(dash(can["header"]["taxid"]), M.NO_NEED)

    def test_processing_status_separate(self):
        a = self.original()
        rec = M.STATE["invoices"][a["header"]["taxid"]]
        self.assertEqual(rec["status"], "IN_PROGRESS")
        self.assertEqual(M.poll_processing(rec), "SUCCESS")
        self.assertEqual(rec["invoiceStatus"], M.AWAITING)
        M.CONFIG["in_progress_polls"] = 2
        b = self.original()
        rb = M.STATE["invoices"][b["header"]["taxid"]]
        self.assertEqual([M.poll_processing(rb) for _ in range(3)],
                         ["IN_PROGRESS", "IN_PROGRESS", "SUCCESS"])

    def test_buyer_approve_reject(self):
        a, b = self.original(), self.original()
        r = M.buyer_action(a["header"]["taxid"], "approve")
        self.assertEqual((r["ok"], r["status"]), (True, M.APPROVED))
        r = M.buyer_action(b["header"]["taxid"], "reject")
        self.assertEqual((r["ok"], r["status"]), (True, M.REJECTED))
        r = M.buyer_action(a["header"]["taxid"], "reject")
        self.assertFalse(r["ok"])
        self.assertEqual(r["error"], "NOT_AWAITING_REACTION")
        self.assertEqual(M.buyer_action(new_taxid(), "approve")["error"], "NOT_FOUND")

    def test_no_reaction_on_type2(self):
        b = self.original(inty=2, tob=None, tinb=None)
        self.assertFalse(M.buyer_action(b["header"]["taxid"], "approve")["ok"])

    def test_system_approval_after_30_days(self):
        a, b = self.original(), self.original()
        M.advance_days(29)
        self.assertEqual(dash(a["header"]["taxid"]), M.AWAITING)
        self.assertTrue(M.buyer_action(b["header"]["taxid"], "reject")["ok"])
        M.advance_days(1)
        self.assertEqual(dash(a["header"]["taxid"]), M.SYSTEMIC)
        self.assertEqual(dash(b["header"]["taxid"]), M.REJECTED)
        r = M.buyer_action(a["header"]["taxid"], "approve")
        self.assertFalse(r["ok"])

    def test_system_approval_via_clock_offset_config(self):
        a = self.original()
        M.CONFIG["clock_offset_ms"] = 31 * DAY
        M.apply_time()
        self.assertEqual(dash(a["header"]["taxid"]), M.SYSTEMIC)

    def test_reaction_window_guard(self):
        a = self.original()
        rec = M.STATE["invoices"][a["header"]["taxid"]]
        r = M.buyer_action(a["header"]["taxid"], "approve", now=rec["receivedAt"] + 30 * DAY)
        self.assertFalse(r["ok"])

    def test_approved_referral_cancels_reference(self):
        base = self.original()
        c = referral_of(base, 2, lines=[line(am=11)])
        self.ok(c)
        self.assertEqual(dash(base["header"]["taxid"]), M.AWAITING)
        M.buyer_action(c["header"]["taxid"], "approve")
        self.assertEqual(dash(base["header"]["taxid"]), M.CANCELED)
        # ... and it cannot be used as a reference again (FAQ 4-9 b)
        M.CONFIG["single_use_reference"] = False
        self.bad(referral_of(base, 2, lines=[line(am=12)]), "0300601", "REF_CANCELED")

    def test_system_approved_referral_cancels_reference(self):
        base = self.original()
        c = referral_of(base, 4, lines=[line(am=9)])
        self.ok(c)
        M.advance_days(30)
        self.assertEqual(dash(c["header"]["taxid"]), M.SYSTEMIC)
        self.assertEqual(dash(base["header"]["taxid"]), M.CANCELED)

    def test_rejected_referral_leaves_reference(self):
        base = self.original()
        c = referral_of(base, 2, lines=[line(am=11)])
        self.ok(c)
        M.buyer_action(c["header"]["taxid"], "reject")
        self.assertEqual(dash(base["header"]["taxid"]), M.AWAITING)

    def test_type2_referral_consumes_at_issuance(self):
        base = self.original(inty=2, tob=None, tinb=None)
        c = referral_of(base, 2, lines=[line(am=11)])
        self.ok(c)
        self.assertEqual(dash(c["header"]["taxid"]), M.NO_NEED)
        self.assertEqual(dash(base["header"]["taxid"]), M.CANCELED)
        self.ok(referral_of(c, 2, lines=[line(am=12)]))     # chain continues on the referral

    def test_set_status_legacy_names(self):
        a = self.original()
        t = a["header"]["taxid"]
        for old, new in (("CONFIRMED", M.APPROVED), ("SYSTEM_CONFIRMED", M.SYSTEMIC),
                         ("NO_REACTION_NEEDED", M.NO_NEED), ("CANCELLED", M.CANCELED)):
            self.assertTrue(M.set_status(t, old))
            self.assertEqual(dash(t), new)
        self.assertTrue(M.set_status(t, "IMPOSSIBLE_REACTION"))
        self.assertFalse(M.set_status(t, "BOGUS"))
        self.assertFalse(M.set_status(new_taxid(), "APPROVED"))
        self.assertTrue(M.set_status(t, "SUCCESS"))
        self.assertEqual(M.STATE["invoices"][t]["status"], "SUCCESS")

    def test_invoice_status_shape(self):
        a = self.original()
        out = M.invoice_status([a["header"]["taxid"], "NOPE"])
        self.assertEqual(out[0], {"taxId": a["header"]["taxid"], "invoiceStatus": M.AWAITING,
                                  "article6Status": "NOT_EXCEEDED", "error": None})
        self.assertEqual(out[1]["error"], "NOT_FOUND")
        self.assertIsNone(out[1]["invoiceStatus"])


# ---------------------------------------------------------------------------
# 7. FAQ 4-9 cancellation matrix
# ---------------------------------------------------------------------------

class T12_Cancellation(Base):
    def test_original_any_status(self):
        for st in (M.AWAITING, M.APPROVED, M.REJECTED, M.SYSTEMIC, M.NO_NEED, M.IMPOSSIBLE):
            M.reset_state()
            base = self.original()
            M.STATE["invoices"][base["header"]["taxid"]]["invoiceStatus"] = st
            can = cancellation_of(base)
            self.ok(can)
            self.assertEqual(dash(base["header"]["taxid"]), M.CANCELED)
            self.assertEqual(dash(can["header"]["taxid"]), M.NO_NEED)

    def test_original_with_awaiting_referral(self):
        base = self.original()
        c = referral_of(base, 2, lines=[line(am=11)])
        self.ok(c)
        self.ok(cancellation_of(base))
        self.assertEqual(dash(base["header"]["taxid"]), M.CANCELED)
        self.assertEqual(dash(c["header"]["taxid"]), M.CANCELED)     # IS_V7.8 p16 item 3

    def test_original_with_rejected_referral(self):
        base = self.original()
        c = referral_of(base, 2, lines=[line(am=11)])
        self.ok(c)
        M.buyer_action(c["header"]["taxid"], "reject")
        self.ok(cancellation_of(base))
        self.assertEqual(dash(c["header"]["taxid"]), M.CANCELED)

    def test_original_with_approved_referral(self):
        base = self.original()
        c = referral_of(base, 2, lines=[line(am=11)])
        self.ok(c)
        M.buyer_action(c["header"]["taxid"], "approve")
        self.bad(cancellation_of(base), "0300601", "REF_CANCELED")

    def test_consumed_guard_even_without_cascade(self):
        base = self.original()
        c = referral_of(base, 2, lines=[line(am=11)])
        self.ok(c)
        M.STATE["invoices"][c["header"]["taxid"]]["invoiceStatus"] = M.APPROVED   # raw, no cascade
        self.bad(cancellation_of(base), "0300601", "CANCEL_CONSUMED")

    def test_referral_awaiting_cancellable(self):
        base = self.original()
        c = referral_of(base, 2, lines=[line(am=11)])
        self.ok(c)
        self.ok(cancellation_of(c))
        self.assertEqual(dash(c["header"]["taxid"]), M.CANCELED)
        self.assertEqual(dash(base["header"]["taxid"]), M.AWAITING)

    def test_referral_rejected_not_cancellable(self):
        base = self.original()
        c = referral_of(base, 2, lines=[line(am=11)])
        self.ok(c)
        M.buyer_action(c["header"]["taxid"], "reject")
        self.bad(cancellation_of(c), "0300601", "CANCEL_TARGET_STATUS")

    def test_referral_approved_is_reference_role(self):
        base = self.original()
        c = referral_of(base, 2, lines=[line(am=11)])
        self.ok(c)
        M.buyer_action(c["header"]["taxid"], "approve")
        self.ok(cancellation_of(c))

    def test_cannot_cancel_cancellation_or_twice(self):
        base = self.original()
        can = cancellation_of(base)
        self.ok(can)
        self.bad(cancellation_of(can), "0300601", "REF_IS_CANCELLATION")
        self.bad(cancellation_of(base), "0300601", "REF_CANCELED")

    def test_no_body_or_amounts_needed(self):
        base = self.original()
        can = cancellation_of(base)
        can["header"].update({"tbill": Decimal(1), "setm": 9, "inty": 7, "tob": 9, "tinb": "x"})
        self.ok(can)

    def test_cancellation_required_fields(self):
        base = self.original()
        can = cancellation_of(base)
        can["header"]["tins"] = None
        self.bad(can, "00009")
        can = cancellation_of(base)
        can["header"]["irtaxid"] = None
        self.bad(can, "00006")


# ---------------------------------------------------------------------------
# 8. message cap
# ---------------------------------------------------------------------------

class T13_MessageCap(Base):
    def _many(self, n):
        lines = [line() for _ in range(n)]
        for l in lines:
            l["prdis"] += 1
            l["adis"] += 1
            l["vam"] = T(l["adis"] * 9 / 100)
            l["tsstam"] = l["adis"] + l["vam"]
        return invoice(lines=lines)

    def test_cap_50_errors_first(self):
        inv = self._many(55)
        inv["header"]["irtaxid"] = new_taxid()          # adds a warning (14006)
        rec = submit(inv)
        self.assertFalse(rec["success"])
        self.assertEqual(len(rec["errors"]), 50)
        self.assertEqual([w["code"] for w in rec["warnings"]], ["00000"])
        self.assertEqual(len(rec["errors"]) + len(rec["warnings"]) - 1, 50)

    def test_no_marker_at_exactly_50(self):
        rec = submit(self._many(50))
        self.assertEqual(len(rec["errors"]), 50)
        self.assertNotIn("00000", codes(rec))


# ---------------------------------------------------------------------------
# 9. end-to-end HTTP with real client-side crypto
# ---------------------------------------------------------------------------

def client_encrypt(pub_b64, obj):
    """Mirror of the SDK: xor, AES-GCM, RSA-OAEP-SHA256(hexUpper(aesKey))."""
    pub = serialization.load_der_public_key(base64.b64decode(pub_b64))
    aes = os.urandom(32)
    iv = os.urandom(16)
    plain = json.dumps(obj, ensure_ascii=False, default=M._json_default).encode("utf-8")
    cipher = AESGCM(aes).encrypt(iv, M.mm.xor_blocks(plain, aes), None)
    sym = pub.encrypt(aes.hex().upper().encode(),
                      padding.OAEP(mgf=padding.MGF1(algorithm=hashes.SHA256()),
                                   algorithm=hashes.SHA256(), label=None))
    return {"symmetricKey": base64.b64encode(sym).decode(), "iv": iv.hex().upper(),
            "data": base64.b64encode(cipher).decode()}


PRODUCTION_SUCCESS_PROFILE = ("14800", "14029", "14030", "14003", "1300501")


class T13b_OutOfPattern(Base):
    """Kind-4 warnings from production TAXDTL. All verdict-neutral."""

    def test_client_shaped_reproduces_production_profile(self):
        rec = self.ok(client_shaped())
        self.assertEqual(rec["errors"], [])
        self.assertEqual(sorted(codes(rec)), sorted(PRODUCTION_SUCCESS_PROFILE))
        for w in rec["warnings"]:
            self.assertEqual(w["errorType"], "WARNING")

    def test_extension(self):
        inv = invoice()
        inv["extension"] = [{}]
        self.ok(inv, "14800")
        for ext in ([], None):
            inv = invoice()
            inv["extension"] = ext
            self.assertNotIn("14800", codes(self.ok(inv)))

    def test_cap_insp_only_outside_setm3(self):
        inv = invoice(setm=3)
        basis = inv["header"]["tbill"] - inv["header"]["tvam"] - inv["header"]["todam"]
        inv["header"]["cap"], inv["header"]["insp"] = basis - 1, Decimal(1)
        rec = self.ok(inv)
        self.assertNotIn("14029", codes(rec))
        self.assertNotIn("14030", codes(rec))
        rec = self.ok(invoice(setm=2, cap=Decimal(0)), "14029")
        self.assertNotIn("14030", codes(rec))

    def test_indati2m(self):
        inv = invoice()
        inv["header"]["indati2m"] = inv["header"]["indatim"]
        self.ok(inv, "14003")
        inv = invoice(insr=1)
        inv["header"]["indati2m"] = inv["header"]["indatim"]
        self.assertNotIn("14003", codes(self.ok(inv)))

    def test_body_gold_fields(self):
        for f, c in (("consfee", "14052"), ("spro", "14053"), ("bros", "14054"), ("tcpbs", "14055")):
            l1, l2 = line(), line(am=3)
            l1[f] = l2[f] = Decimal(0)
            rec = self.ok(invoice(lines=[l1, l2]), c)
            self.assertEqual(codes(rec).count(c), 2)              # per line
            l3 = line()
            l3[f] = Decimal(0)
            self.assertNotIn(c, codes(self.ok(invoice(inp=3, lines=[l3]))))

    def test_cancellation_header_fields(self):
        base = self.original()
        can = cancellation_of(base)
        for f in M.CANCEL_OOP_FIELDS:
            can["header"][f] = Decimal(0)
        rec = self.ok(can)
        got = sorted(c for c in codes(rec) if c.startswith("14"))
        self.assertEqual(got, ["14022", "14023", "14024", "14025", "14026", "14027", "14032"])
        # an original with the same fields gets none of them
        rec = self.ok(invoice(tax17=Decimal(0)))
        self.assertFalse([c for c in codes(rec) if c.startswith("140")])

    def test_never_change_the_verdict(self):
        good = client_shaped()
        for l in good["body"]:
            for f in ("consfee", "spro", "bros", "tcpbs"):
                l[f] = Decimal(0)
        self.assertTrue(submit(good)["success"])
        bad = client_shaped(tinb="12")
        rec = submit(bad)
        self.assertFalse(rec["success"])
        self.assertEqual([m["code"] for m in rec["errors"]], ["0101204"])
        for m in rec["warnings"]:
            self.assertTrue(m["code"].startswith("1"), m)
        for rid, r in M.RULES.items():
            if rid.startswith("OOP_") or rid == "INNO_SERIAL":
                self.assertTrue(r["warning"], rid)


class T14_Http(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        class S(M.ThreadingHTTPServer):
            daemon_threads = True
        cls.srv = S(("127.0.0.1", 0), M.Handler)
        cls.base = "http://127.0.0.1:%d/" % cls.srv.server_address[1]
        threading.Thread(target=cls.srv.serve_forever, daemon=True).start()

    @classmethod
    def tearDownClass(cls):
        cls.srv.shutdown()
        cls.srv.server_close()

    def call(self, route, body=None):
        data = None if body is None else json.dumps(body, default=M._json_default).encode()
        req = urllib.request.Request(self.base + route, data=data,
                                     headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=10) as r:
            return json.loads(r.read().decode("utf-8"))

    def send(self, inv):
        info = self.call("req/api/self-tsp/sync/GET_SERVER_INFORMATION", {})
        pub = info["result"]["data"]["publicKeys"][0]["key"]
        pkt = client_encrypt(pub, inv)
        pkt.update({"uid": M.uuid.uuid4().hex, "fiscalId": "A11216", "packetType": "INVOICE.V01",
                    "encryptionKeyId": "mock-key-0001"})
        resp = self.call("req/api/self-tsp/async/normal-enqueue", {"packets": [pkt]})
        ref = resp["result"][0]["referenceNumber"]
        self.assertIsNotNone(ref, resp)
        inq = self.call("req/api/self-tsp/sync/INQUIRY_BY_REFERENCE_NUMBER",
                        {"packet": {"data": {"referenceNumber": [ref]}}})
        return ref, inq["result"]["data"][0]

    def test_accept_decrypt_and_dashboard(self):
        self.call("__reset", {})
        inv = client_shaped(lines=[line(am="1.5", fee=33333)])    # Decimal path over the wire
        ref, res = self.send(inv)
        self.assertEqual(res["status"], "SUCCESS", res)
        self.assertTrue(res["data"]["success"])
        self.assertEqual(sorted(w["code"] for w in res["data"]["warning"]),
                         sorted(PRODUCTION_SUCCESS_PROFILE))
        state = self.call("__state")
        rec = state["invoices"][inv["header"]["taxid"]]
        for k in ("reference", "uid", "taxid", "ins", "inno", "irtaxid", "errors", "warnings",
                  "success", "status"):
            self.assertIn(k, rec)
        self.assertEqual(rec["reference"], ref)
        payload = self.call("__payloads")[inv["header"]["taxid"]]
        self.assertEqual(payload["body"][0]["am"], 1.5)
        st = self.call("__invoice_status?taxIds=%s&taxIds=NOPE" % inv["header"]["taxid"])
        self.assertEqual(st[0]["invoiceStatus"], "AWAITING_REACTION")
        self.assertEqual(st[1]["error"], "NOT_FOUND")

        t0 = self.call("__clock")["serverTime"]
        self.call("__advance_days", {"days": 30})
        self.assertGreaterEqual(self.call("__clock")["serverTime"] - t0, 30 * DAY)
        st = self.call("__invoice_status?taxIds=%s" % inv["header"]["taxid"])
        self.assertEqual(st[0]["invoiceStatus"], "SYSTEMIC_APPROVED")
        r = self.call("__buyer_action", {"taxid": inv["header"]["taxid"], "action": "approve"})
        self.assertFalse(r["ok"])
        self.call("__reset", {})
        self.assertEqual(self.call("__clock")["offsetMs"], 0)
        self.assertEqual(self.call("__state")["invoices"], {})

    def test_reject_over_the_wire_and_controls(self):
        self.call("__reset", {})
        bad = invoice(tinb="021-555")
        ref, res = self.send(bad)
        self.assertEqual(res["status"], "FAILED")
        self.assertIn("0101204", [e["code"] for e in res["data"]["error"]])
        self.assertIn(ref, self.call("__state")["rejected"])

        good = invoice()
        self.send(good)
        t = good["header"]["taxid"]
        r = self.call("__buyer_action", {"taxid": t, "action": "reject"})
        self.assertEqual((r["ok"], r["status"]), (True, "REJECTED"))
        r = self.call("__set_status", {"taxid": t, "status": "CONFIRMED"})
        self.assertEqual((r["ok"], r["invoiceStatus"]), (True, "APPROVED"))
        cfg = self.call("__config", {"single_use_reference": False, "deadline_days": 5})
        self.assertEqual((cfg["single_use_reference"], cfg["deadline_days"]), (False, 5))
        cfg = self.call("__config", {"clock_offset_ms": -120000})
        _, res = self.send(invoice(indatim=M.real_now_ms()))
        self.assertIn("0200201", [e["code"] for e in res["data"]["error"]])
        self.assertTrue(self.call("__ping")["ok"])
        self.call("__reset", {})


class T15_Faults(unittest.TestCase):
    """Fault injection (__config fault_*) and __stats. Faults model generic HTTP
    failures, not documented behaviour of the real system."""

    setUpClass = classmethod(T14_Http.setUpClass.__func__)
    tearDownClass = classmethod(T14_Http.tearDownClass.__func__)
    call = T14_Http.call

    def setUp(self):
        self.call("__reset", {})
        self.pub = self.call("req/api/self-tsp/sync/GET_SERVER_INFORMATION",
                             {})["result"]["data"]["publicKeys"][0]["key"]

    def tearDown(self):
        self.call("__reset", {})

    def packets(self, *invs):
        out = []
        for inv in invs:
            pkt = client_encrypt(self.pub, inv)
            pkt.update({"uid": M.uuid.uuid4().hex, "fiscalId": "A11216",
                        "packetType": "INVOICE.V01", "encryptionKeyId": "mock-key-0001"})
            out.append(pkt)
        return out

    def raw(self, route, body, timeout=10):
        """(status, json) without raising on HTTP errors."""
        data = json.dumps(body, default=M._json_default).encode()
        req = urllib.request.Request(self.base + route, data=data,
                                     headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return r.status, json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            return e.code, json.loads(e.read().decode("utf-8"))

    def enqueue(self, *invs, timeout=10):
        return self.raw("req/api/self-tsp/async/normal-enqueue",
                        {"packets": self.packets(*invs)}, timeout=timeout)

    def inquire(self, refs):
        return self.raw("req/api/self-tsp/sync/INQUIRY_BY_REFERENCE_NUMBER",
                        {"packet": {"data": {"referenceNumber": refs}}})

    def taxids(self):
        return set(self.call("__state")["invoices"])

    def test_defaults_off_and_reset_turns_off(self):
        cfg = self.call("__config")
        for k in M.FAULT_KEYS:
            self.assertIn(k, cfg)
        self.assertEqual([cfg[k] for k in ("fault_http_500_next", "fault_timeout_next",
                                           "fault_drop_result_next", "fault_duplicate_reference")],
                         [0, 0, 0, 0])
        self.assertEqual(cfg["fault_timeout_seconds"], 35)
        self.call("__config", {"fault_http_500_next": 3, "fault_timeout_next": 2,
                               "fault_timeout_seconds": 1, "fault_drop_result_next": 4,
                               "fault_duplicate_reference": 1})
        self.call("__reset", {})
        cfg = self.call("__config")
        self.assertEqual({k: cfg[k] for k in M.FAULT_KEYS},
                         {k: M.DEFAULT_CONFIG[k] for k in M.FAULT_KEYS})
        st = self.call("__stats")
        self.assertEqual((st["packets_per_send_request"], st["faults_fired"],
                          st["registered_despite_fault"]), ([], {}, []))
        # and with everything off a send is perfectly normal
        code, resp = self.enqueue(invoice())
        self.assertEqual(code, 200)
        self.assertIsNotNone(resp["result"][0]["referenceNumber"])

    def test_http_500_next_n(self):
        self.call("__config", {"fault_http_500_next": 2})
        a, b, c = invoice(), invoice(), invoice()
        code, resp = self.enqueue(a)
        self.assertEqual(code, 500)
        self.assertIsNone(resp["result"])
        self.assertEqual(resp["errors"][0]["errorCode"], "0000500")
        code, _ = self.inquire(["X"])                        # inquiries count too
        self.assertEqual(code, 500)
        self.assertEqual(self.taxids(), set(), "a 500 send must NOT be registered")
        code, resp = self.enqueue(b)                         # fault exhausted
        self.assertEqual(code, 200)
        ref = resp["result"][0]["referenceNumber"]
        code, inq = self.inquire([ref])
        self.assertEqual((code, inq["result"]["data"][0]["status"]), (200, "SUCCESS"))
        self.assertEqual(self.taxids(), {b["header"]["taxid"]})
        self.assertEqual(self.call("__config")["fault_http_500_next"], 0)
        self.assertEqual(self.call("__stats")["faults_fired"], {"fault_http_500_next": 2})
        del c

    def test_timeout_registers_but_client_gives_up(self):
        import socket
        self.call("__config", {"fault_timeout_next": 1, "fault_timeout_seconds": 0.6})
        inv = invoice()
        with self.assertRaises((socket.timeout, TimeoutError, urllib.error.URLError)):
            self.enqueue(inv, timeout=0.15)
        # the server DID register it — the client's outcome is unknown, not "failed"
        self.assertIn(inv["header"]["taxid"], self.taxids())
        st = self.call("__stats")
        self.assertEqual(st["faults_fired"], {"fault_timeout_next": 1})
        self.assertEqual([(r["taxid"], r["fault"], r["accepted"]) for r in st["registered_despite_fault"]],
                         [(inv["header"]["taxid"], "timeout", True)])
        # a patient client gets the normal envelope back
        self.call("__config", {"fault_timeout_next": 1, "fault_timeout_seconds": 0.2})
        inv2 = invoice()
        code, resp = self.enqueue(inv2, timeout=5)
        self.assertEqual(code, 200)
        self.assertIsNotNone(resp["result"][0]["referenceNumber"])
        # resending the timed-out invoice with the SAME taxid is rejected as duplicate
        # (0300101) — which is exactly why RESEND keeps the taxid (CLAUDE.md §4)
        code, resp = self.enqueue(inv)
        _, inq = self.inquire([resp["result"][0]["referenceNumber"]])
        self.assertIn("0300101", [e["code"] for e in inq["result"]["data"][0]["data"]["error"]])

    def test_drop_result(self):
        self.call("__config", {"fault_drop_result_next": 1})
        a, b = invoice(), invoice()
        code, resp = self.enqueue(a, b)
        self.assertEqual((code, resp["result"], resp["errors"]), (200, [], []))
        self.assertEqual(self.taxids(), {a["header"]["taxid"], b["header"]["taxid"]})
        st = self.call("__stats")
        self.assertEqual(sorted(r["fault"] for r in st["registered_despite_fault"]),
                         ["drop_result", "drop_result"])
        code, resp = self.enqueue(invoice())
        self.assertEqual(len(resp["result"]), 1)

    def test_duplicate_reference(self):
        self.call("__config", {"fault_duplicate_reference": 1})
        invs = [invoice(), invoice(), invoice(tinb="021-555")]      # the last one is rejected
        code, resp = self.enqueue(*invs)
        refs = {r["referenceNumber"] for r in resp["result"]}
        self.assertEqual((code, len(resp["result"]), len(refs)), (200, 3, 1))
        self.assertEqual(self.taxids(), {i["header"]["taxid"] for i in invs[:2]})
        _, inq = self.inquire(list(refs))
        data = inq["result"]["data"]
        self.assertEqual(len(data), 3, "one shared reference must answer for every packet")
        self.assertEqual(sorted(d["status"] for d in data), ["FAILED", "SUCCESS", "SUCCESS"])
        self.assertEqual(len({d["uid"] for d in data}), 3)
        code, resp = self.enqueue(invoice(), invoice())                # one-shot
        self.assertEqual(len({r["referenceNumber"] for r in resp["result"]}), 2)

    def test_stats(self):
        self.enqueue(*[invoice() for _ in range(3)])
        self.enqueue(invoice())
        self.call("__config", {"fault_http_500_next": 1})
        self.enqueue(invoice(), invoice())          # counted even though it failed
        self.inquire(["NOPE"])
        st = self.call("__stats")
        self.assertEqual(st["packets_per_send_request"], [3, 1, 2])
        routes = st["requests_by_route"]
        self.assertEqual([v for k, v in routes.items() if k.endswith("normal-enqueue")], [3])
        self.assertEqual([v for k, v in routes.items()
                          if k.endswith("INQUIRY_BY_REFERENCE_NUMBER")], [1])
        self.assertEqual(st["faults_fired"], {"fault_http_500_next": 1})  # the send took it
        self.assertEqual(max(st["packets_per_send_request"]), 3)

    def test_in_progress_over_http(self):
        self.call("__config", {"in_progress_polls": 1, "empty_data_when_in_progress": True})
        _, resp = self.enqueue(invoice())
        ref = resp["result"][0]["referenceNumber"]
        _, inq = self.inquire([ref])
        self.assertEqual((inq["result"]["data"][0]["status"], inq["result"]["data"][0]["data"]),
                         ("IN_PROGRESS", {}))
        _, inq = self.inquire([ref])
        self.assertEqual(inq["result"]["data"][0]["status"], "SUCCESS")


# ---------------------------------------------------------------------------

EXPECTED_UNHIT = set()   # every registered rule must be exercised


def main():
    suite = unittest.defaultTestLoader.loadTestsFromModule(sys.modules[__name__])
    result = unittest.TextTestRunner(verbosity=1).run(suite)
    missing = set(M.RULES) - HIT_RULES - EXPECTED_UNHIT
    if missing:
        print("RULES NEVER EXERCISED:", sorted(missing))
    print("tests run: %d, failures: %d, errors: %d, rules hit: %d/%d"
          % (result.testsRun, len(result.failures), len(result.errors),
             len(HIT_RULES & set(M.RULES)), len(M.RULES)))
    return 0 if result.wasSuccessful() and not missing else 1


if __name__ == "__main__":
    sys.exit(main())
