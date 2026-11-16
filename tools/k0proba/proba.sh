#!/bin/sh
# WStation K0 próba – a DSM Feladatütemezőjéből: sh /volume1/WStation/k0/proba.sh
D="$(cd "$(dirname "$0")" && pwd)"
cd "$D" || exit 1
export TMPDIR="$D"
export HOME="$D"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

{
  echo "=== rendszer ==="
  date
  uname -a
  cat /etc.defaults/VERSION 2>/dev/null | grep -E "productversion|buildnumber|majorversion|minorversion"
  ldd --version 2>&1 | head -1
  ls -la /lib/ld-linux-aarch64.so.1 /lib64/ld-linux-aarch64.so.1 /lib/ld-musl-aarch64.so.1 2>&1
  id
  free -m 2>/dev/null
  df -h "$D" 2>/dev/null
} > "$D/eredmeny_rendszer.txt" 2>&1

chmod +x "$D/glibc/k0proba" "$D/musl/k0proba" 2>/dev/null

echo "=== glibc (normál) ===" > "$D/eredmeny_glibc.txt"
"$D/glibc/k0proba" >> "$D/eredmeny_glibc.txt" 2>&1
echo "kilépési kód: $?" >> "$D/eredmeny_glibc.txt"

if ! grep -q "=== vége ===" "$D/eredmeny_glibc.txt"; then
  echo "=== glibc, W^X kikapcsolva ===" > "$D/eredmeny_glibc_wx.txt"
  DOTNET_EnableWriteXorExecute=0 "$D/glibc/k0proba" >> "$D/eredmeny_glibc_wx.txt" 2>&1
  echo "kilépési kód: $?" >> "$D/eredmeny_glibc_wx.txt"

  echo "=== musl ===" > "$D/eredmeny_musl.txt"
  "$D/musl/k0proba" >> "$D/eredmeny_musl.txt" 2>&1
  echo "kilépési kód: $?" >> "$D/eredmeny_musl.txt"
fi

echo "kész: $(date)" > "$D/eredmeny_kesz.txt"
