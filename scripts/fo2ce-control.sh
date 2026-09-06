#!/usr/bin/env bash
# Control fo2ce (reference/fallout2-ce/run/fallout2-ce) for the comparison-automation pilot.
# See docs/fo2ce-comparison-playbook.md for the full workflow this supports.
#
# Usage:
#   scripts/fo2ce-control.sh launch                 # start fo2ce, print its PID
#   scripts/fo2ce-control.sh shot <output.png>       # screenshot the whole screen
#   scripts/fo2ce-control.sh key <xdotool-key-spec>  # e.g. "Return", "Down Down Return"
#   scripts/fo2ce-control.sh move <dx> <dy>          # jog the in-game cursor by a relative offset
#   scripts/fo2ce-control.sh click                   # click at the CURRENT cursor position
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
  move)
    # fo2ce runs SDL in relative-mouse mode (src/dinput.cc), so an absolute mousemove warp
    # never reaches the game's cursor — only real relative motion events do.
    dx="${1:?usage: move <dx> <dy>}"; dy="${2:?usage: move <dx> <dy>}"
    wid="$(DISPLAY="$DISP" xdotool search --name "FALLOUT II" | head -1)"
    [ -n "$wid" ] || { echo "fo2ce window not found" >&2; exit 1; }
    DISPLAY="$DISP" xdotool windowactivate "$wid"
    DISPLAY="$DISP" xdotool mousemove_relative -- "$dx" "$dy"
    ;;
  click)
    # A combined "mousemove ... click 1" was found unreliable against fo2ce; a separate
    # mousedown/sleep/mouseup at the cursor's current position (set via `move` first) is what
    # actually registers. Use `move` to position the cursor, then `click` to press here.
    wid="$(DISPLAY="$DISP" xdotool search --name "FALLOUT II" | head -1)"
    [ -n "$wid" ] || { echo "fo2ce window not found" >&2; exit 1; }
    DISPLAY="$DISP" xdotool windowactivate "$wid"
    DISPLAY="$DISP" xdotool mousedown 1
    sleep 0.15
    DISPLAY="$DISP" xdotool mouseup 1
    ;;
  status)
    pgrep -f "$BIN" >/dev/null 2>&1
    ;;
  kill)
    pkill -f "$BIN" 2>/dev/null || true
    ;;
  *)
    echo "usage: $0 {launch|shot <file>|key <spec>|move <dx> <dy>|click|status|kill}" >&2
    exit 2
    ;;
esac
