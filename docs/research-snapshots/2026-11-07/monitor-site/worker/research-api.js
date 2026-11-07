const RESEARCH_LIVE_KEY = 'research/v1/live/latest.json';
const LIVE_MAX_BYTES = 16 * 1024;
const LIVE_STALE_SECONDS = 900;
const LIVE_MAX_SKEW_MS = 300000;
const LIVE_IDENTITIES = {
  'M116': {label: 'M116', target: 100007936, baseline: 0, deadline: '2026-11-07T07:02:23.000Z'},
  'S115-S2': {label: 'S115 · S2', target: 695861248, baseline: 595853312, deadline: '2026-11-07T03:56:08.000Z'},
};

function exactRecord(value, keys) {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
}

function isoMillis(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$/.test(value)) return NaN;
  const time = Date.parse(value);
  return Number.isFinite(time) && new Date(time).toISOString() === value ? time : NaN;
}

function nullableCount(value) {
  return value === null || (Number.isSafeInteger(value) && value >= 0);
}

function validLiveRun(run, now) {
  const keys = [
    'id', 'label', 'status', 'observed_at', 'started_at', 'deadline_at', 'decisions',
    'target_decisions', 'new_decisions', 'updates', 'checkpoint_evidence', 'resources', 'progress_health',
  ];
  if (!exactRecord(run, keys) || !Object.hasOwn(LIVE_IDENTITIES, run.id)) return false;
  const identity = LIVE_IDENTITIES[run.id];
  const observed = isoMillis(run.observed_at);
  if (!Number.isFinite(observed) || observed > now + LIVE_MAX_SKEW_MS
      || run.label !== identity.label || run.target_decisions !== identity.target
      || run.deadline_at !== identity.deadline
      || !['running', 'terminal', 'unknown'].includes(run.status)
      || !['none', 'writer_metadata', 'body_verified'].includes(run.checkpoint_evidence)) return false;
  if (run.started_at !== null && !(isoMillis(run.started_at) <= observed)) return false;
  if (![run.decisions, run.new_decisions, run.updates].every(nullableCount)) return false;
  if (run.decisions === null) {
    if (run.new_decisions !== null || run.updates !== null || run.checkpoint_evidence !== 'none') return false;
  } else if (run.decisions <= identity.baseline || run.decisions > identity.target
      || run.updates * 131072 !== run.decisions || run.new_decisions !== run.decisions - identity.baseline
      || run.checkpoint_evidence === 'none') return false;
  if (run.checkpoint_evidence === 'body_verified' && run.status !== 'terminal') return false;
  const resources = run.resources;
  if (!exactRecord(resources, ['gpu', 'cpu', 'memory_gib'])
      || resources.gpu !== 'H100!' || resources.cpu !== 32 || resources.memory_gib !== 64) return false;
  const health = run.progress_health;
  if (!exactRecord(health, ['observed_at', 'training_progress', 'episodes', 'table_overflow_count'])) return false;
  if (health.observed_at === null) {
    return health.training_progress === null && health.episodes === null && health.table_overflow_count === null;
  }
  return isoMillis(health.observed_at) <= observed
    && (health.training_progress === null || (Number.isFinite(health.training_progress)
      && health.training_progress >= 0 && health.training_progress <= 1))
    && nullableCount(health.episodes) && nullableCount(health.table_overflow_count);
}

function validLivePayload(payload, now) {
  if (!exactRecord(payload, ['schema_version', 'version', 'observed_at', 'runs'])
      || payload.schema_version !== 1 || !Number.isSafeInteger(payload.version)
      || !Array.isArray(payload.runs) || payload.runs.length < 1 || payload.runs.length > 2
      || !payload.runs.every(run => validLiveRun(run, now))) return false;
  const newest = Math.max(...payload.runs.map(run => isoMillis(run.observed_at)));
  return payload.version === newest && isoMillis(payload.observed_at) === newest
    && new Set(payload.runs.map(run => run.id)).size === payload.runs.length;
}

function liveIsStale(timestamp, now) {
  return !timestamp || now - isoMillis(timestamp) > LIVE_STALE_SECONDS * 1000;
}

function projectResearchLive(payload, now) {
  const observedAt = payload?.observed_at ?? null;
  const runs = (payload?.runs ?? []).map(run => ({
    ...run, stale: liveIsStale(run.observed_at, now),
  }));
  return {
    schema_version: 1,
    observed_at: observedAt,
    stale: runs.length === 0 || runs.some(run => run.stale),
    stale_after_seconds: LIVE_STALE_SECONDS,
    runs,
  };
}

async function readLiveBody(request) {
  if (Number(request.headers.get('content-length')) > LIVE_MAX_BYTES) return null;
  if (!request.body) return new Uint8Array();
  const reader = request.body.getReader();
  const bytes = new Uint8Array(LIVE_MAX_BYTES);
  let length = 0;
  try {
    while (true) {
      const {done, value} = await reader.read();
      if (done) return bytes.subarray(0, length);
      if (length + value.byteLength > LIVE_MAX_BYTES) {
        await reader.cancel();
        return null;
      }
      bytes.set(value, length);
      length += value.byteLength;
    }
  } finally {
    reader.releaseLock();
  }
}

function serializeLive(value) {
  return JSON.stringify(value, (_, item) => item && typeof item === 'object' && !Array.isArray(item)
    ? Object.fromEntries(Object.keys(item).sort().map(key => [key, item[key]])) : item);
}

function liveProgresses(previous, incoming) {
  if (incoming.version <= previous.version) return false;
  return previous.runs.every(before => {
    const after = incoming.runs.find(run => run.id === before.id);
    return after && isoMillis(after.observed_at) >= isoMillis(before.observed_at)
      && (before.decisions === null || (after.decisions !== null && after.decisions >= before.decisions))
      && (before.status !== 'terminal' || after.status === 'terminal')
      && (before.checkpoint_evidence !== 'body_verified' || after.checkpoint_evidence === 'body_verified');
  });
}

async function handleResearch(request, env, path, archive, jsonResponse) {
  const now = Date.now();
  if (path === '/api/research' && request.method === 'GET') {
    let live = archive.initial_live;
    const object = await env.BUCKET.get(RESEARCH_LIVE_KEY);
    if (object) {
      let stored;
      try { stored = await object.json(); } catch { stored = null; }
      if (validLivePayload(stored, now) && (!live || liveProgresses(live, stored))) live = stored;
    }
    return jsonResponse({
      schema_version: 1,
      generated_at: archive.generated_at,
      history: archive.history,
      architecture: archive.architecture,
      performance: archive.performance,
      budget: archive.budget,
      live: projectResearchLive(live, now),
    });
  }
  if (path !== '/api/research/live' || request.method !== 'POST') return null;
  // The owner-private Sites dispatcher validates service access before forwarding.
  if (request.headers.has('oai-authenticated-user-id')) return jsonResponse({error: 'service_access_required'}, 403);
  const bytes = await readLiveBody(request);
  if (bytes === null) return jsonResponse({error: 'too_large'}, 413);
  let incoming;
  try {
    incoming = JSON.parse(new TextDecoder('utf-8', {fatal: true}).decode(bytes));
  } catch {
    return jsonResponse({error: 'invalid_json'}, 400);
  }
  if (!validLivePayload(incoming, now)) return jsonResponse({error: 'invalid_research_live'}, 422);
  const object = await env.BUCKET.get(RESEARCH_LIVE_KEY);
  let old = archive.initial_live;
  if (object) {
    let stored;
    try { stored = await object.json(); } catch { return jsonResponse({error: 'stored_research_live_invalid'}, 503); }
    if (!validLivePayload(stored, now)) return jsonResponse({error: 'stored_research_live_invalid'}, 503);
    if (!old || stored.version >= old.version) old = stored;
  }
  const body = serializeLive(incoming);
  if (old) {
    if (incoming.version === old.version && body === serializeLive(old)) {
      return jsonResponse({ok: true, version: incoming.version});
    }
    if (!liveProgresses(old, incoming)) return jsonResponse({error: 'stale_research_live'}, 409);
  }
  const put = await env.BUCKET.put(RESEARCH_LIVE_KEY, body, {
    httpMetadata: {contentType: 'application/json'},
    onlyIf: object?.etag ? {etagMatches: object.etag} : new Headers({'If-None-Match': '*'}),
  });
  if (!put) return jsonResponse({error: 'research_live_conflict'}, 409);
  return jsonResponse({ok: true, version: incoming.version});
}
export { handleResearch, validLivePayload };
