#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
oracle_amounts.py — an INDEPENDENT second implementation of the invoice amount
arithmetic, used to cross-check what the C# client puts on the wire.

    python3.12 oracle_amounts.py check  <payloads.json> [--source rows.json] [--json]
    python3.12 oracle_amounts.py fetch  http://127.0.0.1:9190/  [--source rows.json] [--json]

`payloads.json` is a dict taxid -> invoice exactly as the full mock serves it at
GET __payloads.  Exit code 1 when any mismatch is found (known divergences alone
do NOT fail).

----------------------------------------------------------------------------
INDEPENDENCE — how the formulas below were obtained
----------------------------------------------------------------------------
Written WITHOUT opening CorrectionMath.cs, SendInvoiceBulk.cs or CL_MOADIAN.cs.
Every formula is derived only from:
  [C1]  CLAUDE.md §1/§5: amounts use truncation (Math.Truncate), never rounding;
        «با روش قطع کردن محاسبه».
  [C5a] CLAUDE.md §5 item 1 / V7.9 p73 table 53 row 1:
        tsstam = adis + vam + odam + olam   (Os = Ks + Is + Ks2 + Ks3)
  [C5b] CLAUDE.md §5: cap + insp = tbill − tvam − todam, ONLY for setm = 3
        (V7.9 p46 table 25, p47 table 27).
  [F94] FAQ 9-4: every rial value except the unit price (fee) has 0 decimals,
        obtained by truncation; currency values carry up to 4 decimals.
  [DEF] The field definitions themselves (IS field names): prdis = amount before
        discount = am × fee; adis = amount after discount = prdis − dis;
        vam = VAT amount = adis × vra / 100; header t* fields are the sums of the
        corresponding line fields (tprdis=Σprdis, tdis=Σdis, tadis=Σadis,
        tvam=Σvam, todam=Σ(odam+olam), tbill=Σtsstam).
  [OPS] Operational facts from CLAUDE.md §5/§6: in all 18,736 production rows
        odam/olam are 0; setm=3 has zero production samples.

Arithmetic uses decimal.Decimal only (never float); JSON is parsed with
parse_float=Decimal so 5.75 stays exactly 5.75.

----------------------------------------------------------------------------
KNOWN DIVERGENCE (a separate verdict class, NOT an error)
----------------------------------------------------------------------------
CLAUDE.md §5 item 1: the client builds tsstam = adis + vam (odam/olam left out)
and tbill without todam, whereas the document says adis + vam + odam + olam.
When odam/olam are all zero the two coincide and nothing is reported. When they
are non-zero and the sent value matches the CLIENT variant exactly, the oracle
reports `known_divergence` instead of a mismatch. A value matching neither
variant is a mismatch.

----------------------------------------------------------------------------
KNOWN DIVERGENCES FROM THE C# CODE — written AFTER the formulas above were
finished and tested, by reading SendInvoiceBulk.cs (lines as of 2026-09-24).
Nothing above was changed as a result.
----------------------------------------------------------------------------
  D1 (encoded above, class known_divergence): SendInvoiceBulk.cs:481
     mabkn = mabkbt + IMBAA -> tsstam = adis + vam; :699 tbill = Σ mabkn.
  D2 (NOT encoded — reported as a mismatch on purpose): SendInvoiceBulk.cs:473-480
     recomputes vam only when the STORED IMBAA > 0. A line with vra > 0 and a
     stored vam of 0 is sent with vam = 0 after the user answers "continue" to
     the warning at :466-471. The oracle expects trunc(adis*vra/100) and flags
     it (rule VAM=TRUNC(ADIS*VRA/100)). Whether the real system rejects such a
     line is unknown (no operational sample). Same rule family as CLAUDE.md §4
     "known divergence" (correction form recomputes unconditionally).
  D3 (input, not output): SendInvoiceBulk.cs:397 rounds am (MEGHk) to 4
     decimals with Math.Round BEFORE prdis = trunc(am*fee) (:405). The oracle
     works on the wire am, so it agrees; with --source raw rows whose am has
     more than 4 decimals it reports WIRE_EQUALS_SOURCE on am.
  D4 (consistent): gifts (:390-409) send fee = 1 and dis = prdis; the oracle's
     0 <= dis <= prdis rule accepts that.
  Cross-check: the 6 golden/*.json payloads (recorded from the real SDK) give
  0 mismatches, 0 divergences.

----------------------------------------------------------------------------
What the oracle does NOT decide (no evidence — CLAUDE.md §10 "don't guess")
----------------------------------------------------------------------------
  * cap / insp for setm 1/2 (production sends 0/0 and gets warnings 14029/14030);
    only checked to be non-negative integers.
  * the cap vs insp split for setm=3 (user input) — only their sum.
  * ins=3 (cancellation) has no body (IS p17) — amount checks are skipped.
  * am decimals: no documented limit is enforced.
"""

import argparse
import json
import sys
import urllib.request
from decimal import Decimal, ROUND_DOWN, localcontext

SRC_TRUNC = "CLAUDE.md §1/§5 + FAQ 9-4 (truncate, 0 decimals)"
SRC_DEF = "IS field definitions"
SRC_TSSTAM = "CLAUDE.md §5 item 1; V7.9 p73 table 53 row 1"
SRC_SETM3 = "CLAUDE.md §5; V7.9 p46 table 25 / p47 table 27"
SRC_F94 = "FAQ 9-4"

BODY_RIAL = ("prdis", "dis", "adis", "vam", "odam", "olam", "tsstam")
HEADER_RIAL = ("tprdis", "tdis", "tadis", "tvam", "todam", "tbill", "cap", "insp")
CURRENCY_FIELDS = ("cfee", "exr", "sscv", "cop", "vop")   # FAQ 9-4: <= 4 decimals
# fields where "absent/null" legitimately means 0
NULL_IS_ZERO = {"dis", "odam", "olam", "tdis", "todam", "cap", "insp"}

ZERO = Decimal(0)


# --------------------------------------------------------------------------
# primitives
# --------------------------------------------------------------------------

def D(v):
    """Exact Decimal from a wire value. None stays None. Floats are refused
    (they would already have lost exactness) unless given as str()."""
    if v is None:
        return None
    if isinstance(v, bool):
        raise TypeError("boolean where a number was expected: %r" % (v,))
    if isinstance(v, Decimal):
        return v
    if isinstance(v, int):
        return Decimal(v)
    if isinstance(v, str):
        return Decimal(v.strip())
    if isinstance(v, float):
        # payloads must be parsed with parse_float=Decimal; accept but via repr
        return Decimal(repr(v))
    raise TypeError("not a number: %r" % (v,))


def trunc(x):
    """Truncate toward zero to 0 decimals — «روش قطع کردن» [C1][F94]."""
    return x.to_integral_value(rounding=ROUND_DOWN)


def decimals(x):
    """Number of significant decimal places of x (5.7500 -> 2)."""
    x = x.normalize()
    exp = x.as_tuple().exponent
    return max(0, -exp) if isinstance(exp, int) else 0


def _num(obj, key):
    v = obj.get(key) if isinstance(obj, dict) else None
    try:
        return D(v), None
    except (TypeError, ArithmeticError, ValueError) as exc:
        return None, str(exc)


def _fmt(v):
    if v is None:
        return None
    if isinstance(v, Decimal):
        return int(v) if v == v.to_integral_value() else str(v.normalize())
    return v


# --------------------------------------------------------------------------
# the oracle
# --------------------------------------------------------------------------

class Verdict:
    def __init__(self, taxid):
        self.taxid = taxid
        self.skipped = None
        self.expected = {"header": {}, "body": []}
        self.mismatches = []
        self.known_divergences = []
        self.notes = []

    def mismatch(self, path, sent, expected, rule, source):
        self.mismatches.append({"taxid": self.taxid, "path": path, "sent": _fmt(sent),
                                "expected": _fmt(expected), "rule": rule, "source": source})

    def divergence(self, path, sent, expected, client_variant, rule, source):
        self.known_divergences.append({
            "taxid": self.taxid, "path": path, "sent": _fmt(sent), "expected": _fmt(expected),
            "client_variant": _fmt(client_variant), "rule": rule, "source": source,
            "class": "known_divergence"})

    def as_dict(self):
        return {"taxid": self.taxid, "skipped": self.skipped,
                "expected": {"header": {k: _fmt(v) for k, v in self.expected["header"].items()},
                             "body": [{k: _fmt(v) for k, v in b.items()}
                                      for b in self.expected["body"]]},
                "mismatches": self.mismatches, "known_divergences": self.known_divergences,
                "notes": self.notes}


def _compare(vd, path, sent, expected, rule, source, field, client_variant=None,
             divergence_rule=None):
    """sent vs expected; null means 0 only for NULL_IS_ZERO fields."""
    s = sent
    if s is None and field in NULL_IS_ZERO:
        s = ZERO
    if s is not None and s == expected:
        return True
    if (client_variant is not None and client_variant != expected
            and s is not None and s == client_variant):
        vd.divergence(path, sent, expected, client_variant, divergence_rule or rule, SRC_TSSTAM)
        return True
    vd.mismatch(path, sent, expected, rule, source)
    return False


def check_invoice(inv, taxid=None, source_rows=None):
    """Recompute every amount of one invoice and compare with what was sent.

    source_rows (optional): list of dicts {am, fee, dis, vra, odam, olam} — the raw
    database rows, one per body line. When given, the line inputs are taken from
    them (and the wire copies are checked against them), so a wrong am/fee/dis/vra
    on the wire is caught too.
    """
    h = (inv or {}).get("header") or {}
    body = (inv or {}).get("body") or []
    taxid = taxid or h.get("taxid") or "?"
    vd = Verdict(taxid)

    ins = h.get("ins")
    if ins == 3:
        vd.skipped = "ins=3 (cancellation): body is fetched from the reference (IS p17)"
        return vd
    if not body:
        vd.mismatch("body", None, "at least one line", "BODY_PRESENT", SRC_DEF)
        return vd
    if source_rows is not None and len(source_rows) != len(body):
        vd.mismatch("body.length", len(body), len(source_rows), "SOURCE_LINES", "source rows")

    with localcontext() as ctx:
        ctx.prec = 80                                   # far above any real product
        sums = {k: ZERO for k in ("prdis", "dis", "adis", "vam", "oth", "tsstam", "tsstam_client")}
        any_other = False

        for i, b in enumerate(body):
            p = "body[%d]" % i
            if not isinstance(b, dict):
                vd.mismatch(p, b, "object", "TYPE", SRC_DEF)
                continue
            vals, bad = {}, False
            for k in ("am", "fee", "vra") + BODY_RIAL + CURRENCY_FIELDS:
                vals[k], err = _num(b, k)
                if err:
                    vd.mismatch("%s.%s" % (p, k), b.get(k), "number", "TYPE", SRC_DEF)
                    bad = True
            if bad:
                continue

            # inputs: am, fee, dis, vra, odam, olam (from source rows when given)
            src = None
            if source_rows is not None and i < len(source_rows):
                src = {k: D(source_rows[i].get(k)) for k in ("am", "fee", "dis", "vra", "odam", "olam")}
                for k, v in src.items():
                    if v is None:
                        continue
                    sent = vals[k] if vals[k] is not None or k not in NULL_IS_ZERO else ZERO
                    if sent != v:
                        vd.mismatch("%s.%s" % (p, k), vals[k], v, "WIRE_EQUALS_SOURCE", "source rows")
            inp = lambda k: (src[k] if src and src.get(k) is not None else vals[k])  # noqa: E731
            am, fee, vra = inp("am"), inp("fee"), inp("vra")
            dis = inp("dis") or ZERO
            odam, olam = inp("odam") or ZERO, inp("olam") or ZERO

            # FAQ 9-4: every rial field has 0 decimals
            for k in BODY_RIAL:
                if vals[k] is not None and vals[k] != trunc(vals[k]):
                    vd.mismatch("%s.%s" % (p, k), vals[k], trunc(vals[k]), "RIAL_0_DECIMALS", SRC_F94)
            for k in CURRENCY_FIELDS:
                if vals[k] is not None and decimals(vals[k]) > 4:
                    vd.mismatch("%s.%s" % (p, k), vals[k], vals[k].quantize(Decimal("0.0001"),
                                rounding=ROUND_DOWN), "CURRENCY_4_DECIMALS", SRC_F94)

            if am is None or fee is None:
                vd.mismatch(p + ".am/fee", None, "present", "INPUT_MISSING", SRC_DEF)
                continue
            if vra is None:
                vra = ZERO
                vd.notes.append("%s: vra absent, treated as 0" % p)

            # --- the arithmetic, one rule per line ------------------------------
            e_prdis = trunc(am * fee)                          # [DEF][C1] prdis = ⌊am×fee⌋
            e_adis = e_prdis - dis                             # [DEF] adis = prdis − dis
            e_vam = trunc(e_adis * vra / 100)                  # [DEF][C1] vam = ⌊adis×vra/100⌋
            e_tsstam = e_adis + e_vam + odam + olam            # [C5a]
            client_tsstam = e_adis + e_vam                     # CLAUDE.md §5 item 1 (client)
            if odam or olam:
                any_other = True

            vd.expected["body"].append({"prdis": e_prdis, "dis": dis, "adis": e_adis,
                                        "vam": e_vam, "odam": odam, "olam": olam,
                                        "tsstam": e_tsstam})
            _compare(vd, p + ".prdis", vals["prdis"], e_prdis, "PRDIS=TRUNC(AM*FEE)",
                     SRC_TRUNC, "prdis")
            _compare(vd, p + ".adis", vals["adis"], e_adis, "ADIS=PRDIS-DIS", SRC_DEF, "adis")
            _compare(vd, p + ".vam", vals["vam"], e_vam, "VAM=TRUNC(ADIS*VRA/100)",
                     SRC_TRUNC, "vam")
            _compare(vd, p + ".tsstam", vals["tsstam"], e_tsstam, "TSSTAM=ADIS+VAM+ODAM+OLAM",
                     SRC_TSSTAM, "tsstam",
                     client_variant=client_tsstam if (odam or olam) else None,
                     divergence_rule="TSSTAM_EXCLUDES_ODAM_OLAM")
            if dis < 0 or dis > e_prdis:
                vd.mismatch(p + ".dis", dis, "0 <= dis <= prdis", "DIS_RANGE", SRC_DEF)

            sums["prdis"] += e_prdis
            sums["dis"] += dis
            sums["adis"] += e_adis
            sums["vam"] += e_vam
            sums["oth"] += odam + olam
            sums["tsstam"] += e_tsstam
            sums["tsstam_client"] += client_tsstam

        # --- header ----------------------------------------------------------
        hv = {}
        for k in HEADER_RIAL:
            hv[k], err = _num(h, k)
            if err:
                vd.mismatch("header." + k, h.get(k), "number", "TYPE", SRC_DEF)
                hv[k] = None
            elif hv[k] is not None and hv[k] != trunc(hv[k]):
                vd.mismatch("header." + k, hv[k], trunc(hv[k]), "RIAL_0_DECIMALS", SRC_F94)

        exp_h = {"tprdis": sums["prdis"], "tdis": sums["dis"], "tadis": sums["adis"],
                 "tvam": sums["vam"], "todam": sums["oth"], "tbill": sums["tsstam"]}
        vd.expected["header"].update(exp_h)
        for k, rule in (("tprdis", "TPRDIS=SUM(PRDIS)"), ("tdis", "TDIS=SUM(DIS)"),
                        ("tadis", "TADIS=SUM(ADIS)"), ("tvam", "TVAM=SUM(VAM)"),
                        ("todam", "TODAM=SUM(ODAM+OLAM)")):
            _compare(vd, "header." + k, hv[k], exp_h[k], rule, SRC_DEF, k)
        _compare(vd, "header.tbill", hv["tbill"], exp_h["tbill"], "TBILL=SUM(TSSTAM)",
                 SRC_DEF + "; " + SRC_TSSTAM, "tbill",
                 client_variant=sums["tsstam_client"] if any_other else None,
                 divergence_rule="TBILL_EXCLUDES_TODAM")

        # --- settlement --------------------------------------------------------
        setm = h.get("setm")
        for k in ("cap", "insp"):
            if hv[k] is not None and hv[k] < 0:
                vd.mismatch("header." + k, hv[k], ">= 0", "NON_NEGATIVE", SRC_SETM3)
        if setm == 3:
            if hv["cap"] is None or hv["insp"] is None:
                vd.mismatch("header.cap/insp", None, "both present", "SETM3_CAP_INSP_REQUIRED",
                            "V7.9 p45 (per CLAUDE.md §5)")
            else:
                # the relation is evaluated on the SENT header (that is what the system
                # sees); a wrong tbill/tvam/todam is reported separately above.
                basis = (hv["tbill"] or ZERO) - (hv["tvam"] or ZERO) - (hv["todam"] or ZERO)
                vd.expected["header"]["cap+insp"] = basis
                if hv["cap"] + hv["insp"] != basis:
                    vd.mismatch("header.cap+insp", hv["cap"] + hv["insp"], basis,
                                "CAP+INSP=TBILL-TVAM-TODAM", SRC_SETM3)
        elif setm in (1, 2):
            vd.notes.append("setm=%s: cap/insp not judged (no documented rule; production "
                            "sends 0/0 and gets warnings 14029/14030)" % setm)
    return vd


def check_payloads(payloads, source=None):
    """payloads: dict taxid -> invoice (GET __payloads). source: dict taxid -> rows."""
    out = []
    for taxid, inv in payloads.items():
        rows = (source or {}).get(taxid)
        out.append(check_invoice(inv, taxid=taxid, source_rows=rows))
    return out


def load_json_text(text):
    return json.loads(text, parse_float=Decimal)


def fetch_payloads(base_url, timeout=10):
    url = base_url.rstrip("/") + "/__payloads"
    with urllib.request.urlopen(url, timeout=timeout) as r:
        return load_json_text(r.read().decode("utf-8"))


def _report(verdicts, as_json):
    mism = [m for v in verdicts for m in v.mismatches]
    div = [d for v in verdicts for d in v.known_divergences]
    skipped = [v for v in verdicts if v.skipped]
    if as_json:
        print(json.dumps({"invoices": len(verdicts), "skipped": len(skipped),
                          "mismatches": mism, "known_divergences": div,
                          "verdicts": [v.as_dict() for v in verdicts]},
                         ensure_ascii=False, indent=1, default=_fmt))
    else:
        print("oracle: %d invoice(s), %d skipped (ins=3), %d mismatch(es), %d known divergence(s)"
              % (len(verdicts), len(skipped), len(mism), len(div)))
        for m in mism:
            print("  MISMATCH %s %s: sent=%s expected=%s  [%s; %s]"
                  % (m["taxid"], m["path"], m["sent"], m["expected"], m["rule"], m["source"]))
        for d in div:
            print("  KNOWN_DIVERGENCE %s %s: sent=%s document=%s client-variant=%s  [%s]"
                  % (d["taxid"], d["path"], d["sent"], d["expected"], d["client_variant"], d["rule"]))
    return 1 if mism else 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("check")
    c.add_argument("payloads")
    f = sub.add_parser("fetch")
    f.add_argument("url")
    for p in (c, f):
        p.add_argument("--source", help="JSON dict taxid -> list of raw rows {am,fee,dis,vra,odam,olam}")
        p.add_argument("--json", action="store_true")
    a = ap.parse_args(argv)

    source = None
    if a.source:
        with open(a.source, encoding="utf-8") as fh:
            source = load_json_text(fh.read())
    if a.cmd == "check":
        with open(a.payloads, encoding="utf-8-sig") as fh:
            payloads = load_json_text(fh.read())
    else:
        payloads = fetch_payloads(a.url)
    if not isinstance(payloads, dict):
        print("payloads must be a JSON object taxid -> invoice", file=sys.stderr)
        return 2
    return _report(check_payloads(payloads, source), a.json)


if __name__ == "__main__":
    sys.exit(main())
