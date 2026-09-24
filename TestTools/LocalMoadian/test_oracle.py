#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Self-test for oracle_amounts.py:   python3.12 TestTools/LocalMoadian/test_oracle.py

Every expected number below is computed BY HAND in the comment next to it, so the
oracle is checked against arithmetic, not against itself. The sabotage cases prove
the oracle turns red on the mistakes it exists to catch (CLAUDE.md §10).
"""

import json
import os
import subprocess
import sys
import tempfile
import threading
import unittest
from decimal import Decimal
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import oracle_amounts as O  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))


def line(am, fee, prdis, dis, adis, vra, vam, tsstam, odam=None, olam=None):
    return {"sstid": "2720000001569", "am": Decimal(str(am)), "fee": Decimal(str(fee)),
            "prdis": prdis, "dis": dis, "adis": adis, "vra": vra, "vam": vam,
            "odam": odam, "olam": olam, "tsstam": tsstam}


def inv(lines, tprdis, tdis, tadis, tvam, todam, tbill, setm=1, cap=0, insp=0, ins=1,
        taxid="A11216ABCDE0123456789A1"):
    return {"header": {"taxid": taxid, "ins": ins, "inty": 1, "setm": setm,
                       "tprdis": tprdis, "tdis": tdis, "tadis": tadis, "tvam": tvam,
                       "todam": todam, "tbill": tbill, "cap": cap, "insp": insp},
            "body": lines}


def rules(vd):
    return sorted(m["rule"] for m in vd.mismatches)


def paths(vd):
    return sorted(m["path"] for m in vd.mismatches)


class HandComputed(unittest.TestCase):

    def test_fractional_quantity_above_half_truncates(self):
        # am × fee = 5.75 × 123457 = 709877.75  (123457×5 = 617285; ×0.75 = 92592.75)
        # prdis = trunc(709877.75) = 709877  (Round would give 709878)
        # vra 10: vam = trunc(709877 × 10 / 100) = trunc(70987.7) = 70987
        # tsstam = 709877 + 70987 = 780864
        i = inv([line(5.75, 123457, 709877, 0, 709877, 10, 70987, 780864)],
                709877, 0, 709877, 70987, 0, 780864)
        vd = O.check_invoice(i)
        self.assertEqual(vd.mismatches, [])
        self.assertEqual(vd.expected["body"][0]["prdis"], Decimal(709877))
        self.assertEqual(vd.expected["body"][0]["vam"], Decimal(70987))
        self.assertEqual(vd.expected["header"]["tbill"], Decimal(780864))

    def test_exact_half_tie_truncates_down(self):
        # 2.5 × 3 = 7.5 -> trunc 7   (ROUND_HALF_UP would be 8, banker's 8)
        # 1.5 × 5 = 7.5 -> 7 ; vra 9 on adis 7: 7×9/100 = 0.63 -> vam 0
        # line 2: 0.5 × 13 = 6.5 -> 6 (banker's round would give 6 too, half-up 7)
        # vra 9: 6×9/100 = 0.54 -> 0
        # tprdis = 7 + 6 = 13, tbill = 13
        i = inv([line(2.5, 3, 7, 0, 7, 9, 0, 7), line(0.5, 13, 6, 0, 6, 9, 0, 6)],
                13, 0, 13, 0, 0, 13)
        self.assertEqual(O.check_invoice(i).mismatches, [])

    def test_vam_truncation_with_half(self):
        # adis 1050, vra 9: 1050×9/100 = 94.5 -> vam 94 (not 95)
        i = inv([line(1, 1050, 1050, 0, 1050, 9, 94, 1144)], 1050, 0, 1050, 94, 0, 1144)
        self.assertEqual(O.check_invoice(i).mismatches, [])
        i["body"][0]["vam"] = 95; i["body"][0]["tsstam"] = 1145            # noqa: E702
        i["header"]["tvam"] = 95; i["header"]["tbill"] = 1145              # noqa: E702
        self.assertIn("VAM=TRUNC(ADIS*VRA/100)", rules(O.check_invoice(i)))

    def test_discount(self):
        # am 3 × fee 33333 = 99999 ; dis 999 -> adis = 99999 − 999 = 99000
        # vra 10: vam = 9900 exactly ; tsstam = 99000 + 9900 = 108900
        i = inv([line(3, 33333, 99999, 999, 99000, 10, 9900, 108900)],
                99999, 999, 99000, 9900, 0, 108900)
        self.assertEqual(O.check_invoice(i).mismatches, [])

    def test_multi_line_sums(self):
        # L1: 3 × 33333 = 99999, dis 0, vra 0 -> vam 0, tsstam 99999
        # L2: 1.333 × 75001 = 99976.333 -> 99976 ; dis 76 -> adis 99900
        #     vra 9: 99900 × 9 / 100 = 8991 ; tsstam 108891
        # L3: 12 × 12345.5 = 148146 ; dis 0 ; vra 10 -> 14814.6 -> 14814 ; tsstam 162960
        # tprdis = 99999+99976+148146 = 348121 ; tdis = 76
        # tadis  = 99999+99900+148146 = 348045 ; tvam = 0+8991+14814 = 23805
        # tbill  = 99999+108891+162960 = 371850  (= tadis + tvam = 348045+23805 ✓)
        ls = [line(3, 33333, 99999, 0, 99999, 0, 0, 99999),
              line("1.333", 75001, 99976, 76, 99900, 9, 8991, 108891),
              line(12, "12345.5", 148146, 0, 148146, 10, 14814, 162960)]
        i = inv(ls, 348121, 76, 348045, 23805, 0, 371850)
        vd = O.check_invoice(i)
        self.assertEqual(vd.mismatches, [])
        self.assertEqual(vd.expected["header"]["tadis"], Decimal(348045))

    def test_setm3_split(self):
        # 4 × 250000 = 1000000 ; vra 10 -> vam 100000 ; tbill 1100000
        # cap + insp must equal tbill − tvam − todam = 1100000 − 100000 − 0 = 1000000
        # split 400000 cash + 600000 credit = 1000000 ✓
        base = dict(tprdis=1000000, tdis=0, tadis=1000000, tvam=100000, todam=0, tbill=1100000)
        ls = [line(4, 250000, 1000000, 0, 1000000, 10, 100000, 1100000)]
        ok = inv(ls, setm=3, cap=400000, insp=600000, **base)
        self.assertEqual(O.check_invoice(ok).mismatches, [])
        # the common mistake: splitting tbill (VAT included) -> 1100000 != 1000000
        bad = inv(ls, setm=3, cap=500000, insp=600000, **base)
        self.assertEqual(rules(O.check_invoice(bad)), ["CAP+INSP=TBILL-TVAM-TODAM"])
        # setm 3 without insp
        missing = inv(ls, setm=3, cap=1000000, insp=None, **base)
        self.assertEqual(rules(O.check_invoice(missing)), ["SETM3_CAP_INSP_REQUIRED"])
        # setm 1 with the same cap/insp: not judged (no documented rule)
        s1 = inv(ls, setm=1, cap=0, insp=0, **base)
        self.assertEqual(O.check_invoice(s1).mismatches, [])

    def test_cancellation_is_skipped(self):
        i = {"header": {"taxid": "X", "ins": 3, "irtaxid": "Y"}, "body": []}
        vd = O.check_invoice(i)
        self.assertIsNotNone(vd.skipped)
        self.assertEqual(vd.mismatches, [])

    def test_known_divergence_odam(self):
        # 2 × 50000 = 100000 ; vra 10 -> 10000 ; odam 3000, olam 0
        # document: tsstam = 100000 + 10000 + 3000 = 113000, tbill 113000, todam 3000
        # client (CLAUDE.md §5.1): tsstam = 110000, tbill = 110000
        ls = [line(2, 50000, 100000, 0, 100000, 10, 10000, 110000, odam=3000, olam=0)]
        i = inv(ls, 100000, 0, 100000, 10000, 3000, 110000)
        vd = O.check_invoice(i)
        self.assertEqual(vd.mismatches, [])
        self.assertEqual(sorted(d["rule"] for d in vd.known_divergences),
                         ["TBILL_EXCLUDES_TODAM", "TSSTAM_EXCLUDES_ODAM_OLAM"])
        # the document variant is simply correct
        i["body"][0]["tsstam"] = 113000; i["header"]["tbill"] = 113000     # noqa: E702
        vd = O.check_invoice(i)
        self.assertEqual((vd.mismatches, vd.known_divergences), ([], []))
        # neither variant -> a real mismatch
        i["body"][0]["tsstam"] = 111000
        self.assertIn("TSSTAM=ADIS+VAM+ODAM+OLAM", rules(O.check_invoice(i)))

    def test_no_divergence_reported_when_odam_zero(self):
        ls = [line(1, 1000, 1000, 0, 1000, 10, 100, 1100, odam=0, olam=0)]
        vd = O.check_invoice(inv(ls, 1000, 0, 1000, 100, 0, 1100))
        self.assertEqual((vd.mismatches, vd.known_divergences), ([], []))

    def test_null_means_zero_only_for_optional_fields(self):
        # dis/odam/olam/todam null -> 0 (golden payloads send odam: null)
        ls = [line(1, 1000, 1000, None, 1000, 0, 0, 1000)]
        self.assertEqual(O.check_invoice(inv(ls, 1000, None, 1000, 0, None, 1000)).mismatches, [])
        # but a missing prdis is a mismatch
        ls = [line(1, 1000, None, 0, 1000, 0, 0, 1000)]
        self.assertIn("body[0].prdis", paths(O.check_invoice(inv(ls, 1000, 0, 1000, 0, 0, 1000))))

    def test_rial_decimals_and_currency(self):
        # a fractional rial value (prdis 709877.75, i.e. not truncated at all)
        ls = [line(5.75, 123457, Decimal("709877.75"), 0, Decimal("709877.75"), 0, 0,
                   Decimal("709877.75"))]
        vd = O.check_invoice(inv(ls, Decimal("709877.75"), 0, Decimal("709877.75"), 0, 0,
                                 Decimal("709877.75")))
        self.assertIn("RIAL_0_DECIMALS", rules(vd))
        # currency field with 5 decimals
        ls = [line(1, 1000, 1000, 0, 1000, 0, 0, 1000)]
        ls[0]["cfee"] = Decimal("1.23456")
        self.assertEqual(rules(O.check_invoice(inv(ls, 1000, 0, 1000, 0, 0, 1000))),
                         ["CURRENCY_4_DECIMALS"])
        ls[0]["cfee"] = Decimal("1.2345")
        self.assertEqual(O.check_invoice(inv(ls, 1000, 0, 1000, 0, 0, 1000)).mismatches, [])

    def test_source_rows(self):
        # source says am 3, wire says am 2 (and wire is self-consistent for am 2):
        # 2 × 1000 = 2000 on the wire; oracle recomputes from source: 3 × 1000 = 3000
        ls = [line(2, 1000, 2000, 0, 2000, 0, 0, 2000)]
        vd = O.check_invoice(inv(ls, 2000, 0, 2000, 0, 0, 2000),
                             source_rows=[{"am": 3, "fee": 1000, "dis": 0, "vra": 0}])
        self.assertIn("WIRE_EQUALS_SOURCE", rules(vd))
        self.assertIn("PRDIS=TRUNC(AM*FEE)", rules(vd))

    def test_json_parse_keeps_decimals_exact(self):
        # 0.1 × 3 in float = 0.30000000000000004; Decimal keeps 0.3 -> prdis 0 either way,
        # but 1.1 × 10 in float = 11.000000000000002 and 0.7×10=7.000000000000001 — with
        # Decimal these are exactly 11 and 7.
        text = ('{"T":{"header":{"ins":1,"setm":1,"tprdis":18,"tdis":0,"tadis":18,"tvam":0,'
                '"todam":0,"tbill":18},"body":[{"am":1.1,"fee":10,"prdis":11,"dis":0,"adis":11,'
                '"vra":0,"vam":0,"tsstam":11},{"am":0.7,"fee":10,"prdis":7,"dis":0,"adis":7,'
                '"vra":0,"vam":0,"tsstam":7}]}}')
        vds = O.check_payloads(O.load_json_text(text))
        self.assertEqual(vds[0].mismatches, [])


class Sabotage(unittest.TestCase):
    """The oracle must turn red on the three classic mistakes."""

    def clean(self):
        # 5.75 × 123457 = 709877.75 -> 709877 ; vra 10 -> 70987 ; tsstam 780864
        return inv([line(5.75, 123457, 709877, 0, 709877, 10, 70987, 780864)],
                   709877, 0, 709877, 70987, 0, 780864)

    def test_round_instead_of_truncate(self):
        # a client using Math.Round: prdis 709878, adis 709878,
        # vam round(70987.8) = 70988, tsstam 780866, header follows consistently.
        i = inv([line(5.75, 123457, 709878, 0, 709878, 10, 70988, 780866)],
                709878, 0, 709878, 70988, 0, 780866)
        r = rules(O.check_invoice(i))
        self.assertIn("PRDIS=TRUNC(AM*FEE)", r)
        self.assertIn("TPRDIS=SUM(PRDIS)", r)
        self.assertIn("TBILL=SUM(TSSTAM)", r)

    def test_header_sum_off_by_one_rial(self):
        i = self.clean()
        i["header"]["tbill"] = 780865                      # one rial too much
        vd = O.check_invoice(i)
        self.assertEqual(rules(vd), ["TBILL=SUM(TSSTAM)"])
        self.assertEqual(vd.mismatches[0]["expected"], 780864)
        i = self.clean()
        i["header"]["tadis"] = 709876                      # one rial too little
        self.assertEqual(rules(O.check_invoice(i)), ["TADIS=SUM(ADIS)"])

    def test_wrong_vam(self):
        i = self.clean()
        # VAT computed on prdis+... or at 9% instead of 10: 709877×9/100 = 63888.93 -> 63888
        i["body"][0]["vam"] = 63888
        i["body"][0]["tsstam"] = 709877 + 63888
        i["header"]["tvam"] = 63888
        i["header"]["tbill"] = 709877 + 63888
        r = rules(O.check_invoice(i))
        self.assertIn("VAM=TRUNC(ADIS*VRA/100)", r)
        self.assertIn("TVAM=SUM(VAM)", r)

    def test_wrong_adis(self):
        i = self.clean()
        i["body"][0]["dis"] = 100                          # discount ignored in adis
        i["header"]["tdis"] = 100
        self.assertIn("ADIS=PRDIS-DIS", rules(O.check_invoice(i)))


class Cli(unittest.TestCase):

    def _payloads(self, good=True):
        i = inv([line(5.75, 123457, 709877, 0, 709877, 10, 70987, 780864)],
                709877, 0, 709877, 70987, 0, 780864 if good else 780865)
        c = {"header": {"taxid": "C", "ins": 3, "irtaxid": "X"}, "body": []}
        return json.dumps({"A": i, "C": c}, default=str)

    def _run(self, *args):
        return subprocess.run([sys.executable, os.path.join(HERE, "oracle_amounts.py")] + list(args),
                              capture_output=True, text=True, encoding="utf-8")

    def test_check_exit_codes(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "p.json")
            with open(p, "w", encoding="utf-8") as f:
                f.write(self._payloads(True))
            r = self._run("check", p)
            self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
            self.assertIn("1 skipped", r.stdout)
            with open(p, "w", encoding="utf-8") as f:
                f.write(self._payloads(False))
            r = self._run("check", p, "--json")
            self.assertEqual(r.returncode, 1)
            self.assertEqual(json.loads(r.stdout)["mismatches"][0]["path"], "header.tbill")

    def test_fetch(self):
        body = self._payloads(False).encode()

        class H(BaseHTTPRequestHandler):
            def do_GET(self):
                self.send_response(200 if self.path.endswith("__payloads") else 404)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *a):
                pass

        srv = ThreadingHTTPServer(("127.0.0.1", 0), H)
        threading.Thread(target=srv.serve_forever, daemon=True).start()
        try:
            r = self._run("fetch", "http://127.0.0.1:%d/" % srv.server_address[1])
            self.assertEqual(r.returncode, 1, r.stdout + r.stderr)
            self.assertIn("MISMATCH A header.tbill", r.stdout)
        finally:
            srv.shutdown()
            srv.server_close()


if __name__ == "__main__":
    unittest.main(verbosity=1)
