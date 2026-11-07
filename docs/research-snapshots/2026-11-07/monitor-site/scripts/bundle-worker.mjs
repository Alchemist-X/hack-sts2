import assert from 'node:assert/strict';
import {readFile, writeFile} from 'node:fs/promises';

const sources = new URL('../worker/', import.meta.url);
const destination = process.argv[2];
assert(destination, 'pass output worker path');
const [page, api, archive, index] = await Promise.all(
  ['dashboard-page.js', 'research-api.js', 'research-archive.js', 'index.js']
    .map(name => readFile(new URL(name, sources), 'utf8')),
);
const pageExport = 'export const DASHBOARD_PAGE =';
const apiExport = 'export { handleResearch, validLivePayload };';
const archiveExport = 'export const RESEARCH_ARCHIVE =';
const imports = [
  "import { DASHBOARD_PAGE } from './dashboard-page.js';",
  "import { handleResearch } from './research-api.js';",
  "import { RESEARCH_ARCHIVE } from './research-archive.js';",
].join('\n') + '\n';
assert(page.startsWith(pageExport), 'dashboard export contract');
assert(api.trimEnd().endsWith(apiExport), 'research API export contract');
assert(archive.startsWith(archiveExport), 'generated research archive missing');
assert(index.startsWith(imports), 'worker source import contract');
const combined = [
  page.replace(pageExport, 'const DASHBOARD_PAGE ='),
  api.trimEnd().slice(0, -apiExport.length),
  archive.replace(archiveExport, 'const RESEARCH_ARCHIVE ='),
  index.slice(imports.length),
].join('\n');
await writeFile(destination, combined);
