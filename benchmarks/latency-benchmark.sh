#!/usr/bin/env bash
# ll-lang compile-latency benchmark
# Measures wall-clock time for lllc build across representative scenarios.
# Protocol: 5 runs per scenario, record median, p90, max (discard first run).
# Output: benchmarks/results/latency-YYYY-MM-DD.json

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LLLC="dotnet ${REPO_ROOT}/src/LLLangTool/bin/Debug/net10.0/lllc.dll"
TODAY="$(date +%F)"
OUT_JSON="${REPO_ROOT}/benchmarks/results/latency-${TODAY}.json"
OUT_MD="${REPO_ROOT}/benchmarks/results/latency-${TODAY}.md"

# Warmup build to ensure no cold-start in measurements
echo "Warming up..."
${LLLC} build "${REPO_ROOT}/spec/examples/valid/01-basics.lll" >/dev/null 2>&1 || true

# Scenarios per M7 protocol:
#   single-file, multi-module stdlib, dependency-bearing, self-build
declare -a SCENARIO_NAMES=("single-file" "stdlib-map" "stdlib-toml" "self-build")
declare -a SCENARIO_FILES=(
    "${REPO_ROOT}/spec/examples/valid/01-basics.lll"
    "${REPO_ROOT}/stdlib/src/Map.lll"
    "${REPO_ROOT}/stdlib/src/Toml.lll"
    "${REPO_ROOT}/spec/examples/valid/20-bootstrap-compiler.lll"
)

RUNS=5  # number of timed runs (first run is discarded → 4 actual data points)

echo ""
echo "ll-lang Compile-Latency Benchmark"
echo "==================================="
echo "Date:   ${TODAY}"
echo "Runs:   ${RUNS} per scenario (first discarded)"
echo ""

# Sort a space-separated list of integers and pick element at 0-based index N.
# Usage: pick_sorted_index "1 4 2 3" 2  → picks 3rd element of sorted list
pick_sorted_index() {
    local nums="$1"
    local idx="$2"
    printf '%s\n' ${nums} | sort -n | awk -v i="$((idx + 1))" 'NR==i'
}

array_median() {
    local arr=("$@")
    local n=${#arr[@]}
    local mid=$(( n / 2 ))
    local joined="${arr[*]}"
    pick_sorted_index "$joined" "$mid"
}

array_p90() {
    local arr=("$@")
    local n=${#arr[@]}
    local idx=$(( (n * 9) / 10 ))
    [ "$idx" -ge "$n" ] && idx=$(( n - 1 ))
    local joined="${arr[*]}"
    pick_sorted_index "$joined" "$idx"
}

array_max() {
    local arr=("$@")
    local joined="${arr[*]}"
    printf '%s\n' ${joined} | sort -n | tail -n 1
}

# JSON accumulator
json_rows=""

for i in "${!SCENARIO_NAMES[@]}"; do
    name="${SCENARIO_NAMES[$i]}"
    file="${SCENARIO_FILES[$i]}"

    if [ ! -f "$file" ]; then
        echo "SKIP $name — file not found: $file"
        continue
    fi

    echo -n "  $name ... "
    times=()

    for run in $(seq 1 $RUNS); do
        start_ms=$(date +%s%3N)
        ${LLLC} build "$file" >/dev/null 2>&1 || true
        end_ms=$(date +%s%3N)
        elapsed=$(( end_ms - start_ms ))
        times+=("$elapsed")
    done

    # Discard first run (cold-start)
    data=("${times[@]:1}")

    median=$(array_median "${data[@]}")
    p90=$(array_p90 "${data[@]}")
    max=$(array_max "${data[@]}")

    echo "median=${median}ms  p90=${p90}ms  max=${max}ms"

    # Build JSON runs array using printf to avoid global IFS
    runs_csv=$(printf '%s,' "${data[@]}")
    runs_csv="${runs_csv%,}"  # strip trailing comma

    row="{\"scenario\":\"${name}\",\"file\":\"${file##${REPO_ROOT}/}\",\"runs_ms\":[${runs_csv}],\"median_ms\":${median},\"p90_ms\":${p90},\"max_ms\":${max}}"
    if [ -n "$json_rows" ]; then
        json_rows="${json_rows},${row}"
    else
        json_rows="${row}"
    fi
done

# Write JSON
cat > "$OUT_JSON" << JSONEOF
{
  "generated": "${TODAY}",
  "protocol": "5 runs per scenario, first discarded, median/p90/max of 4",
  "variance_guidance": {
    "noise_threshold_pct": 10,
    "notable_pct": 30,
    "regression_pct": 30
  },
  "measurements": [${json_rows}]
}
JSONEOF

# Write Markdown
{
    echo "# ll-lang Compile-Latency Benchmark"
    echo ""
    echo "*Generated: ${TODAY}*"
    echo "*Protocol: 5 runs per scenario, first discarded. Median/p90/max of 4 runs.*"
    echo ""
    echo "| Scenario | Median (ms) | p90 (ms) | Max (ms) |"
    echo "|----------|-------------|----------|----------|"
} > "$OUT_MD"

python3 - "$OUT_JSON" >> "$OUT_MD" << 'PYEOF'
import json, sys
d = json.load(open(sys.argv[1]))
for r in d["measurements"]:
    print(f'| {r["scenario"]} | {r["median_ms"]} | {r["p90_ms"]} | {r["max_ms"]} |')
PYEOF

{
    echo ""
    echo "**Variance guidance:**"
    echo "- < 10% change on median: within noise, no action."
    echo "- 10–30% change on median: notable; comment in PR."
    echo "> 30% change on median: regression; block or justify explicitly."
} >> "$OUT_MD"

echo ""
echo "Results written:"
echo "  JSON: ${OUT_JSON}"
echo "  MD:   ${OUT_MD}"
