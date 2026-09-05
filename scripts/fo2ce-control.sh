#!/usr/bin/env bash
# Control fo2ce (reference/fallout2-ce/run/fallout2-ce) for the comparison-automation pilot.
# See docs/fo2ce-comparison-playbook.md for the full workflow this supports.
#
# Usage:
#   scripts/fo2ce-control.sh launch                 # start fo2ce, print its PID
#   scripts/fo2ce-control.sh shot <output.png>       # screenshot the whole screen
#   scripts/fo2ce-control.sh key <xdotool-key-spec>  # e.g. "Return", "Down Down Return"
#   scripts/fo2ce-control.sh click <x> <y>           # click inside the fo2ce window at (x,y)
#   scripts/fo2ce-control.sh status                  # exit 0 if fo2ce is running, else 1
#   scripts/fo2ce-control.sh kill                    # stop fo2ce
set -uo pipefail
cd "$(dirname "$0")/.."

RUN_DIR="reference/fallout2-ce/run"
BIN="$(cd "$RUN_DIR" && pwd)/fallout2-ce"
LOG="/tmp/fo2ce-control.log"
DISP="${DISPLAY:-:0}"

cmd="${1:-}"
shift || true

case "$cmd" in
  launch)
    if pgrep -f "$BIN" >/dev/null 2>&1; then
      echo "already running: $(pgrep -f "$BIN")" >&2
      exit 1
    fi
    ( cd "$RUN_DIR" && SDL_VIDEODRIVER=x11 DISPLAY="$DISP" "$BIN" >"$LOG" 2>&1 & )
    sleep 3
    pid="$(pgrep -f "$BIN" | head -1)"
    if [ -z "$pid" ]; then
      echo "fo2ce failed to start, see $LOG" >&2
      exit 1
    fi
    echo "$pid"
    ;;
  shot)
    out="${1:?usage: shot <output.png>}"
    mkdir -p "$(dirname "$out")"
    spectacle -b -n -f -o "$out"
    ;;
  key)
    spec="${1:?usage: key <xdotool-key-spec>}"
    wid="$(DISPLAY="$DISP" xdotool search --name "FALLOUT II" | head -1)"
    [ -n "$wid" ] || { echo "fo2ce window not found" >&2; exit 1; }
    DISPLAY="$DISP" xdotool windowactivate "$wid"
    DISPLAY="$DISP" xdotool key --window "$wid" $spec
    ;;
  click)
    x="${1:?usage: click <x> <y>}"; y="${2:?usage: click <x> <y>}"
    wid="$(DISPLAY="$DISP" xdotool search --name "FALLOUT II" | head -1)"
    [ -n "$wid" ] || { echo "fo2ce window not found" >&2; exit 1; }
    DISPLAY="$DISP" xdotool windowactivate "$wid"
    DISPLAY="$DISP" xdotool mousemove --window "$wid" "$x" "$y" click 1
    ;;
  status)
    pgrep -f "$BIN" >/dev/null 2>&1
    ;;
  kill)
    pkill -f "$BIN" 2>/dev/null || true
    ;;
  *)
    echo "usage: $0 {launch|shot <file>|key <spec>|click <x> <y>|status|kill}" >&2
    exit 2
    ;;
esac
