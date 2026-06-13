#!/usr/bin/env bash
# Surface-ledger lint (Epic E02).
#
# Asserts docs/SURFACES.md exists and its ledger table parses:
#   - the exact 5-column header is present, verbatim,
#   - a markdown separator row follows it,
#   - there are at least 10 data rows, each with exactly 5 cells,
#   - every "Last exercised" cell is a real date (YYYY-MM-DD) or
#     "unknown since <date>" — never a guessed "works".
#
# Usage: scripts/lint-surfaces.sh [path-to-SURFACES.md]
set -euo pipefail

FILE="${1:-docs/SURFACES.md}"
HEADER='| Surface | Exercised by | Last exercised | Guard | Notes |'
MIN_ROWS=10

[ -f "$FILE" ] || { echo "::error::$FILE is missing"; exit 1; }

hdr_line="$(grep -nxF "$HEADER" "$FILE" | head -n1 | cut -d: -f1 || true)"
[ -n "$hdr_line" ] || { echo "::error::ledger header row not found verbatim in $FILE"; exit 1; }

sep_line=$((hdr_line + 1))
sep="$(sed -n "${sep_line}p" "$FILE")"
case "$sep" in
  \|*-*\|) : ;;
  *) echo "::error::missing table separator row after header (line $sep_line)"; exit 1 ;;
esac

rows=0
errors=0
line_no=$sep_line
while IFS= read -r line; do
  line_no=$((line_no + 1))
  case "$line" in
    \|*) ;;
    *) break ;;   # first non-table line ends the ledger
  esac
  body="${line#|}"; body="${body%|}"
  IFS='|' read -ra cells <<< "$body"
  if [ "${#cells[@]}" -ne 5 ]; then
    echo "::error::line $line_no: expected 5 cells, got ${#cells[@]}: $line"
    errors=$((errors + 1)); continue
  fi
  le="${cells[2]}"
  le="$(printf '%s' "$le" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
  if ! printf '%s' "$le" | grep -Eq '^[0-9]{4}-[0-9]{2}-[0-9]{2}$|^unknown since [0-9]{4}-[0-9]{2}-[0-9]{2}$'; then
    echo "::error::line $line_no: 'Last exercised' must be YYYY-MM-DD or 'unknown since <date>', got: '$le'"
    errors=$((errors + 1))
  fi
  rows=$((rows + 1))
done < <(tail -n +"$((sep_line + 1))" "$FILE")

if [ "$rows" -lt "$MIN_ROWS" ]; then
  echo "::error::expected >=$MIN_ROWS surface rows, found $rows"; exit 1
fi
if [ "$errors" -gt 0 ]; then
  echo "::error::$errors malformed row(s) in $FILE"; exit 1
fi

echo "OK: $FILE — $rows surface rows; header + separator valid; all 'Last exercised' dates honest."
