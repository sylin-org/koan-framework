#!/usr/bin/env bash
# test03 grader — HTTP only. usage: grade-test03.sh <project-dir>
set -u
PROJ="$(cd "$1" && pwd)"
PORT="${GRADE_PORT:-5099}"; BASE="http://localhost:$PORT"
SCORE=0; TOTAL=0; RESULTS=""
csproj="$(find "$PROJ" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' | head -1)"
[ -z "$csproj" ] && { echo "CHECK fail csproj-found"; echo "SCORE 0/1"; exit 1; }
APPDIR="$(dirname "$csproj")"; APPNAME="$(basename "$csproj" .csproj)"
DLL="$APPDIR/bin/Debug/net10.0/$APPNAME.dll"; [ -f "$DLL" ] || DLL="$(find "$APPDIR/bin" -name "$APPNAME.dll" | head -1)"
note() { RESULTS="$RESULTS$1"$'\n'; case "$1" in *pass*) SCORE=$((SCORE+1));; esac; TOTAL=$((TOTAL+1)); }
titles() { curl -s --max-time 20 "$BASE/api/recipes$1" | jq -r '.. | .title? // empty' 2>/dev/null; }
(cd "$APPDIR" && dotnet build -v q --nologo > "$PROJ/build.log" 2>&1) && note "CHECK pass build" || note "CHECK fail build"
start_app() { (cd "$APPDIR" && exec dotnet "$DLL" --urls "$BASE" > "$PROJ/app.log" 2>&1) & APP_PID=$!
  for i in $(seq 1 60); do curl -s -o /dev/null --max-time 2 "$BASE/api/recipes" && return 0; sleep 2; done; return 1; }
stop_app() { kill "$APP_PID" 2>/dev/null; sleep 3; }
start_app && note "CHECK pass start" || note "CHECK fail start"
# seed: 12 ingredients, then 6 recipes with join lines
mk() { curl -s -o /dev/null -w '%{http_code}' --max-time 15 -X POST "$BASE/api/ingredients" -H 'Content-Type: application/json' -d "{\"id\":\"$1\",\"name\":\"$1\"}"; }
ING="milk salt onions garlic tomato pasta flour egg butter sugar chicken cream"
SEED_OK=1
for i in $ING; do c=$(mk "$i"); [[ "$c" == 2* || "$c" == 409* || "$c" == 400* ]] || SEED_OK=0; done
[ $SEED_OK = 1 ] && note "CHECK pass seed-ingredients" || note "CHECK fail seed-ingredients"
rec() { curl -s -o /dev/null -w '%{http_code}' --max-time 20 -X POST "$BASE/api/recipes" -H 'Content-Type: application/json' -d "$1"; }
LINES_M480='{"name":"milk","quantity":480,"unit":"ml"}'; LINES_M300='{"name":"milk","quantity":300,"unit":"ml"}'; LINES_M15='{"name":"milk","quantity":15,"unit":"tbsp"}'
rec "{\"title\":\"Pancakes\",\"instructions\":\"Fry.\",\"ingredients\":[$LINES_M480,{\"name\":\"flour\"},{\"name\":\"egg\"}]}" > /dev/null
rec "{\"title\":\"Cream Sauce\",\"instructions\":\"Stir.\",\"ingredients\":[$LINES_M300,{\"name\":\"butter\"},{\"name\":\"pasta\"}]}" > /dev/null
rec "{\"title\":\"Big Feast\",\"instructions\":\"Feast.\",\"ingredients\":[$LINES_M15,{\"name\":\"onions\"},{\"name\":\"salt\"},{\"name\":\"garlic\"},{\"name\":\"tomato\"},{\"name\":\"pasta\"},{\"name\":\"flour\"},{\"name\":\"egg\"},{\"name\":\"butter\"},{\"name\":\"sugar\"},{\"name\":\"chicken\"},{\"name\":\"cream\"}]}" > /dev/null
rec "{\"title\":\"Onion Soup\",\"instructions\":\"Simmer.\",\"ingredients\":[{\"name\":\"onions\"},{\"name\":\"salt\"},{\"name\":\"garlic\"}]}" > /dev/null
rec "{\"title\":\"Salted Pasta\",\"instructions\":\"Boil.\",\"ingredients\":[{\"name\":\"pasta\"},{\"name\":\"salt\"}]}" > /dev/null
rec "{\"title\":\"Veggie Mix\",\"instructions\":\"Toss.\",\"ingredients\":[{\"name\":\"onions\"},{\"name\":\"tomato\"},{\"name\":\"garlic\"}]}" > /dev/null
titles | grep -q "Big Feast" && note "CHECK pass create-join" || note "CHECK fail create-join"
UC=$(curl -s --max-time 15 "$BASE/api/ingredients/milk/usage-count" | jq -r '.. | .count? // empty' 2>/dev/null | head -1)
[ "$UC" = "3" ] && note "CHECK pass usage-count-milk" || note "CHECK fail usage-count-milk (got $UC)"
CONV=$(curl -s --max-time 20 "$BASE/api/recipes/using?ingredient=milk&minQuantity=300&unit=ml" | jq -r '.. | .title? // empty' 2>/dev/null)
echo "$CONV" | grep -q "Pancakes" && echo "$CONV" | grep -q "Cream Sauce" && ! echo "$CONV" | grep -q "Big Feast" \
  && note "CHECK pass conversion-filter" || note "CHECK fail conversion-filter"
STAT=$(curl -s --max-time 15 "$BASE/api/stats" | jq -r '.recipesWithMoreThan10Ingredients? // empty' 2>/dev/null)
[ "$STAT" = "1" ] && note "CHECK pass stat-over10" || note "CHECK fail stat-over10 (got $STAT)"
MATCH=$(curl -s --max-time 20 -X POST "$BASE/api/recipes/match" -H 'Content-Type: application/json' \
  -d '{"pantry":[{"name":"milk","quantity":480,"unit":"ml"},{"name":"salt"},{"name":"onions","quantity":2,"unit":"piece"},{"name":"pasta","quantity":1,"unit":"piece"}]}')
FIRST=$(echo "$MATCH" | jq -r '([.[]?] | .[0]) // empty' 2>/dev/null)
FIRST_TITLE=$(echo "$FIRST" | jq -r '.. | .title? // empty' 2>/dev/null | head -1)
FIRST_MISSING=$(echo "$FIRST" | jq -r '.. | .missing? // empty | length' 2>/dev/null | head -1)
[ "$FIRST_TITLE" = "Salted Pasta" ] && [ "$FIRST_MISSING" = "0" ] \
  && note "CHECK pass pantry-full-match-first" || note "CHECK fail pantry-full-match-first"
LAST_TITLE=$(echo "$MATCH" | jq -r '[.. | .title? // empty] | map(select(length>0)) | last' 2>/dev/null)
LAST_MISSING=$(echo "$MATCH" | jq -r '[.. | objects | select(has("missing")) | .missing | length] | last' 2>/dev/null)
[ "$LAST_TITLE" = "Big Feast" ] && [ "${LAST_MISSING:-0}" -ge 8 ] \
  && note "CHECK pass pantry-rank-last" || note "CHECK fail pantry-rank-last (last=$LAST_TITLE missing=$LAST_MISSING)"
stop_app
start_app && note "CHECK pass restart-persistence" || note "CHECK fail restart-persistence"
probe() { local q titles3
  q=$(printf '%s' "$1" | sed 's/ /%20/g')
  titles3=$(curl -s --max-time 40 "$BASE/api/recipes/search?q=$q" | jq -r '.. | .title? // empty' 2>/dev/null | head -3)
  echo "$titles3" | grep -q "$2"
}
probe "comforting breakfast stack" "Pancakes" && note "CHECK pass semantic-probe-1" || note "CHECK fail semantic-probe-1"
probe "warming winter bowl" "Onion Soup" && note "CHECK pass semantic-probe-2" || note "CHECK fail semantic-probe-2"
stop_app
echo "$RESULTS"
echo "SCORE $SCORE/$TOTAL"
