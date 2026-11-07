export const DASHBOARD_PAGE = String.raw`<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="theme-color" content="#172124">
<meta name="description" content="STS2 Silent A10 自主训练实验档案、模型演变、吞吐和费用。">
<title>STS2 研究监控台 · 实验工作台</title>
<style>
:root {
  color-scheme: light; --ink:#172124; --muted:#536360; --paper:#f6f4ed; --card:#fffdf7;
  --line:#d9ded6; --mint:#286a5b; --soft:#e5efe7; --amber:#86511f; --warm:#f5ead8;
  --red:#923d34; --blue:#315e80;
  font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","PingFang SC","Microsoft YaHei",sans-serif;
  color:var(--ink); background:var(--paper); font-size:15px;
}
* {box-sizing:border-box}
[hidden] {display:none !important}
body {margin:0;line-height:1.55}
button,input,select {font:inherit;color:inherit}
button {cursor:pointer}
button,a,input,select,summary,[tabindex] {outline-offset:4px}
:focus-visible {outline:2px solid #1a7764}
a {color:var(--mint);text-underline-offset:3px}
h1,h2,h3,p,dl,dd,figure {margin:0}
h1,h2 {font-family:Georgia,"Songti SC",serif;font-weight:500}
h1 {font-size:clamp(29px,4.5vw,46px);letter-spacing:-.05em;line-height:1.17}
h2 {font-size:clamp(25px,3vw,33px);letter-spacing:-.025em;line-height:1.25}
h3 {font-size:16px;line-height:1.4}
p,dd,li {overflow-wrap:anywhere}
small,.muted {color:var(--muted)}
.mono,.num,time {font-variant-numeric:tabular-nums}
.mono {font-family:ui-monospace,Menlo,monospace;overflow-wrap:anywhere}
.skip {position:absolute;inset:8px auto auto 8px;transform:translateY(-150%);z-index:5;padding:9px;background:var(--card)}
.skip:focus {transform:none}
.shell {width:min(1280px,calc(100% - 48px));margin:auto;min-width:0}
.mast {background:var(--ink);color:#f8f6eb}
.mast .shell {display:flex;align-items:center;justify-content:space-between;gap:14px;padding:17px 0}
.brand {display:flex;align-items:center;gap:12px}
.brandmark {width:37px;height:37px;display:grid;place-items:center;border:1px solid #89ad9e;color:#c2ddcd;font:24px Georgia,serif}
.brand strong {display:block;font-size:16px;letter-spacing:.05em}
.brand small {display:block;font-size:10px;letter-spacing:.15em;color:#c2d0c9}
.mast a {color:#d0e4d8;font-size:13px}
.hero {padding:24px 0 15px}
.kicker {color:var(--mint);font-size:11px;font-weight:800;letter-spacing:.16em;text-transform:uppercase;margin-bottom:7px}
.hero .lead {color:var(--muted);margin-top:11px;max-width:64ch}
nav {border-bottom:1px solid var(--line);display:flex;flex-wrap:wrap;gap:4px 19px;margin-bottom:17px}
nav a {padding:10px 3px;color:var(--muted);font-size:14px;font-weight:650;text-decoration:none;border-bottom:2px solid transparent}
nav a[aria-current="page"] {color:var(--mint);border-color:var(--mint)}
.notice {padding:9px 13px;margin:0 0 16px;border:1px solid var(--line);background:var(--soft);border-radius:7px;font-size:13px}
.notice.warn {background:var(--warm);color:var(--amber)}
.section-head {display:flex;justify-content:space-between;align-items:flex-start;gap:16px;flex-wrap:wrap;margin:0 0 14px}
.section-head p {margin-top:5px;color:var(--muted);max-width:78ch}
.grid {display:grid;gap:13px;min-width:0}
.grid > * {min-width:0}
.two {grid-template-columns:repeat(2,minmax(0,1fr))}
.three {grid-template-columns:repeat(3,minmax(0,1fr))}
.four {grid-template-columns:repeat(4,minmax(0,1fr))}
.card {border:1px solid var(--line);border-radius:10px;background:var(--card);padding:18px;box-shadow:0 2px 4px #17212408;min-width:0}
.card p {margin-top:7px}
.card .label {display:block;color:var(--muted);font-size:12px;font-weight:720;margin-bottom:7px}
.value {font:clamp(23px,3vw,34px) Georgia,"Songti SC",serif;overflow-wrap:anywhere}
.pill {display:inline-block;font-size:11px;line-height:1.4;font-weight:720;background:var(--soft);color:var(--mint);border-radius:100px;padding:4px 8px;overflow-wrap:anywhere}
.pill.warn {background:var(--warm);color:var(--amber)}
.pill.fail {background:#f5e5e1;color:var(--red)}
.bar {height:6px;background:#e4e8df;border-radius:4px;overflow:hidden;margin:9px 0 6px}
.bar span {display:block;height:100%;background:var(--mint)}
.stack > * + * {margin-top:15px}
.split {display:flex;align-items:flex-start;justify-content:space-between;gap:10px;flex-wrap:wrap}
.note {color:var(--muted);font-size:13px}
.empty {border:1px dashed #b6c1b9;border-radius:8px;padding:20px;color:var(--muted);text-align:center}
button.plain,select,input {border:1px solid #aab9b1;border-radius:6px;background:var(--card);padding:7px 10px;max-width:100%}
button.plain:hover {background:var(--soft)}
.control {display:flex;flex-wrap:wrap;align-items:end;gap:10px}
.control label {display:grid;gap:3px;font-size:12px;color:var(--muted);font-weight:650;min-width:0}
.control input {width:min(100%,270px);min-width:0}
.control input,.control select {font-size:14px;color:var(--ink)}
.archive {display:grid;grid-template-columns:minmax(230px,1fr) minmax(0,2.6fr);gap:14px;align-items:start}
.archive-list {max-height:660px;overflow:auto;margin:0;padding:4px;list-style:none}
.archive-list button {width:100%;text-align:left;padding:9px 10px;border:0;border-radius:6px;background:none;color:var(--ink);overflow-wrap:anywhere}
.archive-list button:hover,.archive-list button[aria-current="true"] {background:var(--soft)}
.archive-list button[aria-current="true"] {box-shadow:inset 3px 0 var(--mint)}
.archive-list small {display:block;margin-top:2px}
.archive-list .category {padding:10px 10px 3px;color:var(--muted);font-size:12px;font-weight:800}
.detail-list {display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:9px 14px}
.detail-list > div {min-width:0}
.detail-list dt {color:var(--muted);font-size:12px}
.detail-list dd {margin-top:2px;font-size:14px}
ul.compact {margin:6px 0 0;padding-left:19px}
.compact li {margin-bottom:4px}
details {margin-top:8px;border-top:1px solid var(--line);padding-top:8px}
summary {cursor:pointer;color:var(--mint);font-weight:650}
.table-wrap {max-width:100%;overflow:auto}
table {border-collapse:collapse;width:100%;font-size:13px}
caption {text-align:left;font-weight:700;margin-bottom:5px}
th,td {padding:8px 9px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top;overflow-wrap:anywhere}
th {color:var(--muted);font-size:12px}
td:first-child,th:first-child {padding-left:0}
td.num,th.num {text-align:right;white-space:nowrap}
.chart {display:block;width:100%;height:auto;margin-top:9px}
.chart text {font-family:inherit;font-size:11px;fill:var(--muted)}
.chart .mark {cursor:default;outline:none}
.chart .mark:hover .pin,.chart .mark:focus .pin {stroke:var(--ink);stroke-width:2.5px}
.chart .mark:focus .ring {stroke:#1a7764;stroke-width:1.5px;fill:none}
.readout {min-height:2.5em;font-size:13px;color:var(--muted);border-top:1px solid var(--line);padding-top:8px}
.flow-wrap {width:min(100%,600px);margin:0 auto}
.flow {display:block;width:100%;height:auto}
.flow .node {cursor:pointer;outline:none}
.flow .node rect {fill:#eef3ec;stroke:#adc1b6;stroke-width:1.5}
.flow .node:hover rect,.flow .node:focus rect,.flow .node[aria-pressed="true"] rect {stroke:var(--mint);stroke-width:3;fill:#e1efe5}
.flow text {font:600 13px -apple-system,BlinkMacSystemFont,sans-serif;fill:var(--ink);pointer-events:none}
.flow .wire {fill:none;stroke:#648578;stroke-width:1.7}
.versions {display:flex;flex-wrap:wrap;gap:7px;margin:8px 0 0}
.versions button[aria-pressed="true"] {background:var(--mint);color:white;border-color:var(--mint)}
.legend {display:flex;flex-wrap:wrap;gap:6px 16px;color:var(--muted);font-size:12px;margin:6px 0 0}
.legend span::before {content:"";display:inline-block;width:7px;height:7px;border-radius:100%;background:var(--legend);margin-right:5px}
footer {border-top:1px solid var(--line);color:var(--muted);font-size:12px;padding:15px 0 25px;margin-top:25px}
@media(max-width:900px) {.four{grid-template-columns:repeat(2,minmax(0,1fr))}.archive{grid-template-columns:minmax(180px,1fr) minmax(0,2fr)}}
@media(max-width:650px) {
  .shell{width:calc(100% - 28px)} .mast .shell{padding:12px 0} .hero{padding:20px 0 13px}
  nav{gap:1px 12px} nav a{font-size:13px} .card{padding:14px} .two,.three,.archive{grid-template-columns:1fr}
  .archive-list{max-height:250px} .section-head{gap:8px}
}
@media(max-width:365px) {.four,.detail-list{grid-template-columns:1fr}}
</style>
</head>
<body>
<a href="#main" class="skip">跳至内容</a>
<header class="mast"><div class="shell">
  <div class="brand"><div class="brandmark" aria-hidden="true">S</div><div><strong>STS2 研究监控台</strong><small>MODAL · SILENT / A10</small></div></div>
  <a href="/legacy">旧版监控</a>
</div></header>
<main id="main" class="shell" tabindex="-1">
  <div class="hero">
    <div class="kicker">Research operations / 2026</div>
    <h1>自主训练实验工作台</h1>
    <p class="lead">从随机初始化起步，追踪 Silent A10 的训练剂量、正式开发集结果与实验开销。</p>
  </div>
  <nav aria-label="研究工作台">
    <a href="#overview" data-tab="overview" aria-current="page">概览</a>
    <a href="#experiments" data-tab="experiments">实验档案</a>
    <a href="#architecture" data-tab="architecture">架构演变</a>
    <a href="#performance" data-tab="performance">训练吞吐</a>
    <a href="#budget" data-tab="budget">费用</a>
  </nav>
  <div id="connection" class="notice" role="status">正在读取研究档案…</div>
  <section id="overview" aria-labelledby="overview-title" class="stack">
    <div class="section-head"><div><h2 id="overview-title">运行与正式评测</h2><p>运行卡片报告训练新决策数。正式结果来自独立评测局。</p></div></div>
    <div id="run-cards" class="grid two"><div class="empty">等待真实运行快照。</div></div>
    <div id="goal-cards" class="grid four"></div>
    <figure class="card">
      <div class="split"><div><h3>正式开发集表现</h3><p class="note">横轴为模型累计新决策。每个点保留自己的评测分母与协议。</p></div>
      <div class="control"><label>实验 <select id="quality-arm"><option value="">全部实验</option></select></label>
      <label>纵轴 <select id="quality-metric"><option value="progress">平均进度</option><option value="win">胜率</option></select></label></div></div>
      <div id="quality-chart"></div><div id="quality-legend" class="legend"></div>
      <figcaption id="quality-readout" class="readout"></figcaption>
      <details id="quality-details" hidden><summary id="quality-detail-label">观测点</summary><div id="quality-table"></div></details>
    </figure>
  </section>
  <section id="experiments" aria-labelledby="experiments-title" hidden>
    <div class="section-head"><div><h2 id="experiments-title">实验档案</h2><p id="archive-meta">查看假设、剂量、谱系、评测和失败恢复。</p></div></div>
    <div class="archive">
      <div class="card stack">
        <div class="control">
          <label for="archive-search">搜索实验<input type="search" id="archive-search" placeholder="任务号、模型或关键词"></label>
          <label for="archive-kind">类型<select id="archive-kind"><option value="">全部类型</option></select></label>
          <label for="archive-status">状态<select id="archive-status"><option value="">全部状态</option></select></label>
        </div>
        <div id="archive-count" class="note" role="status"></div>
        <ul id="archive-list" class="archive-list" aria-label="实验列表"></ul>
      </div>
      <div id="archive-detail" class="card stack"><div class="empty">等待档案加载。</div></div>
    </div>
  </section>
  <section id="architecture" aria-labelledby="architecture-title" class="stack" hidden>
    <div class="section-head"><div><h2 id="architecture-title">架构演变</h2><p>对照不同模型宽度、参数量与数据路由。选择图中的模块查看说明。</p></div></div>
    <div id="architecture-content" class="stack"><div class="empty">等待架构证据。</div></div>
  </section>
  <section id="performance" aria-labelledby="performance-title" class="stack" hidden>
    <div class="section-head"><div><h2 id="performance-title">训练吞吐</h2><p>按实验追踪更新与里程碑。吞吐和训练进度用于诊断执行过程。</p></div></div>
    <div id="performance-content" class="stack"><div class="empty">等待吞吐档案。</div></div>
  </section>
  <section id="budget" aria-labelledby="budget-title" class="stack" hidden>
    <div class="section-head"><div><h2 id="budget-title">费用与归属</h2><p>账单、应用计量、估算及预留分别列示。展开应用或实验可复核对应口径。</p></div></div>
    <div id="budget-content" class="stack"><div class="empty">等待账单与实验归属数据。</div></div>
  </section>
  <footer class="split"><span>STS2 · 研究工作台 · 只读</span><span id="footer-meta">等待数据</span></footer>
</main>
<script>
(function () {
'use strict';
const $ = (id) => document.getElementById(id);
const arr = (value) => Array.isArray(value) ? value : [];
const isNum = (value) => typeof value === 'number' && Number.isFinite(value);
const fmt = (value) => isNum(value) ? value.toLocaleString('zh-CN') : '未知';
const dec = (value, digits=4) => isNum(value) ? value.toFixed(digits) : '未知';
const pct = (value) => isNum(value) ? (value * 100).toFixed(2) + '%' : '未知';
const short = (value) => isNum(value) ? (value >= 1e6 ? (value / 1e6).toFixed(2).replace(/\.?0+$/, '') + 'M' : fmt(value)) : '未知';
function usd(value) {
  const amount = typeof value === 'string' && /^-?\d+(\.\d+)?(?:e[+-]?\d+)?$/i.test(value) ? Number(value) : value;
  return isNum(amount) ? '$' + (amount || 0).toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:8}) : '未知';
}
const cents = (value) => usd(isNum(value) ? value / 100 : null);
const esc = (value) => String(value ?? '').replace(/[&<>"']/g, (char) => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
const text = (value) => esc(value ?? '未知');
const iso = (value) => typeof value === 'string' && Number.isFinite(Date.parse(value)) ? new Date(value).toISOString().slice(0,19).replace('T',' ') + ' UTC' : '未知';
const relative = (value) => {
  if (!value || !Number.isFinite(Date.parse(value))) return '时间未知';
  const mins = Math.max(0, Math.floor((Date.now() - Date.parse(value))/60000));
  return mins < 1 ? '不到 1 分钟前' : mins < 60 ? mins + ' 分钟前' : mins < 1440 ? Math.floor(mins/60) + ' 小时前' : Math.floor(mins/1440) + ' 天前';
};
const empty = (message) => '<div class="empty">' + esc(message) + '</div>';
const pill = (message, type='') => '<span class="pill ' + type + '">' + esc(message) + '</span>';
const metric = (label,value,note='') => '<div class="card"><span class="label">' + esc(label) + '</span><div class="value num">' + esc(value) + '</div>' + (note ? '<p class="note">' + esc(note) + '</p>' : '') + '</div>';
const fields = (pairs) => '<dl class="detail-list">' + pairs.map(([label,value]) => '<div><dt>' + esc(label) + '</dt><dd>' + value + '</dd></div>').join('') + '</dl>';
const section = (title,html) => html ? '<section><h3>' + esc(title) + '</h3>' + html + '</section>' : '';
const list = (values) => values.length ? '<ul class="compact">' + values.map((value) => '<li>' + esc(value) + '</li>').join('') + '</ul>' : '';
const table = (headers,rows,label) => rows.length ? '<div class="table-wrap" tabindex="0" role="region" aria-label="' + esc(label) + '"><table><thead><tr>' + headers.map((h) => '<th scope="col">' + esc(h) + '</th>').join('') + '</tr></thead><tbody>' + rows.map((row) => '<tr>' + row.map((cell) => '<td>' + cell + '</td>').join('') + '</tr>').join('') + '</tbody></table></div>' : empty('暂无可用记录。');
const statusNames = {running:'运行中',terminal:'已终止',completed:'已完成',failed:'失败',not_executed:'未执行',planned:'计划',unknown:'未知'};
const kindNames = {training:'训练',resource_probe:'资源探针',diagnostic:'诊断',formal_evaluation:'正式评测',engineering:'工程验证',training_attempt:'训练尝试',planned_experiment:'前瞻计划'};
const palettes = ['#286a5b','#315e80','#a34e44','#866125','#6f4d8a','#4a6950','#80514d','#4c6470'];
const evidenceNames = {none:'暂无检查点证据',writer_metadata:'写入元数据可见',body_verified:'检查点内容已验'};
let data = null;
let fetchedAt = null;
let connectionFailed = false;
let archiveRecords = [];
let archiveID = '';
let archID = '';
let archExperiment = '';
let flowID = 'observe';
let perfID = '';
let lastSize = 0;
function sentence(value) {
  if (typeof value === 'string' || isNum(value)) return String(value);
  if (Array.isArray(value)) return value.map(sentence).filter(Boolean).join('；');
  if (!value || typeof value !== 'object') return '';
  return [value.label ?? value.title ?? value.stage ?? value.event ?? value.type ?? value.kind,
    value.description ?? value.note ?? value.summary ?? value.detail ?? value.result ?? value.conclusion,
    value.issue, value.recovery, isNum(value.cumulative_decisions) ? fmt(value.cumulative_decisions) + ' 决策' : null,
    value.evaluation_id, value.at_utc ?? value.at].map(sentence).filter(Boolean).join(' · ');
}
function bullets(values) {
  if (typeof values === 'string') return list([values]);
  return list(arr(values).map(sentence).filter(Boolean));
}
function sourceRefs(values) {
  const refs = [...new Set(arr(values).map((source) => {
    if (typeof source === 'string') return shortRef(source);
    if (!source || typeof source !== 'object') return '';
    const ref = shortRef(source.source_id ?? source.ref ?? source.path ?? source.source ?? source.label ?? '');
    const hash = String(source.sha256 ?? source.hash ?? '').match(/^[a-f0-9]{8,64}$/i);
    return [ref, hash ? hash[0].slice(0,12) : ''].filter(Boolean).join(' · ');
  }).filter(Boolean))];
  return refs.length ? '<details><summary>证据引用（' + refs.length + '）</summary>' + list(refs) + '</details>' : '';
}
function shortRef(value) {
  if (typeof value !== 'string') return '';
  return value.replace(/\\/g,'/').split('/').filter(Boolean).slice(-2).join('/').slice(0,125);
}
function route() {
  const parts = location.hash.slice(1).split('/');
  const tabs = ['overview','experiments','architecture','performance','budget'];
  const active = tabs.includes(parts[0]) ? parts[0] : 'overview';
  if (active === 'experiments' && parts[1]) archiveID = parts[1];
  tabs.forEach((name) => {
    $(name).hidden = name !== active;
    const link = document.querySelector('[data-tab="' + name + '"]');
    if (name === active) link.setAttribute('aria-current','page'); else link.removeAttribute('aria-current');
  });
  if (active === 'overview') renderQuality();
  if (active === 'experiments') renderArchiveList();
  if (active === 'performance') renderPerformance();
}
function renderConnection() {
  const live = data?.live;
  const last = live?.observed_at;
  const old = !last || Date.now() - Date.parse(last) > 1000 * (live?.stale_after_seconds || 900);
  const stale = !live || live.stale || old;
  const connection = $('connection');
  connection.className = 'notice' + (stale || connectionFailed ? ' warn' : '');
  const status = !arr(live?.runs).length ? '实时采样暂未回传。' : stale ? '实时采样已超过 15 分钟。' : '已载入监控数据。各实验采样时刻见运行卡片。';
  const next = data ? (connectionFailed ? '连接暂不可用，保留上次成功画面。' : '') + status : connectionFailed ? '暂时无法读取研究数据。页面会自动重试。' : '正在读取研究档案…';
  if (connection.textContent !== next) connection.textContent = next;
  $('footer-meta').textContent = data ? '档案生成 ' + iso(data.generated_at) + ' · 页面获取 ' + iso(fetchedAt) : '等待数据';
}
function liveCard(run) {
  const actual = run.decisions;
  const target = run.target_decisions;
  const segment = run.new_decisions;
  const segmentTarget = [actual,target,segment].every(isNum) ? target - (actual - segment) : null;
  const share = segmentTarget > 0 ? Math.min(100,Math.max(0,100 * segment / segmentTarget)) : null;
  const gpu = typeof run.resources?.gpu === 'string' ? '1 × ' + run.resources.gpu.replace(/!$/,'') : 'GPU 未知';
  const stale = run.stale || !run.observed_at || Date.now() - Date.parse(run.observed_at) > 1000 * (data?.live?.stale_after_seconds || 900);
  return '<article class="card stack"><div class="split"><h3>' + text(run.label ?? run.id) + '</h3>' + pill((stale ? '上次观测 · ' : '') + (statusNames[run.status] || run.status || '未知'),run.status==='running' && !stale?'':'warn') +
    '</div><div><div class="value num">' + fmt(actual) + '</div><p class="note">检查点累计决策 · 目标 ' + fmt(target) + '</p>' +
    (share === null ? '' : '<div class="bar" role="progressbar" aria-label="本段训练剂量" aria-valuemin="0" aria-valuemax="100" aria-valuenow="' + share.toFixed(1) + '"><span style="width:' + share + '%"></span></div>') +
    '<p class="note">本段 ' + fmt(segment) + ' / ' + fmt(segmentTarget) + ' 决策' + (share===null?'':' · '+share.toFixed(1)+'%') + '</p></div>' +
    fields([['检查点更新',fmt(run.updates)],['检查点证据',text(evidenceNames[run.checkpoint_evidence])],['申请资源',text(gpu) + ' · ' + fmt(run.resources?.cpu) + ' vCPU · ' + fmt(run.resources?.memory_gib) + ' GiB'],
      ['训练进度（诊断）',dec(run.progress_health?.training_progress)],['训练器状态时刻',iso(run.progress_health?.observed_at)],
      ['观察时刻',iso(run.observed_at) + ' · ' + relative(run.observed_at)],['提交时间',iso(run.started_at)]]) + '</article>';
}
function historyRecords() {
  const history = data?.history;
  return [['实验',history?.experiments],['工程验证',history?.engineering],['计划',history?.planned]].flatMap(([group,records]) =>
    [...arr(records)].sort((a,b) => (b.display_order ?? 0)-(a.display_order ?? 0)).filter((record) => typeof (record.experiment_id ?? record.id) === 'string').map((record) =>
      ({...record,id:record.experiment_id ?? record.id,group,kind:record.kind || (group==='工程验证'?'engineering':'training')})));
}
function formalPoints() {
  const records = new Map(archiveRecords.filter((r) => r.group === '实验' && r.kind === 'training').map((r) => [r.id,r]));
  return arr(data?.history?.formal_evaluations).flatMap((p) => {
    const record = records.get(p.experiment_id);
    if (!record || !isNum(p.mean_progress) || !isNum(p.cumulative_decisions) || !isNum(p.n) || p.n <= 0 ||
      p.descriptive_only || p.diagnostic_only || p.protocol?.role !== 'reused_dev_not_blind' ||
      p.protocol?.search !== 'off' || p.protocol?.breaker !== 'off') return [];
    return [{...p,record}];
  });
}
function qualityLabel(p) {
  return p.record.title + ' · ' + fmt(p.cumulative_decisions) + ' 决策 · 进度 ' + dec(p.mean_progress) +
    ' · ' + fmt(p.wins) + '/' + fmt(p.n) + ' 胜' +
    (p.protocol?.seed_set_id ? ' · ' + p.protocol.seed_set_id : '');
}
function renderOverview() {
  const live = arr(data?.live?.runs);
  $('run-cards').innerHTML = live.length ? live.map(liveCard).join('') : empty('实时采样暂未回传 S115 / M116。');
  const points = formalPoints();
  const best = [...points].sort((a,b) => b.mean_progress-a.mean_progress)[0];
  const bestWin = points.filter((p) => isNum(p.wins)).sort((a,b) => b.wins/b.n-a.wins/a.n)[0];
  $('goal-cards').innerHTML = metric('研究目标 · 正式进度','0.6+') + metric('研究目标 · 正式胜率','10%+') +
    metric('当前最佳正式进度',dec(best?.mean_progress,6),best ? best.record.title + ' · ' + fmt(best.n) + ' 局' : '等待正式结果') +
    metric('当前最佳正式胜率',pct(bestWin ? bestWin.wins / bestWin.n : null),bestWin ? fmt(bestWin.wins) + ' / ' + fmt(bestWin.n) + ' 局' : '等待正式结果');
  const selector = $('quality-arm');
  const currentArm = selector.value;
  const arms = [...new Map(points.map((p) => [p.record.id,p.record.title])).entries()];
  selector.innerHTML = '<option value="">全部实验</option>' + arms.map(([id,title]) =>
    '<option value="' + esc(id) + '">' + esc(title) + '</option>').join('');
  if (arms.some(([id]) => id===currentArm)) selector.value=currentArm;
  renderQuality();
}
function axisNumber(value) {
  if (value >= 1e6) return (value / 1e6).toLocaleString('zh-CN',{maximumFractionDigits:1}) + 'M';
  if (value >= 1e3) return (value / 1e3).toLocaleString('zh-CN',{maximumFractionDigits:1}) + 'k';
  return fmt(value);
}
function lineChart(node, readout, series, settings) {
  const points = series.flatMap((line) => line.points);
  if (!points.length) {
    node.innerHTML = empty('此口径暂无可绘制的观测。');
    readout.textContent = '';
    return;
  }
  const width = Math.max(270,Math.round(node.clientWidth || 700));
  const height = width < 500 ? 235 : 295;
  const x0 = 52, x1 = width-22, y0 = 20, y1 = height-42;
  const minX = settings.localX ? Math.min(...points.map((p) => p.x))-1 : 0;
  const maxX = Math.max(minX+2,...points.map((p) => p.x));
  const maxY = Math.max(settings.minY || 0,...points.map((p) => p.y)) || 1;
  const X = (value) => x0 + (x1-x0)*(value-minX)/(maxX-minX);
  const Y = (value) => y1 - (y1-y0)*value/maxY;
  let svg = '<svg class="chart" viewBox="0 0 ' + width + ' ' + height + '" role="group" aria-label="' + esc(settings.label) + '">';
  for (let i=0;i<=4;i++) {
    const y = Y(maxY*i/4);
    svg += '<path d="M' + x0 + ' ' + y + 'H' + x1 + '" stroke="#d9ded6"/><text x="' + (x0-7) + '" y="' + (y+4) + '" text-anchor="end">' + esc(settings.formatY(maxY*i/4)) + '</text>';
  }
  const ticks = width < 500 ? 3 : 5;
  for (let i=0;i<=ticks;i++) {
    const value = minX + (maxX-minX)*i/ticks;
    svg += '<text x="' + X(value) + '" y="' + (y1+20) + '" text-anchor="middle">' + esc(axisNumber(Math.round(value))) + '</text>';
  }
  svg += '<text x="' + (width/2) + '" y="' + (height-4) + '" text-anchor="middle">' + esc(settings.axis) + '</text>';
  for (const line of series) {
    const sorted = [...line.points].sort((a,b) => a.x-b.x);
    if (sorted.length > 1) svg += '<polyline points="' + sorted.map((p) => X(p.x) + ',' + Y(p.y)).join(' ') + '" fill="none" stroke="' + line.color + '" stroke-opacity=".64" stroke-width="1.8"/>';
    for (const p of sorted) {
      svg += '<g class="mark" tabindex="0" role="img" aria-label="' + esc(p.label) + '" data-point="' + esc(p.label) +
        '"><circle class="ring" cx="' + X(p.x) + '" cy="' + Y(p.y) + '" r="8" fill="transparent"/><circle class="pin" cx="' + X(p.x) +
        '" cy="' + Y(p.y) + '" r="4" fill="' + line.color + '" stroke="#fffdf7" stroke-width="1"/><title>' + esc(p.label) + '</title></g>';
    }
  }
  node.innerHTML = svg + '</svg>';
  const defaultNote = '将鼠标移到数据点或用 Tab 聚焦，查看精确数值。';
  readout.textContent = defaultNote;
  const show = (event) => {readout.textContent = event.currentTarget.dataset.point;};
  node.querySelectorAll('[data-point]').forEach((mark) => {
    mark.addEventListener('mouseenter',show); mark.addEventListener('focus',show); mark.addEventListener('click',show);
  });
}
function renderQuality() {
  if ($('overview').hidden) return;
  const mode = $('quality-metric').value;
  const arm = $('quality-arm').value;
  const points = formalPoints().filter((p) => !arm || p.record.id===arm);
  const byRecord = new Map();
  for (const p of points) {
    const y = mode === 'progress' ? p.mean_progress : (isNum(p.wins) ? p.wins / p.n : null);
    if (y === null) continue;
    const series = byRecord.get(p.record.id) || {name:p.record.title,color:palettes[byRecord.size%palettes.length],points:[]};
    series.points.push({x:p.cumulative_decisions,y,label:qualityLabel(p)});
    byRecord.set(p.record.id,series);
  }
  const lines = [...byRecord.values()];
  lineChart($('quality-chart'),$('quality-readout'),lines,{
    label:'正式开发集' + (mode==='progress'?'平均进度':'胜率'),axis:'累计新决策',
    minY:mode==='progress'?.6:.1,formatY:mode==='progress'?(v)=>dec(v,2):pct
  });
  $('quality-legend').innerHTML = lines.map((line) => '<span style="--legend:' + line.color + '">' + esc(line.name) + '</span>').join('');
  $('quality-details').hidden = !points.length;
  $('quality-detail-label').textContent = '查看 ' + points.length + ' 个正式观测点';
  $('quality-table').innerHTML = table(['实验','累计决策','平均进度','胜 / 局','二 / 三幕到达'],
    points.map((p) => [linkRecord(p.record.id),fmt(p.cumulative_decisions),dec(p.mean_progress,6),
      fmt(p.wins)+' / '+fmt(p.n),fmt(p.act2_reach)+' / '+fmt(p.act3_reach)]),'正式评测观测点');
}
function fillArchiveFilters() {
  const options = (id, label, values, names) => {
    const old = $(id).value;
    $(id).innerHTML = '<option value="">' + esc(label) + '</option>' + [...new Set(values)].sort().map((value) =>
      '<option value="' + esc(value) + '">' + esc(names[value] || value) + '</option>').join('');
    if (values.includes(old)) $(id).value = old;
  };
  options('archive-kind','全部类型',archiveRecords.map((r) => r.kind),kindNames);
  options('archive-status','全部状态',archiveRecords.map((r) => r.status).filter(Boolean),statusNames);
}
function renderArchiveList() {
  const query = $('archive-search').value.trim().toLowerCase();
  const kind = $('archive-kind').value;
  const status = $('archive-status').value;
  const filtered = archiveRecords.filter((r) => (!kind || kind === r.kind) && (!status || status === r.status) &&
    (!query || [r.id,r.title,r.hypothesis,r.group,sentence(r.model_arch),sentence(r.task_ids),sentence(r.recipe_changes)].join(' ').toLowerCase().includes(query)));
  if (!filtered.some((r) => r.id === archiveID)) archiveID = filtered[0]?.id || '';
  $('archive-count').textContent = '匹配 ' + filtered.length + ' 条 · 共 ' + archiveRecords.length + ' 条';
  $('archive-list').innerHTML = ['实验','工程验证','计划'].map((group) => {
    const matches = filtered.filter((r) => r.group === group);
    if (!matches.length) return '';
    return '<li class="category">' + group + ' · ' + matches.length + '</li>' + matches.map((r) =>
      '<li><button type="button" data-record="' + esc(r.id) + '"' + (r.id === archiveID ? ' aria-current="true"' : '') +
      '>' + esc(r.title ?? r.description ?? r.id) + '<small>' + esc(r.id) + ' · ' + esc(statusNames[r.status] || r.status || '未知') + '</small></button></li>').join('');
  }).join('');
  renderArchiveDetail(filtered.find((r) => r.id === archiveID));
}
function linkRecord(id, label) {
  if (!id) return text(label);
  const canonical = data?.history?.id_aliases?.[id] ?? id;
  const record = archiveRecords.find((item) => item.id === canonical || arr(item.aliases).includes(id));
  const name = label ?? record?.title ?? id;
  return record ? '<a href="#experiments/' + esc(record.id) + '" data-goto-record>' + esc(name) + '</a>' : esc(name);
}
function lineage(r) {
  const parent = r.weight_parent;
  const modes = {fresh_random:'随机初始化',fresh:'随机初始化',resume:'继承权重',continuation:'续训',warm_start:'热启动',unknown:'未知'};
  let weight = typeof parent === 'string' ? linkRecord(parent) : text(modes[parent?.mode] || parent?.mode);
  if (parent?.experiment_id) weight += ' · ' + linkRecord(parent.experiment_id);
  const sha = parent?.checkpoint_sha256;
  if (typeof sha === 'string' && /^[a-f0-9]{8,64}$/i.test(sha)) weight += ' · <span class="mono">' + sha.slice(0,12) + '</span>';
  return fields([['权重谱系',weight],['配方参照',r.recipe_parent_experiment_id ? linkRecord(r.recipe_parent_experiment_id) : '未知']]);
}
function protocolLabel(p) {
  const protocol = p.protocol;
  if (!protocol) return '未知';
  if (typeof protocol === 'string') return protocol;
  const off = (v) => v === 'off' || v === false ? '关' : v === 'on' || v === true ? '开' : '未知';
  return [protocol.seed_set_id ?? protocol.seedset_id, protocol.policy, '搜索' + off(protocol.search),
    '破局器' + off(protocol.breaker), protocol.role].filter(Boolean).join(' · ');
}
function diagnosticDetail(r) {
  const sample = r.diagnostic_sample || {};
  const counts = [
    ['诊断样本',r.cases ?? r.diagnostic_results?.cases],['复演局',sample.episodes],['端点一致',r.diagnostic_results?.endpoint_matches],
    ['策略决策',r.diagnostic_policy_steps ?? sample.actor_decisions],['记录动作',sample.recorded_action_steps],['复演动作',r.diagnostic_replay_steps],
    ['采集决策',r.actual?.collected_decisions],['Adam 步',r.actual?.adam_steps]
  ].filter(([,value]) => isNum(value));
  const points = arr(r.diagnostic_score_points);
  return (counts.length ? fields(counts.map(([key,value]) => [key,fmt(value)])) : '') + (points.length ?
    table(['采样温度','平均进度','胜 / 局','二 / 三幕到达','策略'],points.map((p) =>
      [dec(p.temperature,1),dec(p.mean_progress),fmt(p.wins)+' / '+fmt(p.n),
        fmt(p.act2_reach)+' / '+fmt(p.act3_reach),text(p.policy)]),'独立温度诊断') : '');
}
function renderArchiveDetail(r) {
  const detail = $('archive-detail');
  if (!r) {
    detail.innerHTML = empty('没有匹配的实验档案。');
    return;
  }
  const actual = r.actual || {};
  const target = r.target || {};
  const calls = arr(r.actual_calls);
  const ids = new Set(arr(r.formal_eval_ids));
  const formal = ids.size ? arr(data?.history?.formal_evaluations).filter((p) => ids.has(p.evaluation_id)) : arr(r.formal_eval_points);
  const points = formal.map((p) => [text(p.evaluation_id || short(p.cumulative_decisions)),fmt(p.cumulative_decisions),dec(p.mean_progress),
    fmt(p.wins) + ' / ' + fmt(p.n),fmt(p.act2_reach) + ' / ' + fmt(p.act3_reach),text(protocolLabel(p))]);
  const budget = arr(data?.budget?.experiments).find((entry) => entry.id === r.id || arr(r.aliases).includes(entry.id) || data?.history?.id_aliases?.[entry.id] === r.id);
  const checkpoint = r.checkpoint?.sha256 ?? r.checkpoint?.manifest_hash;
  const arch = typeof r.model_arch === 'object' ? r.model_arch?.label ?? r.model_arch?.family ?? r.model_arch?.id : r.model_arch;
  const dose = [
    ['累计新决策',actual.cumulative_decisions,target.cumulative_decisions,actual.accepted_cumulative_decisions],
    ['本实验新决策',actual.additional_decisions,target.additional_decisions,actual.accepted_additional_decisions],
    ['更新',actual.updates,target.updates,actual.accepted_updates]
  ];
  const hasAccepted = dose.some((row) => isNum(row[3]));
  const doseLabel = {completed_optimizer_updates_only:'完成优化器更新',accepted_checkpoint_only:'已验收检查点'}[actual.counter_basis];
  const summary = r.actual_dose_display ? '<p class="notice">' + text(r.actual_dose_display) + '</p>' : '';
  detail.innerHTML = '<div class="split"><div><div class="kicker">' + esc(r.id) + ' · ' + esc(kindNames[r.kind] || r.kind) + '</div><h2 id="record-title" tabindex="-1">' +
    text(r.title ?? r.id) + '</h2></div>' + pill(r.status==='running'?'运行中（档案快照）':statusNames[r.status] || r.status || '未知',r.status==='failed'?'fail':r.group==='计划'?'warn':'') + '</div>' +
    fields([['任务',text(arr(r.task_ids).join('、') || null)],['模型',text(arch)],['开始',iso(r.started_at_utc)],['结束',iso(r.ended_at_utc)],
      ['已记录调用',calls.length ? fmt(calls.length) : r.group==='计划'?'0':'未知'],['状态时间',iso(r.status_as_of_utc ?? actual.status_updated_at_utc)]]) +
    section('实验假设与配方', (r.hypothesis ? '<p>' + text(r.hypothesis) + '</p>' : '') + bullets(r.recipe_changes)) +
    section('父模型与配方谱系',lineage(r)) +
    section('实际与目标剂量',summary + table(['口径',doseLabel || '实际观测','计划目标',...(hasAccepted?['已验收']:[])],
      dose.map(([label,seen,goal,accepted]) => [label,fmt(seen),fmt(goal),...(hasAccepted?[fmt(accepted)]:[])]),'实际与目标剂量') +
      (checkpoint && /^[a-f0-9]{8,64}$/i.test(checkpoint) ? '<p class="note">检查点摘要 <span class="mono">' + checkpoint.slice(0,12) + '</span></p>' : '')) +
    section('正式评测',points.length ? table(['评测','累计决策','平均进度','胜 / 局','二 / 三幕到达','样本口径'],points,'正式评测结果') :
      '<p class="note">本条暂无附属正式开发集结果。</p>') +
    section('故障与恢复',bullets(r.issues_recovery)) +
    section('诊断与采集样本',diagnosticDetail(r)) +
    section('阶段记录',bullets(r.timeline)) +
    section('结论',r.conclusion || r.description ? '<p>' + text(r.conclusion ?? r.description) + '</p>' : '') +
    section('限制与不确定性',bullets(r.uncertainty)) +
    (budget ? section('费用归属',fields([['独占 App 范围计量',usd(budget.app_scope_metered_usd)],['逐调用实付',usd(budget.actual_per_call_usd)]]) +
      '<p><a href="#budget">查看费用口径和共享范围</a></p>') : '') +
    sourceRefs([...arr(r.sources),...[...formal,...arr(r.diagnostic_score_points)].flatMap((p) => [...arr(p.sources),...(p.source?[p.source]:[])])]);
}
function focusArchive(id) {
  if (!archiveRecords.some((r) => r.id === id)) return;
  $('archive-search').value = '';
  $('archive-kind').value = '';
  $('archive-status').value = '';
  archiveID = id;
  history.pushState(null,'','#experiments/' + id);
  route();
  const title = $('record-title');
  if (title) title.focus();
}
const modelLabels = {'s-pre-candidate-critic':'S · 094 / 097','s-current':'S · 099 起','m-historical':'M · 046 / 088','m-current':'M · 116','l-historical-probe':'L · 046 资源探针'};
const flowDetails = {
  public:'只读取公开观察，包括 HP、能量、卡牌、敌人、地图、奖励与提示。',
  rows:'行编码后形成 17 个表 token，并保留逐张手牌、逐个敌人及药水 token。',
  trunk:'Actor 与 Critic 使用同一 Transformer。宽度与层数由所选型号决定。',
  state:'拼接全局 token 和有效 token 的均值，经 MLP 得到共享 state。',
  candidate:'编码动作类型、公开卡牌与目标指针等。奖励明细可经候选 option bank 路由。104 B0 起补齐可从公开信息确定的手选指针。',
  h2:'两层 ReLU 结合共享 state 与候选特征，形成逐候选表示。',
  actor:'共享 state 提供查询向量，与 h2 计算缩放点积后叠加候选偏置。',
  mask:'屏蔽非法候选。训练按类别分布采样，正式 dev 使用贪心动作。',
  pool:'只汇总合法候选表示。候选均值送入 Critic 时停止该分支的梯度。',
  value:'共享 state 投影与候选均值投影相加，再经过 ReLU 得到标量价值。共享 state 仍接收梯度。',
  oldaux:'旧的胜利和战斗辅助头从 state 取特征。其参数已经计入 Policy。',
  nextaux:'拼接共享 state 与选中动作的 h2，经宽度 64 的隐藏层预测四项公开变化。损失系数为 0.1，特征仍参与反向传播。',
  targets:'公开目标为 HP、格挡、能量及敌人 HP 的变化量。目标与 mask 停止梯度，只用于训练损失。'
};
const evolutionCopy = {
  'transition-094':'引入独立 Transition 辅助头，损失系数 0.1。',
  'reward-097':'将公开奖励选项的明细送入候选 option bank。共享 state 的池化规则不变。',
  'critic-099':'Critic 加入停止梯度的合法候选均值，共享 state 仍反向传播。',
  'B0-104':'恢复可公开确定的手选指针，从随机初始化训练新 S。',
  'continue-109-115':'沿用 104 的公开输入、候选感知 Critic 和 Transition 辅助头。114 control 的 β 为 0。',
  'M-116':'使用当前 B0、Critic 和 Transition 配方，从随机初始化训练 M。'
};
const flowRelations = {recipe_evolution_not_weight_resume:'配方演变',fresh_weight_lineage:'随机初始化',weight_resume:'续训'};
const compactFlow = {
  public:[112,10],rows:[5,84],trunk:[5,158],state:[5,232],candidate:[222,84],h2:[222,232],
  actor:[222,326],mask:[222,400],pool:[222,474],value:[112,548],oldaux:[5,326],nextaux:[5,622],targets:[222,622]
};
function flowMarkup(nodes, edges, compact) {
  const view = compact ? [0,0,360,686] : [0,0,1440,830];
  const coords = new Map(nodes.map((node) => {
    const xy = compactFlow[node.id];
    return [node.id,compact && xy ? {...node,x:xy[0],y:xy[1],width:133,height:49} : node];
  }));
  function boundary(a,b) {
    const cx = a.x + a.width/2, cy = a.y + a.height/2;
    const dx = b.x + b.width/2 - cx, dy = b.y + b.height/2 - cy;
    const distance = Math.min(a.width/(2*Math.abs(dx)),a.height/(2*Math.abs(dy)));
    return [cx+dx*distance,cy+dy*distance];
  }
  const wires = edges.map((edge) => {
    const from = coords.get(edge.from), to = coords.get(edge.to);
    if (!from || !to || from === to) return '';
    const start = boundary(from,to), end = boundary(to,from);
    const dashed = edge.kind === 'detached' || edge.kind === 'target_only';
    const color = edge.kind === 'selected_feature' ? '#315e80' : '#648578';
    return '<path class="wire" style="stroke:' + color + '" d="M' + start.join(' ') + 'L' + end.join(' ') +
      '" marker-end="url(#arr-flow)"' + (dashed?' stroke-dasharray="6 5"':'') + '><title>' + text(edge.label) + '</title></path>';
  }).join('');
  return '<svg class="flow" viewBox="' + view.join(' ') + '" role="group" aria-label="公开观察、共享编码和学习分支"><defs><marker id="arr-flow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse"><path d="M0 0 L10 5 L0 10 z" fill="#648578"/></marker></defs>' + wires +
    [...coords.values()].map((n) => '<g class="node" role="button" tabindex="0" aria-label="' + text(n.label) + '" data-node="' + esc(n.id) + '" aria-pressed="' + (n.id===flowID) + '">' +
      '<rect x="' + n.x + '" y="' + n.y + '" width="' + n.width + '" height="' + n.height + '" rx="8"/><text x="' + (n.x+n.width/2) +
      '" y="' + (n.y+n.height/2) + '" dominant-baseline="middle" text-anchor="middle" style="font-size:' + (compact?12:20) + 'px">' + text(n.label) + '</text></g>').join('') + '</svg>';
}
function renderArchitecture() {
  const arch = data?.architecture;
  const profiles = arr(arch?.model_profiles);
  if (!profiles.length) return;
  const profile = profiles.find((p) => p.id === archID) ?? profiles.find((p) => p.id === arch.default_profile_id) ?? profiles[0];
  archID = profile.id;
  const versions = {
    's-pre-candidate-critic':['task094-s','task097-s'],
    's-current':['task099-s','task104-s','task109-s','task114-control','task114-strategic-kl','task115-s'],
    'm-historical':['task046-m-probe','task088-m'],
    'm-current':['task116-m'],
    'l-historical-probe':['task046-l-probe']
  }[profile.id] || [];
  if (!versions.includes(archExperiment)) archExperiment = versions.at(-1) || '';
  const capabilities = arch.experiment_capabilities?.[archExperiment] || {};
  const aware = capabilities.candidate_aware_critic ?? (profile.value_candidate_projection_parameters > 0);
  const transition = capabilities.transition_aux ?? (profile.transition_aux_parameters > 0);
  const graph = arch.graph;
  const nodes = arr(graph?.nodes).filter((n) => (aware || n.id !== 'pool') && (transition || !['nextaux','targets'].includes(n.id)));
  if (!nodes.some((n) => n.id === flowID)) flowID = nodes[0]?.id;
  const probe = profile.id === 'l-historical-probe';
  $('architecture-content').innerHTML =
    '<div class="card stack"><div><h3>模型与版本</h3><div class="versions" role="group" aria-label="模型版本">' + profiles.map((p) =>
      '<button type="button" class="plain" data-profile="' + esc(p.id) + '" aria-pressed="' + (p.id===archID) + '">' + text(modelLabels[p.id] ?? p.label) + '</button>').join('') + '</div></div>' +
    (versions.length ? '<label class="note">查看该型号的实验版本 <select id="architecture-version">' + versions.map((id) =>
      '<option value="' + esc(id) + '"' + (id===archExperiment?' selected':'') + '>' + esc(id) + '</option>').join('') + '</select></label>' : '') +
    (probe ? '<p class="notice warn">旧 H100 80GB 资源探针：首个 4096 样本的有梯度前向 OOM。优化器更新 0 次，未产生正式训练曲线或评测。</p>' : '') +
    fields([['编码宽度 d / state 宽度',fmt(profile.d)+' / '+fmt(profile.state_width)],['Transformer 层 / 注意力头',fmt(profile.depth)+' / '+fmt(profile.heads)],
      ['FFN 倍率',fmt(profile.ff_mult)],['Policy（含旧辅助头）',fmt(profile.policy_parameters)],['Transition 辅助头',fmt(profile.transition_aux_parameters)],
      [probe?'探针模型参数':'总训练参数',fmt(profile.total_training_parameters)]]) + sourceRefs(profile.source_refs) + '</div>' +
    '<div class="card stack"><div><h3>公开输入与共享编码</h3><p class="note">按所选版本的源码证据重绘。实线是前向特征，虚线表示停止梯度的候选均值或仅用于损失的公开目标。</p></div>' +
    '<div class="flow-wrap" id="flow-diagram" style="width:min(100%,940px)"></div><div class="notice" id="flow-detail" role="status"></div></div>' +
    '<div class="card stack"><h3>配方变更时间线</h3>' + table(['实验','配方变化','参数变化','关系'],arr(arch.evolution).map((e) =>
      [arr(e.experiment_ids ?? (e.experiment_id?[e.experiment_id]:[])).map((id) => linkRecord(id,id)).join('<br>'),text(evolutionCopy[e.id] ?? e.change) + sourceRefs(e.source_refs),
        isNum(e.parameter_delta) ? (e.parameter_delta>0?'+':'')+fmt(e.parameter_delta) : '未知',text(flowRelations[e.relation] ?? e.relation)]),'架构配方演变') +
    (arr(arch.historical_visuals).length ? '<details><summary>早期可视化与资料</summary><p class="note">旧进度图记录早期实验曲线。旧实时 Harness 描述的是 LLM 与 MCP 流程。此处模型图按自主 PPO 源码证据重绘。</p>' +
      list(arr(arch.historical_visuals).map((v) => [shortRef(v.path),v.version].filter(Boolean).join(' · '))) +
      sourceRefs(arch.historical_visuals.flatMap((v) => arr(v.source_refs))) + '</details>' : '') + '</div>';
  const updateFlow = () => {
    const node = nodes.find((entry) => entry.id === flowID);
    const custom = node?.id==='value' && !aware ? '此版本从共享 state 预测标量价值。候选均值投影从 099 起加入。' :
      node?.id==='candidate' ? '候选编码包含公开动作字段及关联指针。' +
        (capabilities.reward_choices_routing ? '奖励明细经候选 option bank 路由。' : '此版本未启用 097 的公开奖励明细路由。') +
        (capabilities.B0_public_handselect ? 'B0 恢复可从公开信息确定的手选指针。' : '此版本尚未启用 B0 手选指针恢复。') :
      node?.id==='public' && !capabilities.reward_choices_routing ? '只使用 HP、能量、卡牌、敌人、地图和提示等公开观察。' : flowDetails[node?.id];
    $('flow-detail').innerHTML = node ? '<strong>' + text(node.label) + '</strong><p>' + text(custom ?? node.detail) + '</p>' + sourceRefs(node.source_refs) : '暂无模块说明。';
    $('flow-diagram').querySelectorAll('[data-node]').forEach((el) => el.setAttribute('aria-pressed',String(el.dataset.node===flowID)));
  };
  $('flow-diagram').innerHTML = flowMarkup(nodes,arr(graph?.edges),window.innerWidth < 650);
  $('flow-diagram').querySelectorAll('[data-node]').forEach((el) => {
    const choose = () => {flowID=el.dataset.node; updateFlow();};
    el.addEventListener('click',choose);
    el.addEventListener('keydown',(e) => {if (e.key==='Enter' || e.key===' ') {e.preventDefault(); choose();}});
  });
  updateFlow();
  $('architecture-version')?.addEventListener('change',(event) => {
    archExperiment = event.target.value;
    renderArchitecture();
    $('architecture-version').focus();
  });
}
const seconds = (v) => isNum(v) ? fmt(v) + ' 秒' : '未知';
const speed = (v) => isNum(v) ? fmt(Math.round(v*10)/10) : '未知';
const phaseNames = {
  environment_s:'环境', inference_host_interval_s:'推理宿主区间', learner_update_s:'学习器更新', checkpoint_phase_s:'检查点',
  evaluation_phase_s:'评测', iteration_total_s:'迭代总计', unallocated_host_interval_s:'其他宿主区间', sync_s:'独立同步',
  network_publish_s:'独立网络发布', prefetch_s:'预取（可重叠）', trainer_cold_start_s:'训练器冷启动',
  complete_update_s:'完整更新', pool_warm_included_s:'池预热（含于冷启动）', worker_runtime_init_s:'Worker 初始化',
  checkpoint_write_s:'写入检查点', ready_publication_s:'发布就绪', trainer_first_round_including_cold_s:'含冷启动首轮'
};
function phaseTable(phases) {
  return fields(Object.entries(phases || {}).map(([key,value]) => [phaseNames[key] || key,seconds(value)]));
}
function aggregateDetail(aggregate, experimentID) {
  let phases = aggregate.phase_seconds;
  let inspection;
  if (!isNum(phases?.iteration_total_s) && aggregate.kind === 'complete_segment1_summary' && aggregate.segment_id === 'segment1') {
    const task = experimentID.match(/^task\d+/)?.[0];
    const path = task + '/actual/segment1/training-inspection/inspection.json';
    const budget = arr(data?.budget?.experiments).find((entry) => entry.id === experimentID);
    inspection = arr(budget?.estimates).find((estimate) => estimate.experiment_id === experimentID &&
      estimate.new_decisions === aggregate.observed_decisions && estimate.duration_source === path &&
      typeof estimate.elapsed_update_seconds === 'string' && /^\d+(\.\d+)?$/.test(estimate.elapsed_update_seconds) &&
      Number.isFinite(Number(estimate.elapsed_update_seconds)));
    if (inspection) phases = {...phases,iteration_total_s:Number(inspection.elapsed_update_seconds)};
  }
  const notes = [aggregate.coverage || (aggregate.kind === 'complete_segment1_summary' ? '第一段完整训练汇总' : null),
    isNum(aggregate.observed_decisions) ? fmt(aggregate.observed_decisions) + ' 决策' : null,
    isNum(aggregate.derived_iteration_decisions_per_s) ? '窗口 ' + speed(aggregate.derived_iteration_decisions_per_s) + ' 决策/秒' : null,
    isNum(aggregate.reported_session_decisions_per_s) ? '会话报告 ' + speed(aggregate.reported_session_decisions_per_s) + ' 决策/秒' : null
  ].filter(Boolean);
  const refs = [...arr(aggregate.source_refs),...(inspection ? [inspection.duration_source] : [])];
  return '<section><h3>' + (isNum(aggregate.first_update) ? 'u'+fmt(aggregate.first_update)+'–'+fmt(aggregate.last_update) :
    text(aggregate.kind ?? '已保留的采样')) + '</h3>' +
    (notes.length ? '<p class="note">' + esc(notes.join(' · ')) + '</p>' : '') +
    phaseTable(phases) + (inspection ? '<p class="note">迭代总计使用该段训练检查记录的 t_total_s。</p>' : '') +
    sourceRefs(refs) + '</section>';
}
function renderPerformance() {
  const perf = data?.performance;
  const entries = arr(perf?.experiments);
  if (!entries.length) return;
  const exp = entries.find((p) => p.experiment_id===perfID) ?? entries.find((p)=>p.experiment_id==='task115-s') ?? entries[0];
  perfID = exp.experiment_id;
  const windows = arr(perf.update_windows).filter((p) => p.experiment_id===perfID);
  const status = arr(perf.status_snapshots).filter((p) => p.experiment_id===perfID);
  const checkpoints = arr(perf.checkpoint_points).filter((p) => p.experiment_id===perfID);
  const probe = arr(perf.probe_results).find((p) => p.experiment_id===perfID);
  const estimates = arr(perf.estimates).filter((p) => p.experiment_id===perfID);
  const timing = windows.map((p) => [text('u'+p.first_update+'–'+p.last_update),fmt(p.observed_decisions),seconds(p.phase_seconds?.environment_s),
    seconds(p.phase_seconds?.inference_host_interval_s),seconds(p.phase_seconds?.learner_update_s),
    seconds(p.phase_seconds?.checkpoint_phase_s),seconds(p.phase_seconds?.iteration_total_s),speed(p.derived_iteration_decisions_per_s)]);
  const checkpointsRows = checkpoints.map((p) => [text(p.segment_id),iso(p.observed_at_utc),fmt(p.updates),
    fmt(p.cumulative_decisions_derived_from_update),fmt(p.experiment_additional_decisions_derived),sourceRefs(p.source_refs)]);
  const states = status.map((p) => [text(p.segment_id),iso(p.status_updated_at_utc),fmt(p.updates),fmt(p.cumulative_decisions),speed(p.reported_decisions_per_s),
    speed(p.reported_session_decisions_per_s),seconds(p.session_elapsed_s),sourceRefs(p.source_refs)]);
  $('performance-content').innerHTML =
    '<div class="card stack"><div class="split"><label class="note">选择实验 <select id="perf-experiment">' + entries.map((item) =>
      '<option value="' + esc(item.experiment_id) + '"' + (item.experiment_id===perfID?' selected':'') + '>' + esc(item.experiment_id) + '</option>').join('') +
      '</select></label>' + linkRecord(perfID,'打开实验档案') + '</div>' + fields([
      ['GPU',text(exp.hardware?.gpu)],['环境线程',fmt(exp.hardware?.environment_threads)],['GPU 数量',fmt(exp.hardware?.gpu_count)],
      ['每次更新新决策',fmt(exp.rollout?.decisions_per_update)],['环境数 × rollout 长度',fmt(exp.rollout?.envs)+' × '+fmt(exp.rollout?.length)],
      ['学习器 minibatch',fmt(exp.rollout?.learner_minibatch)],['权重父剂量',fmt(exp.parent_cumulative_decisions)],
      ['证据数据截至',iso(perf.latest_observed_status_at_utc)]]) + sourceRefs(exp.source_refs) + '</div>' +
    '<figure class="card"><h3>更新吞吐</h3><p class="note">按各点自己的 PPO 更新编号绘制。窗口耗时汇总和训练器快照采用各自口径。</p>' +
    '<div id="perf-chart"></div><div class="legend"><span style="--legend:#286a5b">窗口决策 / 迭代秒</span><span style="--legend:#315e80">末次更新报告值</span>' +
    '<span style="--legend:#a34e44">当前会话报告值</span></div><figcaption id="perf-readout" class="readout"></figcaption></figure>' +
    '<div class="card stack"><h3>分阶段耗时</h3><p class="note">推理与学习器耗时是宿主区间。独立同步或发布耗时缺测时显示未知。重叠的预取耗时不计入分项合计。</p>' +
    (arr(exp.phase_aggregates).length ? arr(exp.phase_aggregates).map((p) => aggregateDetail(p,perfID)).join('') :
      '<p class="note">此实验暂无绑定的阶段耗时聚合。</p>') +
    (timing.length ? '<details><summary>逐窗口明细（' + timing.length + '）</summary>' +
      table(['更新范围','窗口决策','环境','推理','更新','检查点','迭代总计','决策/秒'],timing,'逐窗口耗时') +
      sourceRefs(windows.flatMap((p) => arr(p.source_refs))) + '</details>' : '') + '</div>' +
    (probe ? '<div class="card stack"><h3>资源探针</h3><p class="note">' + (probe.formal_training===false
      ? '首次 4096 有梯度前向 OOM。优化器更新 0 次，未产生正式训练或评测。'
      : '通过单次更新验证。此处只展示资源探针。') + '</p>' + phaseTable(probe.phase_seconds) + sourceRefs(probe.source_refs) + '</div>' : '') +
    '<div class="grid two"><div class="card stack"><h3>训练器状态快照</h3>' +
    table(['分段','状态时刻','更新','累计决策','更新决策/秒','会话决策/秒','会话时长','证据'],states,'训练器状态快照') +
    '</div><div class="card stack"><h3>保存检查点里程碑</h3><p class="note">由写入时的更新号推导累计剂量，快照与检查点可能处于不同更新。</p>' +
    table(['分段','观测时刻','检查点更新','累计决策（推导）','新增（推导）','证据'],checkpointsRows,'检查点里程碑') + '</div></div>' +
    (estimates.length ? '<div class="card stack"><h3>规划估算</h3>' + estimates.map((p) =>
      '<p>' + fmt(p.decision_dose) + ' 决策 · 假设 ' + arr(p.assumed_decisions_per_s_range).map(fmt).join('–') +
      ' 决策/秒 · ' + arr(p.estimated_training_hours_range).map((v)=>dec(v,2)).join('–') + ' 小时。估算未含启动、载入和保存。</p>' +
      sourceRefs(p.source_refs)).join('') + '</div>' : '');
  $('perf-experiment').addEventListener('change',(event) => {
    perfID=event.target.value; renderPerformance(); $('perf-experiment').focus();
  });
  const chartNode = $('perf-chart'), readout = $('perf-readout');
  const series = [
    {color:'#286a5b',points:windows.map((p) => ({x:p.last_update,y:p.derived_iteration_decisions_per_s,label:'u'+p.first_update+'–'+p.last_update+
      ' · '+fmt(p.observed_decisions)+' 决策 · 窗口 '+speed(p.derived_iteration_decisions_per_s)+' 决策/秒'}))},
    {color:'#315e80',points:status.map((p) => ({x:p.updates,y:p.reported_decisions_per_s,label:'u'+p.updates+
      ' · 更新 '+speed(p.reported_decisions_per_s)+' 决策/秒 · '+iso(p.status_updated_at_utc)}))},
    {color:'#a34e44',points:status.map((p) => ({x:p.updates,y:p.reported_session_decisions_per_s,label:'u'+p.updates+
      ' · 会话 '+speed(p.reported_session_decisions_per_s)+' 决策/秒 · '+iso(p.status_updated_at_utc)}))}
  ];
  for (const line of series) line.points = line.points.filter((p) => isNum(p.x) && isNum(p.y));
  lineChart(chartNode,readout,series,{label:'训练更新决策吞吐',axis:'累计 PPO 更新',localX:true,formatY:(v)=>axisNumber(Math.round(v))});
}
const categoryNames = {
  training:'训练', formal_dev:'正式开发集', formal_evaluation:'正式评测', diagnostic:'诊断', engineering:'工程验证',
  resource_probe:'资源探针', probe:'资源探针', evaluation:'正式评测', research:'研究', mixed_shared_project:'混合用途共享',
  unassigned:'未归属', unmatched_workspace:'未匹配 App', external_api:'外部 API'
};
const resourceNames = {CPU:'CPU', H100:'H100 GPU', Memory:'内存', 'Deployed Apps':'已部署 App', 'LLM Tokens':'LLM Token',
  'Network Egress':'网络流出', Volumes:'存储卷'};
const appTypes = {exclusive_experiment_app_scope:'独占实验 App',shared_app_scope:'共享 App',unmatched_workspace_app:'未匹配 App'};
function budgetAppID(id) { return typeof id === 'string' ? id.slice(0,6) + '…' + id.slice(-5) : '未知'; }
function amountTable(values,label) {
  return table(['资源','计量 USD'],Object.entries(values || {}).map(([key,value]) => [text(resourceNames[key] ?? key),usd(value)]),label);
}
function renderBudget() {
  const budget = data?.budget;
  if (!budget?.workspace || !budget?.report) return;
  const {workspace:w,report:r,ledger={},archive={}} = budget;
  const scopes = arr(budget.app_scopes);
  const slots = arr(budget.direct_plan_slots);
  const activePlan = slots.length ? slots.reduce((sum,entry) => sum + (isNum(entry.active_cents)?entry.active_cents:0),0) : null;
  const categoryRows = arr(budget.categories).map((c) => [text(categoryNames[c.category] ?? c.category),usd(c.app_scope_metered_usd),
    fmt(c.ledger_job_count),cents(c.raw_nominal_reserve_cents),cents(c.active_unknown_hold_cents)]);
  const budgetExp = arr(budget.experiments);
  $('budget-content').innerHTML =
    '<div class="grid four">' + metric('项目预算上限',cents(ledger.project_cap_cents),'预算口径 · USD') +
    metric('Workspace 毛计量',usd(w.metered_gross_usd),'全工作区，用量口径') +
    metric(w.invoiced_net_label || '工作区账单净额（截至采集）',usd(w.invoiced_net_usd),'实际现金支付 ' + usd(w.paid_cash_usd)) +
    metric('Workspace 抵扣额度',usd(w.credits_usd),'免费存储调整 ' + usd(w.free_storage_adjustment_usd)) + '</div>' +
    '<div class="notice">Workspace 快照 ' + iso(w.observed_at) + '。小时报告覆盖 ' + iso(r.coverage_start) + ' 至 ' +
    iso(r.coverage_end_exclusive) + '（不含结束时刻）。小时计量、账单和各类承诺对应不同口径。</div>' +
    '<div class="grid two"><div class="card stack"><h3>Workspace 资源摘要</h3>' +
    amountTable(w.metered_breakdown_usd,'Workspace 资源') +
    fields([['明细与毛计量对账差额',usd(w.unreconciled_breakdown_difference_usd)]]) + '</div>' +
    '<div class="card stack"><h3>已返回小时的 App 用量</h3>' +
    amountTable(r.cost_by_resource_usd,'小时用量按资源') +
    fields([['小时报告总计',usd(r.total_metered_usd)],['小时总计与资源分项差额',usd(r.reported_row_total_minus_resource_sum_usd)],['与项目关联的 App',usd(r.project_associated_metered_usd)],
      ['未匹配 App',usd(r.unmatched_app_metered_usd)],['Workspace App 与小时报告差额',usd(r.workspace_apps_not_in_hourly_report_usd)],
      ['混合用途共享 App',usd(r.shared_project_app_scopes?.mixed_project_app_metered_usd)],
      ['114 两臂共享训练 App（已计入训练）',usd(r.shared_project_app_scopes?.task114_two_arm_training_app_metered_usd)]]) +
    '<p class="note">未匹配和覆盖差额均保持未归属。缺失小时可能尚未回报。</p></div></div>' +
    '<div class="card stack"><h3>用途与预留</h3><p class="note">App 计量按用途列示。共享训练 App 在用途表只计一次。原始名义预留（含历史）及仍占用额度与计量分列。</p>' +
    table(['用途','App 范围计量','台账任务','原始名义预留（含历史）','未知结算占用'],categoryRows,'用途与预留') + '</div>' +
    '<div class="card stack"><h3>按实验归属</h3><p class="note">逐调用实付未知。独占 App 金额仅覆盖已返回用量。共享计量与时长估算单列，同一 App 只记一份费用。</p>' +
    '<label class="note">筛选实验 <input id="budget-search" type="search" placeholder="实验名或任务号"></label><div id="budget-experiments"></div></div>' +
    '<div class="card stack"><h3>App 计量明细</h3><p class="note">每个 App 的已知小时窗口及资源，包含共享和未匹配记录。</p>' +
    scopes.map((scope) => '<details><summary>' + text(budgetAppID(scope.app_id)) + ' · ' +
      text(scope.scope_label ?? appTypes[scope.attribution] ?? scope.attribution) + ' · ' + usd(scope.metered_usd) + '</summary><p class="note">已返回 ' +
      fmt(scope.rows) + ' 小时桶 · 首 ' + iso(scope.first_interval) + ' · 末 ' + iso(scope.last_interval) + '</p>' +
      '<p>' + (arr(scope.experiment_ids).map((id) => linkRecord(id,id)).join(' · ') || '实验未归属') + '</p>' +
      amountTable(scope.cost_by_resource_usd,'App 资源') + sourceRefs(scope.source?[scope.source]:[]) + '</details>').join('') + '</div>' +
    '<div class="card stack"><h3>承诺与未知结算</h3><p class="note">账本取样 ' + iso(ledger.observed_at) +
    '。原始名义预留包含当前及历史记录。台账占用、直接计划和归档额度按来源列示。</p>' +
    fields([['台账未知结算占用',cents(ledger.active_unknown_hold_cents)],['其中 Modal / 非 Modal',cents(ledger.modal_active_unknown_hold_cents)+' / '+cents(ledger.non_modal_active_unknown_hold_cents)],
      ['直接计划仍占用',cents(activePlan)],['原始名义预留（含历史）',cents(ledger.raw_nominal_reserve_cents)],
      ['独立归档额度',cents(archive.commitment_cents)],['归档实际金额',usd(archive.actual_usd)],
      ['外部 API 实际金额',usd(budget.external_api?.non_modal_actual_usd)]]) +
    '<details><summary>直接计划槽位</summary>' + table(['计划','槽位','当前占用','历史上限','实付'],
      slots.map((slot) => [text(String(slot.plan_event ?? '').match(/task\d+/)?.[0]),text(slot.slot),cents(slot.active_cents),
        cents(slot.historical_maximum_cents),usd(slot.actual_cost_usd)]),'直接计划槽位') + '</details></div>';
  function renderExperimentCosts() {
    const query = $('budget-search').value.trim().toLowerCase();
    const filtered = budgetExp.filter((e) => [e.id,archiveRecords.find((r)=>r.id===e.id)?.title].join(' ').toLowerCase().includes(query));
    $('budget-experiments').innerHTML = filtered.length ? filtered.map((e) => {
      const appScopes = scopes.filter((scope) => arr(e.app_scope_ids).includes(scope.app_id));
      const estimates = arr(e.estimates);
      return '<details><summary>' + text(e.id) + ' · 独占 App ' + usd(e.app_scope_metered_usd) + '</summary><p>' +
        linkRecord(e.id,'打开实验档案') + '</p>' + (e.app_scope_metered_label ? '<p class="note">' + text(e.app_scope_metered_label) + '</p>' : '') + fields([
          ['逐调用实付',usd(e.actual_per_call_usd)],['台账任务',fmt(e.ledger_job_count)],['原始名义预留（含历史）',cents(e.raw_nominal_reserve_cents)],
          ['未知结算占用',cents(e.active_unknown_hold_cents)]]) +
        (appScopes.length ? table(['App','归属口径','App 范围计量','小时桶','最后小时'],appScopes.map((scope) =>
          [text(budgetAppID(scope.app_id)),text(appTypes[scope.attribution] ?? scope.attribution),
            scope.attribution==='shared_app_scope'?'见 App 明细':usd(scope.metered_usd),fmt(scope.rows),iso(scope.last_interval)]),'实验关联 App') : '') +
        (estimates.length ? '<p class="note">按本段 PPO 更新周期墙时估算，覆盖采样、推理、优化和周期内检查点。该估算可能与 App 计量重叠，单独列示。</p>' +
          table(['估算 USD','观察时长','范围'],estimates.map((p) =>
            [usd(p.estimated_compute_usd),text(p.elapsed_update_seconds)+' 秒','本段 PPO 更新周期']),'实验计算时长估算') : '') + '</details>';
    }).join('') : empty('没有匹配的费用档案。');
  }
  $('budget-search').addEventListener('input',renderExperimentCosts);
  renderExperimentCosts();
}
function updateLive() {
  const runs = arr(data?.live?.runs);
  $('run-cards').innerHTML = runs.length ? runs.map(liveCard).join('') : empty('实时采样暂未回传 S115 / M116。');
}
async function poll() {
  try {
    const response = await fetch('/api/research',{cache:'no-store'});
    if (!response.ok) throw new Error('unavailable');
    const next = await response.json();
    if (!next || next.schema_version !== 1) throw new Error('unsupported');
    const changed = !data || ['history','architecture','performance','budget'].some((key) =>
      JSON.stringify(next[key]) !== JSON.stringify(data[key]));
    const liveChanged = !data || JSON.stringify(next.live) !== JSON.stringify(data.live);
    data = next;
    fetchedAt = new Date().toISOString();
    connectionFailed = false;
    renderConnection();
    if (changed) {
      archiveRecords = historyRecords();
      fillArchiveFilters();
      renderOverview();
      renderArchitecture();
      renderBudget();
      route();
    } else if (liveChanged) updateLive();
  } catch (_) {
    connectionFailed = true;
    renderConnection();
  }
}
$('quality-metric').addEventListener('change',renderQuality);
$('quality-arm').addEventListener('change',renderQuality);
$('archive-search').addEventListener('input',renderArchiveList);
$('archive-kind').addEventListener('change',renderArchiveList);
$('archive-status').addEventListener('change',renderArchiveList);
$('archive-list').addEventListener('click',(event) => {
  const button = event.target.closest('[data-record]');
  if (button) {
    archiveID = button.dataset.record;
    history.replaceState(null,'','#experiments/' + archiveID);
    $('archive-list').querySelectorAll('[aria-current]').forEach((el) => el.removeAttribute('aria-current'));
    button.setAttribute('aria-current','true');
    renderArchiveDetail(archiveRecords.find((r) => r.id===archiveID));
    if (window.innerWidth < 650) $('record-title')?.focus();
  }
});
$('architecture-content').addEventListener('click',(event) => {
  const button = event.target.closest('[data-profile]');
  if (button) {
    archID = button.dataset.profile;
    renderArchitecture();
    $('architecture-content').querySelector('[aria-pressed="true"][data-profile]')?.focus();
  }
});
document.addEventListener('click',(event) => {
  const link = event.target.closest('[data-goto-record]');
  if (link && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey) {
    event.preventDefault();
    focusArchive(link.getAttribute('href').split('/')[1]);
  }
});
document.querySelector('nav').addEventListener('click',(event) => {
  const link = event.target.closest('[data-tab]');
  if (!link || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
  event.preventDefault();
  history.pushState(null,'',link.getAttribute('href'));
  route();
});
window.addEventListener('hashchange',route);
window.addEventListener('resize',() => {
  if (Math.abs(window.innerWidth-lastSize) < 20) return;
  const wasCompact = lastSize < 650;
  lastSize = window.innerWidth;
  if (!$('architecture').hidden && wasCompact !== (lastSize<650)) renderArchitecture();
  if (!$('performance').hidden) renderPerformance();
  renderQuality();
});
lastSize = window.innerWidth;
route();
poll();
setInterval(poll,45000);
setInterval(renderConnection,30000);
})();
</script>
</body>
</html>`;
