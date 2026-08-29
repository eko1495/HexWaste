#!/usr/bin/env bash
# Fails when a C# string or char literal carries a character above U+00FF.
#
# Why this exists: the game's own fonts are AAF, which are BYTE-indexed — 256 glyph
# records — so a character above U+00FF has no glyph and never can. Before
# AafFont.GlyphIndex was added, a plain (byte) cast TRUNCATED such a character
# (U+2014 '—' became 0x14, a control slot holding an arbitrary glyph); it now
# degrades to '?', which is visible but still wrong. Either way the string is a
# defect, and no golden fixture can catch it: not one of the 279 committed
# transcripts contains a single non-ASCII byte, so this whole class of bug is
# invisible to the suite by construction. That is what this lint replaces.
#
# Console output and the OS window title are NOT rendered with the game's font, so
# they are legitimately allowed an em-dash. Mark those lines with a trailing
#   // ascii-ok: <reason>
# The marker is deliberately per-line and requires a reason: the point is to force
# each case to be classified, not to provide a blanket exemption.
#
# Known limitation: the scan is line-by-line, so it cannot see inside a string
# that spans lines (a multi-line @"..." or """...""" literal) — a high character
# on a continuation line would be missed. src/ currently holds no such literal
# (139 verbatim/raw literals, all opened and closed on one line), so the gap is
# real but empty. A trailing // comment on a code line is likewise not scanned;
# comments are never rendered.
set -uo pipefail
cd "$(dirname "$0")/.."

python3 - "$@" <<'PY'
import re, sys, pathlib

LITERAL = re.compile(r'"(?:[^"\\\n]|\\.)*"|\'(?:[^\'\\\n]|\\.)*\'')
violations = []

for path in sorted(pathlib.Path('src').rglob('*.cs')):
    for lineno, line in enumerate(path.read_text(encoding='utf-8', errors='replace').splitlines(), 1):
        stripped = line.lstrip()
        # Comments may say whatever they like — they are never rendered.
        if stripped.startswith(('//', '///', '*', '/*')):
            continue
        if 'ascii-ok:' in line:
            continue
        for match in LITERAL.finditer(line):
            high = sorted({c for c in match.group(0) if ord(c) > 0xFF})
            if high:
                chars = ' '.join(f"U+{ord(c):04X} {c!r}" for c in high)
                violations.append(f"{path}:{lineno}: {chars}\n    {stripped[:100]}")

if violations:
    print(f"ascii-lint: {len(violations)} literal(s) carry a character the game font cannot render:\n")
    print("\n".join(violations))
    print("\nFix the string, or mark the line `// ascii-ok: <reason>` if it goes to the console")
    print("or the OS window title rather than through AafFontRenderer.")
    sys.exit(1)

print("ascii-lint: clean")
PY
