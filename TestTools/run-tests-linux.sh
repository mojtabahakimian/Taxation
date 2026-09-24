#!/usr/bin/env bash
# اجرای هارنس تست مؤدیان روی لینوکس با شیم SDK (TestTools/SdkShim).
# سرور ساختگی را بالا می‌آورد، هارنس را اجرا می‌کند، سرور را می‌بندد و
# با کد خروجی هارنس خارج می‌شود. بدون دیتابیس (بدون --bulk).
#
#   bash TestTools/run-tests-linux.sh [--port 9090] [--no-full]
#
# به‌طور پیش‌فرض سرور دوم (moadian_mock_full.py، کارپوشه + کدهای EC_V02) هم روی
# پورت+100 بالا می‌آید، خودآزمون پایتونی‌اش اجرا می‌شود و گروه ۲۸ با --full=... اجرا
# می‌شود؛ گروه ۲۹ (--e2e) هم با همان سرور. با --no-full فقط سرور اول.
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT=9090
if [ "${1:-}" = "--port" ] && [ -n "${2:-}" ]; then PORT="$2"; shift 2; fi
BASE="http://127.0.0.1:${PORT}/"
FULL=1
if [ "${1:-}" = "--no-full" ]; then FULL=0; shift; fi
FULL_PORT=$((PORT + 100))
FULL_BASE="http://127.0.0.1:${FULL_PORT}/"

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# پروژهٔ شیم net8.0 است؛ این برای وقتی است که کسی آن را به net6.0 برگرداند.
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"

if curl -fsS "${BASE}__ping" >/dev/null 2>&1; then
  echo "یک سرور روی پورت ${PORT} از قبل بالاست — وضعیتش مشترک می‌شود. پورت دیگری بدهید: --port N" >&2
  exit 2
fi

# پایتونی پیدا کن که کتابخانهٔ cryptography در آن واقعاً کار کند.
# (روی این ماشین python3 پیش‌فرض 3.11 است ولی بستهٔ دبیان cryptography برای 3.12
#  ساخته شده و با خطای _cffi_backend می‌ترکد.)
PY=""
for cand in ${PYTHON:-} python3 python3.12 python3.13 python3.11 python3.10; do
  if command -v "$cand" >/dev/null 2>&1 && \
     "$cand" -c "from cryptography.hazmat.primitives.ciphers.aead import AESGCM" >/dev/null 2>&1; then
    PY="$cand"; break
  fi
done
if [ -z "$PY" ]; then
  echo "هیچ پایتونی با کتابخانهٔ cryptography سالم پیدا نشد (pip install cryptography یا PYTHON=...)" >&2
  exit 4
fi

echo "== ساخت پروژهٔ شیم"
dotnet build "$ROOT/TestTools/ShimBuild" -nologo -v:q || { echo "ساخت شکست خورد" >&2; exit 3; }

echo "== بالا آوردن سرور ساختگی روی ${BASE} (با $PY)"
"$PY" "$ROOT/TestTools/LocalMoadian/moadian_mock.py" --port "$PORT" --quiet &
MOCK_PID=$!
FULL_PID=""
cleanup() {
  kill "$MOCK_PID" 2>/dev/null; wait "$MOCK_PID" 2>/dev/null
  [ -n "$FULL_PID" ] && { kill "$FULL_PID" 2>/dev/null; wait "$FULL_PID" 2>/dev/null; }
}
trap cleanup EXIT INT TERM

for _ in $(seq 1 50); do
  curl -fsS "${BASE}__ping" >/dev/null 2>&1 && break
  if ! kill -0 "$MOCK_PID" 2>/dev/null; then echo "سرور ساختگی بالا نیامد" >&2; exit 4; fi
  sleep 0.2
done
curl -fsS "${BASE}__ping" >/dev/null 2>&1 || { echo "سرور ساختگی پاسخ نداد" >&2; exit 4; }

EXTRA=()
if [ "$FULL" = 1 ]; then
  echo "== خودآزمون سرور کامل (test_mock_full.py)"
  "$PY" "$ROOT/TestTools/LocalMoadian/test_mock_full.py" >/tmp/test_mock_full.log 2>&1 \
    || { tail -30 /tmp/test_mock_full.log >&2; echo "خودآزمون سرور کامل شکست خورد" >&2; exit 5; }
  tail -3 /tmp/test_mock_full.log

  if curl -fsS "${FULL_BASE}__ping" >/dev/null 2>&1; then
    echo "پورت ${FULL_PORT} از قبل اشغال است" >&2; exit 2
  fi
  echo "== بالا آوردن سرور کامل روی ${FULL_BASE}"
  "$PY" "$ROOT/TestTools/LocalMoadian/moadian_mock_full.py" --port "$FULL_PORT" --quiet &
  FULL_PID=$!
  for _ in $(seq 1 50); do
    curl -fsS "${FULL_BASE}__ping" >/dev/null 2>&1 && break
    sleep 0.2
  done
  curl -fsS "${FULL_BASE}__ping" >/dev/null 2>&1 || { echo "سرور کامل پاسخ نداد" >&2; exit 4; }
  # گروه ۲۹ (سر تا ته با دیتابیس ساختگی) همراه گروه ۲۸ اجرا می‌شود؛ پیشگوی مبالغ همین پایتون را می‌گیرد.
  EXTRA=("--full=${FULL_BASE}" "--e2e")
  export MOADIAN_PYTHON="$PY"
fi

echo "== اجرای هارنس"
dotnet run --no-build --project "$ROOT/TestTools/ShimBuild" -- "$BASE" "${EXTRA[@]}" "$@"
CODE=$?

echo "== کد خروجی هارنس: ${CODE}"
exit "$CODE"
