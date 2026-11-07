import http from 'node:http';
import { readFile } from 'node:fs/promises';
import worker from '../worker/index.js';
const port = Number(process.env.STS2_MONITOR_PREVIEW_PORT ?? 8765);
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('Invalid preview port');
const origin = 'http://127.0.0.1:' + port;
const snapshot = await readFile(new URL('../snapshot.json', import.meta.url), 'utf8');
const env = {BUCKET: {get: async key => key === 'snapshots/latest.json' ? {body: snapshot} : null}};
const server = http.createServer(async (req, res) => {
  try {
    const request = new Request(origin + req.url, {method: req.method, headers: req.headers});
    const response = await worker.fetch(request, env);
    res.writeHead(response.status, Object.fromEntries(response.headers));
    res.end(Buffer.from(await response.arrayBuffer()));
  } catch {res.writeHead(500);res.end('Preview error')}
});
server.listen(port, '127.0.0.1', () => console.log(origin));
