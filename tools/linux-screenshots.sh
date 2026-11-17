#!/usr/bin/env bash
# Takes the README screenshots on Linux by running the real Windows build of the game under Wine,
# in its screenshot mode (a demo garden, never your save file).
#
#   tools/linux-screenshots.sh [output-folder]      default: docs/screenshots
#
# Needs Ubuntu/Debian (root or sudo for apt), the .NET 10 SDK, python3 and git.
# On Windows you don't need this: run  TaskBarGame.exe --screenshots docs\screenshots
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
out="$(realpath -m "${1:-$repo/docs/screenshots}")"
work="${TBG_SCREENSHOT_WORK:-${TMPDIR:-/tmp}/taskbargame-screenshots}"
mkdir -p "$work" "$out"

# 1. Wine and a virtual screen.
if ! command -v wine >/dev/null || ! command -v Xvfb >/dev/null; then
  sudo=""; [ "$(id -u)" = 0 ] || sudo=sudo
  $sudo apt-get update -qq
  DEBIAN_FRONTEND=noninteractive $sudo apt-get install -y -qq --no-install-recommends \
    wine64 wine xvfb xauth fonts-liberation fonts-dejavu-core
fi

display=":$((RANDOM % 500 + 100))"
Xvfb "$display" -screen 0 1280x800x24 -nolisten tcp >/dev/null 2>&1 &
xvfb=$!
trap 'wineserver -k 2>/dev/null; kill $xvfb 2>/dev/null || true' EXIT
sleep 1
export DISPLAY="$display" WINEPREFIX="$work/wineprefix" WINEDEBUG=-all

# 2. Fonts. WPF needs Windows fonts, and Segoe UI (the game's font on Windows) can't be copied,
#    so build Selawik (Microsoft's open-source stand-in for Segoe UI, same letter sizes) and
#    install it under the Windows names. DejaVu Sans covers symbols like ✕ and ▾.
fonts="$work/fonts"
if [ ! -f "$fonts/segoeui.ttf" ]; then
  python3 -m venv "$work/venv"
  "$work/venv/bin/pip" install -q --disable-pip-version-check fontmake
  rm -rf "$work/selawik"
  git clone -q --depth 1 https://github.com/microsoft/Selawik "$work/selawik"
  for weight in Regular Bold; do
    "$work/venv/bin/fontmake" -u "$work/selawik/Source files/UFO/Selawik-$weight.ufo" \
      -o ttf --output-dir "$work/selawik-ttf" >/dev/null 2>&1
  done
  mkdir -p "$fonts"
  "$work/venv/bin/python" - "$work/selawik-ttf" "$fonts" <<'PY'
import sys
from fontTools.ttLib import TTFont
src, out = sys.argv[1], sys.argv[2]
def install(path, file, family, style, weight):
    font = TTFont(path)
    names = font["name"]
    for nid in (16, 17, 21, 22):
        names.removeNames(nameID=nid)
    full = family if style == "Regular" else f"{family} {style}"
    for ids in ((3, 1, 0x409), (1, 0, 0)):
        for nid, text in ((1, family), (2, style), (3, full), (4, full), (6, (family + "-" + style).replace(" ", ""))):
            names.setName(text, nid, *ids)
    font["OS/2"].usWeightClass = weight
    font.save(f"{out}/{file}")
install(f"{src}/Selawik-Regular.ttf", "segoeui.ttf", "Segoe UI", "Regular", 400)
install(f"{src}/Selawik-Bold.ttf", "segoeuib.ttf", "Segoe UI", "Bold", 700)
install("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "seguisym.ttf", "Segoe UI Symbol", "Regular", 400)
install("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "arial.ttf", "Arial", "Regular", 400)
install("/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf", "arialbd.ttf", "Arial", "Bold", 700)
PY
fi

# 3. A Wine "Windows" with those fonts, drawing in software (there's no graphics card).
if [ ! -d "$WINEPREFIX/drive_c/windows" ]; then
  # Skip Wine's own .NET and browser add-ons while creating it (only here: the game needs mscoree).
  WINEDLLOVERRIDES="mscoree,mshtml=" wineboot -i >/dev/null 2>&1
fi
mkdir -p "$WINEPREFIX/drive_c/windows/Fonts"
cp "$fonts"/*.ttf "$WINEPREFIX/drive_c/windows/Fonts/"
for entry in "Segoe UI=segoeui.ttf" "Segoe UI Bold=segoeuib.ttf" "Segoe UI Symbol=seguisym.ttf" \
             "Arial=arial.ttf" "Arial Bold=arialbd.ttf"; do
  wine reg add 'HKLM\Software\Microsoft\Windows NT\CurrentVersion\Fonts' \
    /v "${entry%%=*} (TrueType)" /t REG_SZ /d "${entry#*=}" /f >/dev/null 2>&1
done
wine reg add 'HKCU\Software\Microsoft\Avalon.Graphics' /v DisableHWAcceleration /t REG_DWORD /d 1 /f >/dev/null 2>&1
wineserver -w

# 4. Build the Windows game and run its screenshot tour.
dotnet publish "$repo/src/TaskBarGame.App" -c Release -r win-x64 --self-contained true -o "$work/game" -v quiet -nologo
rm -f "$WINEPREFIX"/drive_c/users/*/AppData/Roaming/TaskBarGame/error.log
run_tour() {
  # If the game crashes, fail straight away instead of waiting in Wine's crash debugger.
  WINEDLLOVERRIDES="winedbg.exe=d" timeout 300 wine "$work/game/TaskBarGame.exe" --screenshots "$(winepath -w "$out")" >"$work/run.log" 2>&1
}
if ! run_tour; then
  echo "The screenshot tour failed. Log: $work/run.log" >&2
  cat "$WINEPREFIX"/drive_c/users/*/AppData/Roaming/TaskBarGame/error.log 2>/dev/null >&2 || true
  exit 1
fi
echo "Screenshots saved to $out:"
ls "$out"
