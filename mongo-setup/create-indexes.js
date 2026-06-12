// create-indexes.js
//
// Creates the benchmark database and collections and ensures the required indexes
// exist on any Mongo-compatible backend (Azure DocumentDB vCore first, then Cosmos RU,
// then MongoDB on a VM).
//
// Workload model: a single sequential numeric-string request id `n` is the `_id` of every
// document. All operations key on `_id` (IDHACK):
//   find({_id:"n"}) on calc_input  ->  insert {_id:"n"} into calc_output  ->  remove({_id:"n"})
// Because every filter hits `_id`, NO secondary index is required. A `ReqId` field equal to
// `_id` is stored for production-schema parity only and is intentionally NOT indexed.
//
// Idempotent: safe to re-run. Collections are created if missing; the default `_id` index is
// always present and is the only index this script relies on.
//
// Usage:
//   mongosh "<connection-string>" --file create-indexes.js
//   mongosh "<connection-string>" --file create-indexes.js --eval "var DB_NAME='bmt_db'"

/* global db, print, printjson */

const DB_NAME = (typeof globalThis.DB_NAME === "string" && globalThis.DB_NAME) || "bmt_db";
const INPUT = "calc_input";
const OUTPUT = "calc_output";

const target = db.getSiblingDB(DB_NAME);

function ensureCollection(name) {
  const existing = target.getCollectionNames();
  if (existing.indexOf(name) === -1) {
    target.createCollection(name);
    print(`created collection ${DB_NAME}.${name}`);
  } else {
    print(`collection ${DB_NAME}.${name} already exists`);
  }
}

function listIndexNames(name) {
  return target
    .getCollection(name)
    .getIndexes()
    .map(function (ix) {
      return ix.name;
    });
}

print(`=== create-indexes.js : database '${DB_NAME}' ===`);

ensureCollection(INPUT);
ensureCollection(OUTPUT);

// No secondary indexes are created on purpose. The default `_id` index backs every operation.
// If a prior run created a ReqId index (per an older plan), drop it so the layout matches
// the sequential-id design.
[INPUT, OUTPUT].forEach(function (name) {
  const coll = target.getCollection(name);
  coll.getIndexes().forEach(function (ix) {
    if (ix.name !== "_id_") {
      print(`dropping unexpected secondary index ${name}.${ix.name}`);
      coll.dropIndex(ix.name);
    }
  });
});

print("");
print("index summary (expect _id_ only):");
[INPUT, OUTPUT].forEach(function (name) {
  print(`  ${DB_NAME}.${name}: [${listIndexNames(name).join(", ")}]`);
});

// Fail loudly if anything other than the default _id index is present.
let ok = true;
[INPUT, OUTPUT].forEach(function (name) {
  const names = listIndexNames(name);
  if (names.length !== 1 || names[0] !== "_id_") {
    ok = false;
    print(`ERROR: ${DB_NAME}.${name} has unexpected indexes: [${names.join(", ")}]`);
  }
});

if (!ok) {
  throw new Error("Index layout verification failed: only the _id index is expected.");
}

print("");
print("OK: bmt_db + calc_input + calc_output ready with _id indexes only.");
