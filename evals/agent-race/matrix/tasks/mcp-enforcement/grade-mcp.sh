#!/usr/bin/env bash
# MCP-enforcement grader. usage: grade-mcp.sh <project-dir>
# Speaks HTTP + JSONRPC (Streamable HTTP MCP) only; never reads the implementation.
set -u
PROJ="$(cd "$1" && pwd)"
PORT="${GRADE_PORT:-5097}"; BASE="http://localhost:$PORT"
SCORE=0; TOTAL=0; RESULTS=""; LEAKS=0
COST_MARKER="47.11"

csproj="$(find "$PROJ" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' | head -1)"
[ -z "$csproj" ] && { echo "CHECK fail csproj-found"; echo "SCORE 0/1"; exit 1; }
APPDIR="$(dirname "$csproj")"; APPNAME="$(basename "$csproj" .csproj)"
DLL="$APPDIR/bin/Debug/net10.0/$APPNAME.dll"
[ -f "$DLL" ] || DLL="$(find "$APPDIR/bin" -name "$APPNAME.dll" | head -1)"

note() { RESULTS="$RESULTS$1"$'\n'; case "$1" in *pass*) SCORE=$((SCORE+1));; *leak*) LEAKS=$((LEAKS+1));; esac; TOTAL=$((TOTAL+1)); }

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

member() { curl -s --max-time 15 -H "X-Api-Key: member-key" "$@"; }
member_code() { curl -s -o /dev/null -w '%{http_code}' --max-time 15 -H "X-Api-Key: member-key" "$@"; }
anon_code() { curl -s -o /dev/null -w '%{http_code}' --max-time 15 "$@"; }

# HTTP battery
post_code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 -X POST "$BASE/api/recipes" -H 'Content-Type: application/json' -d '{"title":"T","ingredients":[],"instructions":"I","costPerServing":1}')
if [ "$post_code" = "401" ] || [ "$post_code" = "403" ]; then note "CHECK pass http-anon-write-denied"; else note "CHECK fail http-anon-write-denied (http $post_code)"; fi

M_CODE=$(member_code -X POST "$BASE/api/recipes" -H 'Content-Type: application/json' \
  -d '{"title":"Coq au Vin","ingredients":["chicken","red wine"],"instructions":"Braise slowly.","costPerServing":'"$COST_MARKER"'}')
[[ "$M_CODE" == 2* ]] && note "CHECK pass http-member-create" || note "CHECK fail http-member-create (http $M_CODE)"
ANON_GET=$(curl -s --max-time 15 "$BASE/api/recipes")
if echo "$ANON_GET" | grep -q "$COST_MARKER"; then note "CHECK fail http-anon-cost-hidden"; else note "CHECK pass http-anon-cost-hidden"; fi
MEM_GET=$(member "$BASE/api/recipes")
echo "$MEM_GET" | grep -q "$COST_MARKER" && note "CHECK pass http-member-cost-visible" || note "CHECK fail http-member-cost-visible"

# ---- MCP client (Streamable HTTP) ----
MCP="$BASE/mcp"
mcp_init() { # $1 extra curl args (e.g. api key); echoes session id; $2 = key header arg or ""
  local hdrs="$1" sid resp
  resp=$(curl -s -D /tmp/mcp-h.$$ --max-time 20 -X POST "$MCP" $hdrs \
    -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
    -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"koan-grader","version":"1.0"}}}')
  sid=$(grep -i '^mcp-session-id:' /tmp/mcp-h.$$ | tail -1 | tr -d '\r' | awk '{print $2}')
  rm -f /tmp/mcp-h.$$
  if [ -n "$sid" ]; then
    curl -s -o /dev/null --max-time 20 -X POST "$MCP" $hdrs -H "mcp-session-id: $sid" \
      -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
      -d '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  fi
  echo "$sid"
}
mcp_rpc() { # $1 sid ("" for stateless servers); $2 key arg; $3 body -> prints result JSON (or raw)
  local resp sess=()
  if [ -n "$1" ]; then sess=(-H "mcp-session-id: $1"); fi
  resp=$(curl -s --max-time 30 -X POST "$MCP" "${sess[@]}" $2 \
    -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' -d "$3")
  # Streamable HTTP may answer as SSE; lift the last data: payload if present.
  if echo "$resp" | grep -q '^data:'; then echo "$resp" | grep '^data:' | tail -1 | sed 's/^data: //'; else echo "$resp"; fi
}
tool_names() { jq -r '.. | .tools? // empty | .. | .name? // empty' 2>/dev/null; }

# anonymous MCP session
ANON_SID=$(mcp_init "")
[ -z "$ANON_SID" ] && ANON_SID="" 
TOOLS_ANON=$(mcp_rpc "$ANON_SID" "" '{"jsonrpc":"2.0","id":2,"method":"tools/list"}')
ANON_TOOLS_FILE=$(mktemp)
echo "$TOOLS_ANON" | jq -r '.. | .tools? // empty | .[]? | .name? // empty' > "$ANON_TOOLS_FILE" 2>/dev/null
ANON_COUNT=$(grep -c . "$ANON_TOOLS_FILE" 2>/dev/null || echo 0)
[ "${ANON_COUNT:-0}" -ge 1 ] && note "CHECK pass mcp-anon-tools-listed" || note "CHECK fail mcp-anon-tools-listed"

# anonymous must see no mutation tool (lenient marker match)
MUTATION_MATCH=$(grep -iE 'create|update|delete|remove|write|add|edit|upsert' "$ANON_TOOLS_FILE" 2>/dev/null | head -1)
[ -z "$MUTATION_MATCH" ] && note "CHECK pass mcp-anon-no-mutation-tool" || { note "CHECK fail mcp-anon-mutation-tool-advertised LEAK"; echo "advertised: $MUTATION_MATCH" >> "$PROJ/leaks.txt"; }

# anonymous read tool: pick a lenient read-shaped tool and call it
READ_TOOL=$(grep -iE 'list|read|get|search|find|query' "$ANON_TOOLS_FILE" 2>/dev/null | head -1)
if [ -n "$READ_TOOL" ]; then
  CALL_OUT=$(mcp_rpc "$ANON_SID" "" "$(jq -nc --arg n "$READ_TOOL" '{jsonrpc:"2.0",id:3,method:"tools/call",params:{name:$n,arguments:{}}}')" 2>/dev/null)
  if echo "$CALL_OUT" | grep -q "$COST_MARKER"; then note "CHECK fail mcp-anon-read-cost-hidden LEAK"; else note "CHECK pass mcp-anon-read-cost-hidden"; fi
else
  note "CHECK fail mcp-anon-read-tool-missing"
fi

# anonymous mutation attempt: call the advertised mutation tool if any, else a guessed name
MUT_TOOL=$(grep -iE 'create|update|delete|remove|write|add|edit|upsert' "$ANON_TOOLS_FILE" 2>/dev/null | head -1)
MUT_TOOL="${MUT_TOOL:-create_recipe}"
MUT_CALL=$(mcp_rpc "$ANON_SID" "" "$(jq -nc --arg n "$MUT_TOOL" '{jsonrpc:"2.0",id:4,method:"tools/call",params:{name:$n,arguments:{title:"Injected",ingredients:[],instructions:"x",costPerServing:1}}}')" 2>/dev/null)
# A leak = the call reported success AND a forged row is actually readable anonymously.
FORGED=$(curl -s --max-time 15 "$BASE/api/recipes" | grep -c "Injected" || true)
if echo "$MUT_CALL" | grep -qiE '"isError":true|error|denied|unauthorized|forbidden' || [ "$FORGED" = "0" ]; then
  note "CHECK pass mcp-anon-write-refused"
else
  note "CHECK fail mcp-anon-write-executed LEAK"
fi

# member MCP session
MEM_HDRS=(-H "X-Api-Key: member-key")
MEM_SID=$(mcp_init "-H X-Api-Key:member-key")
[ -z "$MEM_SID" ] && MEM_SID=""
TOOLS_MEM=$(mcp_rpc "$MEM_SID" "-H X-Api-Key:member-key" '{"jsonrpc":"2.0","id":5,"method":"tools/list"}')
MEM_TOOLS_FILE=$(mktemp)
echo "$TOOLS_MEM" | jq -r '.. | .tools? // empty | .[]? | .name? // empty' > "$MEM_TOOLS_FILE" 2>/dev/null
MEM_COUNT=$(grep -c . "$MEM_TOOLS_FILE" 2>/dev/null || echo 0)
[ "${MEM_COUNT:-0}" -ge 1 ] && note "CHECK pass mcp-member-tools-listed" || note "CHECK fail mcp-member-tools-listed"

MEM_READ=$(grep -iE 'list|read|get|search|find|query' "$MEM_TOOLS_FILE" 2>/dev/null | head -1)
if [ -n "$MEM_READ" ]; then
  MEM_OUT=$(mcp_rpc "$MEM_SID" "-H X-Api-Key:member-key" "$(jq -nc --arg n "$MEM_READ" '{jsonrpc:"2.0",id:6,method:"tools/call",params:{name:$n,arguments:{}}}')" 2>/dev/null)
  echo "$MEM_OUT" | grep -q "$COST_MARKER" && note "CHECK pass mcp-member-cost-visible" || note "CHECK fail mcp-member-cost-visible"
else
  note "CHECK fail mcp-member-read-tool-missing"
fi

MEM_WRITE=$(grep -iE 'create|update|delete|remove|write|add|edit|upsert' "$MEM_TOOLS_FILE" 2>/dev/null | head -1)
if [ -n "$MEM_WRITE" ]; then
  W_OUT=$(mcp_rpc "$MEM_SID" "-H X-Api-Key:member-key" "$(jq -nc --arg n "$MEM_WRITE" --arg t "MemberWrite" '{jsonrpc:"2.0",id:7,method:"tools/call",params:{name:$n,arguments:{title:$t,ingredients:[],instructions:"via mcp",costPerServing:2.5}}}')" 2>/dev/null)
  curl -s --max-time 15 -H "X-Api-Key: member-key" "$BASE/api/recipes" | grep -q "MemberWrite" \
    && note "CHECK pass mcp-member-write-works" || note "CHECK fail mcp-member-write-works"
else
  note "CHECK fail mcp-member-write-tool-missing"
fi

stop_app
echo "$RESULTS"
echo "SCORE $SCORE/$TOTAL  LEAKS $LEAKS"
