import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

const file = process.argv[2] || new URL('../dist/server/index.js', import.meta.url);
const source = await readFile(file);
const {default: worker} = await import('data:text/javascript;base64,' + source.toString('base64'));
const key = 'research/v1/live/latest.json';
const legacyKey = 'snapshots/latest.json';
const bucket = new Map();
let nextETag = 1;
const env = {BUCKET: {
  async get(name) {
    const entry = bucket.get(name);
    return entry ? {...entry, json: async () => JSON.parse(entry.body)} : null;
  },
  async put(name, body, options) {
    const old = bucket.get(name);
    const condition = options?.onlyIf;
    if (condition?.etagMatches && old?.etag !== condition.etagMatches) return null;
    if (condition instanceof Headers && condition.get('If-None-Match') === '*' && old) return null;
    const entry = {body, etag: String(nextETag++)};
    bucket.set(name, entry);
    return entry;
  },
  async delete(name) { bucket.delete(name); },
}};
const request = (path, init) => worker.fetch(new Request('https://local.invalid' + path, init), env);
const post = (payload, headers) => request('/api/research/live', {
  method: 'POST', headers, body: typeof payload === 'string' ? payload : JSON.stringify(payload),
});

const dashboard = await request('/');
const legacy = await request('/legacy');
assert.equal(dashboard.status, 200);
assert.equal(legacy.status, 200);
assert.notEqual(await dashboard.text(), await legacy.text());
assert.match(dashboard.headers.get('content-security-policy'), /frame-ancestors 'none'/);

const initial = await (await request('/api/research')).json();
assert.deepEqual(Object.keys(initial).sort(), ['architecture', 'budget', 'generated_at', 'history', 'live', 'performance', 'schema_version']);
assert.equal(initial.schema_version, 1);
assert.equal(initial.live.runs.length, 2);
assert(initial.live.runs.every(run => run.checkpoint_evidence === 'writer_metadata'));
assert.equal(initial.live.stale_after_seconds, 900);
assert(!JSON.stringify(initial).match(/(?:\/Users\/|\/data\/raven\/|\/var\/lib\/|Bearer [A-Za-z0-9._-]{12,})/));

const initialVersion = Date.parse(initial.live.observed_at);
const actualNow = Date.now;
Date.now = () => initialVersion + 920_000;
try {
  const aged = await (await request('/api/research')).json();
  assert.equal(aged.live.stale, true);
  assert(aged.live.runs.every(run => run.stale));
  const runs = aged.live.runs.map(({stale, ...run}) => run);
  const original = {schema_version: 1, version: initialVersion, observed_at: aged.live.observed_at, runs};
  assert.equal((await post(original)).status, 200, 'semantic retry of the embedded value');
  assert.equal((await post(original, {'oai-authenticated-user-id': 'visitor-fixture'})).status, 403);
  assert.equal((await post(' '.repeat(16 * 1024 + 1))).status, 413);
  assert.equal((await post('{}', {'Content-Length': String(16 * 1024 + 1)})).status, 413);
  assert.equal((await post('{')).status, 400);
  assert.equal((await post({...original, arbitrary: '/data/secret'})).status, 422);
  const forged = structuredClone(original);
  forged.runs[0].progress_health.token = 'unexpected';
  assert.equal((await post(forged)).status, 422);
  const nonfinite = JSON.stringify(original).replace(/"training_progress":[0-9.]+/, '"training_progress":1e999');
  assert.equal((await post(nonfinite)).status, 422);
  assert.equal((await post({...original, observed_at: '2026-02-30T10:00:00.000Z'})).status, 422);

  const newer = structuredClone(original);
  const advance = (payload, by) => {
    payload.version += by;
    payload.observed_at = new Date(payload.version).toISOString();
    payload.runs[1].observed_at = payload.observed_at;
  };
  advance(newer, 910_000);
  assert.equal((await post(newer)).status, 200);
  assert.equal((await post(original)).status, 409);
  assert.equal((await post(newer)).status, 200);
  const readback = await (await request('/api/research')).json();
  assert.equal(readback.live.runs[1].observed_at, newer.observed_at);
  assert.equal(readback.live.runs[1].stale, false);
  assert.equal(readback.live.runs[0].stale, true, 'fresh run must not hide stale peer');

  const concurrentA = structuredClone(newer);
  const concurrentB = structuredClone(newer);
  advance(concurrentA, 1);
  advance(concurrentB, 2);
  const results = await Promise.all([post(concurrentA), post(concurrentB)]);
  assert.deepEqual(results.map(response => response.status).sort(), [200, 409]);
  assert.equal(bucket.has(key), true);

  const future = structuredClone(newer);
  advance(future, 400_000);
  assert.equal((await post(future)).status, 422);
  assert.equal(bucket.has(legacyKey), false, 'live writes use only their own namespace');

  const legacyArm = {stage: 'plan', decisions: 0, target: null, points: [], evaluations: []};
  const stream = {arms: {S: legacyArm, M: legacyArm, L: legacyArm}, status: 'pending', current_task: 'fixture'};
  const stamp = new Date(initialVersion).toISOString().replace(/\.\d{3}Z$/, 'Z');
  const oldSnapshot = {
    schema_version: 1, version: 1, generated_at: stamp, data_at: stamp,
    update_interval_seconds: 300, poll_interval_seconds: 45000,
    study: 'SILENT · A10 · 从零训练', phase_order: ['plan', 'code', 'deployed', 'real'],
    streams: {formal: stream, engineering: stream},
    budget: {approved_usd: null, actual_usd: null, actual_status: 'unknown', platform_gross_observed_usd: null, reserved_usd: null},
    notice: 'fixture',
  };
  const legacyPost = body => request('/api/snapshot', {method: 'POST', body: JSON.stringify(body)});
  assert.equal((await legacyPost(oldSnapshot)).status, 200);
  assert.equal(await (await request('/api/snapshot')).text(), JSON.stringify(oldSnapshot));
  assert.equal((await legacyPost({...oldSnapshot, injected: true})).status, 422);
  assert.equal((await request('/api/snapshot', {method: 'POST', headers: {'oai-authenticated-user-id': 'fixture'}})).status, 403);
  assert.equal(bucket.has('snapshots/history/000000000001.json'), true);
  assert.equal(bucket.has(key), true);
} finally {
  Date.now = actualNow;
}
console.log('Research API passed stale, size, finite, version, visitor, CAS and legacy checks');
