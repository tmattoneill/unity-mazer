#!/bin/zsh
set -euo pipefail

script_dir=${0:A:h}
project_dir=${script_dir:h}
app_path=${1:-"$project_dir/Builds/MemoryAudit/UnityMazer.app"}
report_dir=${2:-"$project_dir/Builds/MemoryAudit/reports/$(date -u +%Y%m%d-%H%M%S)"}
app_path=${app_path:A}
report_dir=${report_dir:A}
executable_path="$app_path/Contents/MacOS/UnityMazer"
diagnostic_dir="$HOME/Library/Logs/DiagnosticReports"

if [[ ! -d "$app_path" ]]; then
  print -u2 "Missing audit build: $app_path"
  exit 64
fi

mkdir -p "$report_dir"
typeset -a crashes_before
typeset -a crashes_after
setopt null_glob
crashes_before=("$diagnostic_dir"/UnityMazer-*.ips)

find_player_pid() {
  local attempt pid
  for attempt in {1..300}; do
    pid=$(pgrep -f "$executable_path" | tail -n 1 || true)
    if [[ -n "$pid" ]]; then
      print "$pid"
      return 0
    fi
    sleep 0.1
  done
  return 1
}

run_launch() {
  local name=$1
  local mode=$2
  local json_path="$report_dir/$name.json"
  local rss_path="$report_dir/$name-rss.tsv"
  local open_pid player_pid sample=0

  /usr/bin/open -n -W "$app_path" --args "$mode" --memory-audit-output "$json_path" &
  open_pid=$!
  player_pid=$(find_player_pid) || {
    print -u2 "LaunchServices did not produce a UnityMazer process for $name"
    wait "$open_pid" || true
    return 1
  }

  printf 'unix_time\trss_kib\n' > "$rss_path"
  while kill -0 "$player_pid" 2>/dev/null; do
    printf '%s\t%s\n' "$(date +%s)" "$(ps -o rss= -p "$player_pid" | tr -d ' ')" >> "$rss_path"
    if (( sample % 2 == 0 )); then
      /usr/bin/vmmap -summary "$player_pid" > "$report_dir/$name-vmmap-$sample.txt" 2>&1 || true
    fi
    sample=$((sample + 1))
    sleep 1
  done
  wait "$open_pid" || true

  if [[ ! -s "$json_path" ]]; then
    print -u2 "$name exited without a JSON report"
    return 1
  fi
  if ! grep -q '"passed": true' "$json_path"; then
    print -u2 "$name reported a failed memory check"
    return 1
  fi
}

for launch in {1..5}; do
  run_launch "cold-launch-$launch" --memory-audit-smoke
done
run_launch full-audit --memory-audit

crashes_after=("$diagnostic_dir"/UnityMazer-*.ips)
if (( ${#crashes_after} > ${#crashes_before} )); then
  print -u2 "UnityMazer created a macOS crash report during the audit"
  exit 2
fi

awk 'NR > 1 && $2 > peak { peak = $2 } END { printf "Peak sampled RSS: %.1f MiB\n", peak / 1024 }' "$report_dir/full-audit-rss.tsv"

typeset -a vmmap_files
vmmap_files=("$report_dir"/full-audit-vmmap-*.txt(Nn))
if (( ${#vmmap_files} == 0 )); then
  print -u2 "The full audit produced no vmmap samples"
  exit 2
fi

if ! physical_summary=$(awk '
  function mib(value) {
    unit = substr(value, length(value), 1)
    number = substr(value, 1, length(value) - 1) + 0
    if (unit == "G") return number * 1024
    if (unit == "M") return number
    if (unit == "K") return number / 1024
    return value / 1048576
  }
  /^Physical footprint:/ {
    current = mib($3)
    if (current > peak) peak = current
    final = current
    samples++
  }
  /^Physical footprint \(peak\):/ {
    recordedPeak = mib($4)
    if (recordedPeak > peak) peak = recordedPeak
    peakSamples++
  }
  END {
    if (samples == 0 || peakSamples == 0 || peak <= 0 || final <= 0) exit 1
    printf "%.1f %.1f", peak, final
  }
' "${vmmap_files[@]}"); then
  print -u2 "The full audit produced no valid physical-footprint or peak measurements"
  exit 2
fi
read peak_physical_mib final_physical_mib <<< "$physical_summary"
printf 'Peak physical footprint: %.1f MiB\n' "$peak_physical_mib"
printf 'Final sampled physical footprint: %.1f MiB\n' "$final_physical_mib"
if (( peak_physical_mib > 1024.0 )); then
  print -u2 "Peak physical footprint exceeded 1.0 GiB"
  exit 2
fi
if (( final_physical_mib > 700.0 )); then
  print -u2 "Settled physical footprint exceeded 700 MiB"
  exit 2
fi
print "Memory audit passed: $report_dir"
