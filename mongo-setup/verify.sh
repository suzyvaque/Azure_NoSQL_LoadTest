#!/usr/bin/env bash
#
# verify.sh — verify a Mongo-compatible backend is ready for the BMT workload.
#
# Prints the server version, the index list for calc_input and calc_output, and confirms
# that ONLY the default `_id` index exists on each (the sequential-id design needs no
# secondary index). Replica-set status and profiler level are reported when the backend
# supports them (MongoDB on a VM); on managed backends (DocumentDB vCore, Cosmos RU) those
# checks are skipped gracefully.
#
# Usage:
#   ./verify.sh "<connection-string>" [db_name]
#
# Exit codes:
#   0  verification passed
#   1  unexpected index layout (a secondary index is present, or a collection is missing)
#   2  usage / connectivity error

set -euo pipefail

CONN="${1:-}"
DB_NAME="${2:-bmt_db}"

if [[ -z "${CONN}" ]]; then
  echo "usage: $0 \"<connection-string>\" [db_name]" >&2
  exit 2
fi

if ! command -v mongosh >/dev/null 2>&1; then
  echo "ERROR: mongosh not found on PATH." >&2
  exit 2
fi

echo "=== verify.sh : database '${DB_NAME}' ==="

# Single round-trip: emit a small JSON report from the server, then assert on it locally.
REPORT="$(mongosh "${CONN}" --quiet --eval "
const dbName = '${DB_NAME}';
const target = db.getSiblingDB(dbName);
function indexNames(c) {
  try { return target.getCollection(c).getIndexes().map(i => i.name); }
  catch (e) { return null; }
}
let version = null;
try { version = db.version(); } catch (e) {}
let rsState = null;
try { rsState = rs.status().myState; } catch (e) {}
let profiler = null;
try { profiler = target.getProfilingStatus().was; } catch (e) {}
printjson({
  version: version,
  replicaSetState: rsState,
  profilerLevel: profiler,
  calc_input: indexNames('calc_input'),
  calc_output: indexNames('calc_output')
});
")"

echo "${REPORT}"

# Parse the report with mongosh's JSON (node) to keep dependencies minimal.
ASSERT="$(mongosh --quiet --nodb --eval "
const r = ${REPORT};
const expected = ['_id_'];
function only_id(names) {
  return Array.isArray(names) && names.length === 1 && names[0] === '_id_';
}
let problems = [];
['calc_input','calc_output'].forEach(c => {
  if (r[c] === null) problems.push(c + ' is missing');
  else if (!only_id(r[c])) problems.push(c + ' has unexpected indexes: [' + r[c].join(', ') + ']');
});
print(problems.length ? 'FAIL: ' + problems.join('; ') : 'PASS');
")"

echo ""
echo "${ASSERT}"

if [[ "${ASSERT}" == PASS ]]; then
  echo "OK: calc_input and calc_output have _id indexes only."
  exit 0
else
  exit 1
fi
