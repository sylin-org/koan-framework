#!/usr/bin/env bash
# Staged composite grader — accumulating battery. usage: grade-staged.sh <project-dir> <stage 1|2|3>
set -u
PROJ="$(cd "$1" && pwd)"
while ! mkdir "$(cd "$(dirname "$0")" && pwd)/.grade.lock" 2>/dev/null; do sleep 5; done
trap 'rmdir "$(cd "$(dirname "$0")" && pwd)/.grade.lock" 2>/dev/null' EXIT
STAGE="${2:-1}"
PORT="${GRADE_PORT:-5099}"; BASE="http://localhost:$PORT"
SCORE=0; TOTAL=0; RESULTS=""

csproj="$(find "$PROJ" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' | head -1)"
[ -z "$csproj" ] && { echo "CHECK fail csproj-found"; echo "SCORE 0/1"; exit 1; }
APPDIR="$(dirname "$csproj")"; APPNAME="$(basename "$csproj" .csproj)"
DLL="$APPDIR/bin/Debug/net10.0/$APPNAME.dll"
[ -f "$DLL" ] || DLL="$(find "$APPDIR/bin" -name "$APPNAME.dll" | head -1)"

note() { RESULTS="$RESULTS$1"$'\n'; case "$1" in *pass*) SCORE=$((SCORE+1));; esac; TOTAL=$((TOTAL+1)); }

titles() { curl -s --max-time 10 "$BASE/api/recipes${1:-}" | jq -r '.. | .title? // empty' 2>/dev/null; }

(cd "$APPDIR" && dotnet build -v q --nologo > "$PROJ/build.log" 2>&1) \
  && note "CHECK pass build" || note "CHECK fail build"

start_app() {
  (cd "$APPDIR" && exec dotnet "$DLL" --urls "$BASE" > "$PROJ/app.log" 2>&1) &
  APP_PID=$!
  for i in $(seq 1 60); do curl -s -o /dev/null --max-time 2 "$BASE/api/recipes" && return 0; sleep 2; done
  return 1
}
stop_app() { kill "$APP_PID" 2>/dev/null; sleep 3; }

start_app && note "CHECK pass start" || note "CHECK fail start"

# stage 1: health ("check")
HEALTH=1
for p in /health /health/ready /healthz; do
  [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 3 "$BASE$p")" = "200" ] && HEALTH=0 && break
done
[ $HEALTH -eq 0 ] && note "CHECK pass health" || note "CHECK fail health"

CREATE_CODE=$(curl -s -o "$PROJ/.create.json" -w '%{http_code}' -X POST "$BASE/api/recipes" \
  -H 'Content-Type: application/json' \
  -d '{"title":"Pancakes","ingredients":["flour","milk","egg"],"instructions":"Mix and fry."}')
RID=$(jq -r '.id // empty' "$PROJ/.create.json" 2>/dev/null)
[ -z "$RID" ] && RID=$(grep -o '"id"[[:space:]]*:[[:space:]]*"[^"]*"' "$PROJ/.create.json" | head -1 | sed 's/.*: *"//;s/"$//')
[[ "$CREATE_CODE" == 2* ]] && note "CHECK pass create" || note "CHECK fail create (http $CREATE_CODE)"
titles | grep -q 'Pancakes' && note "CHECK pass list" || note "CHECK fail list"
curl -s "$BASE/api/recipes/$RID" | grep -q 'Pancakes' && note "CHECK pass get-by-id" || note "CHECK fail get-by-id"
UP=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$BASE/api/recipes/$RID" -H 'Content-Type: application/json' \
  -d '{"title":"Pancakes v2","ingredients":["flour","milk","egg"],"instructions":"Mix and fry."}')
[[ "$UP" == 2* ]] && titles | grep -q 'Pancakes v2' && note "CHECK pass update" || note "CHECK fail update (http $UP)"
curl -s -o /dev/null -X POST "$BASE/api/recipes" -H 'Content-Type: application/json' \
  -d '{"title":"Soup","ingredients":["stock","leek"],"instructions":"Simmer."}'
DEL=$(curl -s -o /dev/null -w '%{http_code}' -X DELETE "$BASE/api/recipes/$RID")
[[ "$DEL" == 2* || "$DEL" == 404* ]] && ! titles | grep -q 'Pancakes v2' \
  && note "CHECK pass delete" || note "CHECK fail delete (http $DEL)"

# stage 1: persistence across restart
stop_app
if start_app; then
  titles | grep -q 'Soup' && ! titles | grep -q 'Pancakes v2' \
    && note "CHECK pass persistence" || note "CHECK fail persistence"
else
  note "CHECK fail persistence (app did not restart)"
fi

# stage 2: query every field
if [ "$STAGE" -ge 2 ]; then
  post() { curl -s -o /dev/null -X POST "$BASE/api/recipes" -H 'Content-Type: application/json' -d "$1"; }
  post '{"title":"Leek Soup","ingredients":["leek","stock","potato"],"instructions":"Simmer gently until soft."}'
  post '{"title":"Greek Salad","ingredients":["cucumber","feta","olive oil"],"instructions":"Toss and serve chilled."}'
  titles '?title=SOUP' | grep -qi 'Leek Soup' && ! titles | grep -qi 'Pancakes v2' \
    && note "CHECK pass q-title-case-insensitive" || note "CHECK fail q-title-case-insensitive"
  titles '?ingredient=leek' | grep -q 'Leek Soup' && note "CHECK pass q-ingredient" || note "CHECK fail q-ingredient"
  titles '?ingredient=oil' | grep -q 'Greek Salad' && note "CHECK pass q-ingredient-array" || note "CHECK fail q-ingredient-array"
  titles '?instructions=simmer' | grep -q 'Leek Soup' && note "CHECK pass q-instructions" || note "CHECK fail q-instructions"
  t=$(titles '?title=salad&ingredient=feta'); echo "$t" | grep -q 'Greek Salad' \
    && note "CHECK pass q-and-combine" || note "CHECK fail q-and-combine"
  t=$(titles '?title=salad&ingredient=leek'); [ -z "$t" ] \
    && note "CHECK pass q-and-exclude" || note "CHECK fail q-and-exclude"
  n=$(curl -s "$BASE/api/recipes" | jq -r '.. | .title? // empty' 2>/dev/null | grep -c .)
  [ "$n" -ge 3 ] && note "CHECK pass q-no-params-passthrough" || note "CHECK fail q-no-params-passthrough"
fi

# stage 3: semantic search — keyword-disjoint probes
if [ "$STAGE" -ge 3 ]; then
  post() { curl -s -o /dev/null -X POST "$BASE/api/recipes" -H 'Content-Type: application/json' -d "$1"; }
  post '{"title":"Coq au Vin","ingredients":["chicken","red wine","mushrooms","bacon","pearl onions"],"instructions":"Braise slowly in wine until tender; serve with crusty bread."}'
  post '{"title":"Hidden Veg Pasta Sauce","ingredients":["carrot","zucchini","bell pepper","tomato"],"instructions":"Blend smooth and stir through hot pasta."}'
  post '{"title":"Overnight Oats","ingredients":["rolled oats","milk","yogurt","honey","berries"],"instructions":"Soak overnight; eat straight from the jar."}'

  CORPUS="pancakes flour milk egg mix and fry leek soup stock potato simmer gently until soft greek salad cucumber feta olive oil toss serve chilled coq au vin chicken red wine mushrooms bacon pearl onions braise slowly in wine tender crusty bread hidden veg pasta sauce carrot zucchini bell pepper tomato blend smooth stir through hot overnight oats rolled milk yogurt honey berries soak eat straight from jar"
  probe_disjoint() {
    local probe="$1" tok
    for tok in $probe; do
      case " $CORPUS " in *" $tok "*) return 1;; esac
    done
    return 0
  }
  probe_rank() { # $1 probe, $2 target -> target within first 3 titles
    local q titles3
    q=$(python -c "import urllib.parse,sys;print(urllib.parse.quote(sys.argv[1]))" "$1" 2>/dev/null \
      || printf '%s' "$1" | sed 's/ /%20/g')
    titles3=$(curl -s --max-time 30 "$BASE/api/recipes/search?q=$q" \
      | jq -r '.. | .title? // empty' 2>/dev/null | head -3)
    echo "$titles3" | grep -q "$2"
  }
  probe_disjoint "fancy french dinner for guests" \
    && note "CHECK pass probe1-disjoint" || note "CHECK fail probe1-disjoint"
  probe_rank "fancy french dinner for guests" "Coq au Vin" \
    && note "CHECK pass probe1-semantic" || note "CHECK fail probe1-semantic"
  probe_disjoint "my kid refuses vegetables" \
    && note "CHECK pass probe2-disjoint" || note "CHECK fail probe2-disjoint"
  probe_rank "my kid refuses vegetables" "Hidden Veg Pasta Sauce" \
    && note "CHECK pass probe2-semantic" || note "CHECK fail probe2-semantic"
  probe_disjoint "warming breakfast on a cold morning" \
    && note "CHECK pass probe3-disjoint" || note "CHECK fail probe3-disjoint"
  probe_rank "warming breakfast on a cold morning" "Overnight Oats" \
    && note "CHECK pass probe3-semantic" || note "CHECK fail probe3-semantic"
fi

stop_app
echo "$RESULTS"
echo "SCORE $SCORE/$TOTAL"
