#!/usr/bin/env node
// Generates docs/explanation/repo-workflow.html: how this repository works under repo-blueprint, built
// from its own configuration so it never drifts - the flow from idea to release, the gates per stage
// (tooling/gates.yaml), agent tiers, roles, skills and hooks (.agents/), the folders in docs/ and
// project/, the hard stops (AGENTS.md) and CI. Output is deterministic; --check fails when the page is
// out of date (gate `overview`). Never edit the page by hand: change the sources and re-run.
// Usage: node tooling/scripts/render-overview.mjs [--check]
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { listFiles, report, root } from './lib/repo.mjs';
import { loadGates } from './lib/gates.mjs';
import { readFrontMatter } from './lib/yaml.mjs';

const OUT = 'docs/explanation/repo-workflow.html';
const read = (path) => (existsSync(join(root, path)) ? readFileSync(join(root, path), 'utf8') : '');
const json = (path) => (existsSync(join(root, path)) ? JSON.parse(read(path)) : {});
const esc = (text) => String(text ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
const md = (text) => esc(text).replace(/`([^`]+)`/g, '<code>$1</code>').replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>').replace(/\[([^\]]+)\]\([^)]+\)/g, '$1');
const table = (head, rows) => rows.length ? `<div class="wrap"><table><thead><tr>${head.map((h) => `<th>${h}</th>`).join('')}</tr></thead><tbody>${rows.map((r) => `<tr>${r.map((c) => `<td>${c}</td>`).join('')}</tr>`).join('')}</tbody></table></div>` : '<p class="muted">None.</p>';

const manifest = json('tooling/blueprint.json');
const project = manifest.values?.PROJECT ?? root.split(/[\\/]/).pop();
const files = listFiles().map((file) => file.replaceAll('\\', '/'));
// Install output (node_modules, bin, obj, dist) counts as present: the page must be the same on a machine with
// and without installed dependencies, or the overview gate would differ between host and CI container.
const installOutput = /(^|\/)(node_modules|bin|obj|dist)(\/|$)/;
const has = (requirement) => requirement.startsWith('glob:') ? files.some((file) => new RegExp(requirement.slice(5)).test(file)) : requirement.split('|').some((path) => installOutput.test(path) || existsSync(join(root, path)));

// Gates
const gates = loadGates(root).config;
const WHEN = {
  edit: 'Agent hook after every edit', 'turn-end': 'Agent hook before a turn ends', quick: 'Anyone, while working',
  docs: 'Documentation changes', 'pre-commit': 'Git hook on commit (staged files)', 'pre-push': 'Git hook on push',
  verify: 'Definition of done for a chunk', ci: 'Local CI container and runners', build: 'After an image is pushed to Zot',
  release: 'Release skill', nightly: 'Nightly schedule'
};
const active = (id) => !(gates.checks?.[id]?.requires ?? []).some((requirement) => !has(requirement));
const stageRows = Object.entries(gates.stages ?? {}).map(([stage, ids]) => [
  `<code>${esc(stage)}</code>`, esc(WHEN[stage] ?? ''),
  ids.filter(active).map((id) => `<code>${esc(id)}</code>`).join(' ') + (ids.some((id) => !active(id)) ? ` <span class="muted">(+${ids.filter((id) => !active(id)).length} not applicable here)</span>` : '')
]);
const checkRows = Object.entries(gates.checks ?? {}).filter(([id]) => active(id)).map(([id, check]) => [
  `<code>${esc(id)}</code>`, `<code>${esc(check.run)}</code>`, esc([check.tools?.length ? `needs ${check.tools.join(', ')}` : '', check.staged !== undefined ? 'changed files only' : ''].filter(Boolean).join('; '))
]);

// Agents
const config = json('.agents/agents.config.json');
const tierRows = Object.entries(config.tiers ?? {}).map(([tier, t]) => [`<strong>${esc(tier)}</strong> ${esc(t.label ?? '')}`, esc(t.claude?.model ?? 'inherit'), esc(`${t.codex?.model ?? 'inherit'}, effort ${t.codex?.effort ?? '-'}`)]);
const listDir = (dir) => (existsSync(join(root, dir)) ? readdirSync(join(root, dir)).sort() : []);
const roleRows = listDir('.agents/roles').filter((f) => f.endsWith('.md')).map((f) => {
  const { data } = readFrontMatter(read(`.agents/roles/${f}`));
  return [`<code>${esc(data.name ?? f)}</code>`, esc(data.tier ?? ''), md(data.description ?? '')];
});
const skillRows = listDir('.agents/skills').filter((d) => existsSync(join(root, '.agents/skills', d, 'SKILL.md'))).map((d) => {
  const { data } = readFrontMatter(read(`.agents/skills/${d}/SKILL.md`));
  return [`<code>${esc(data.name ?? d)}</code>`, md(data.description ?? '')];
});
const HOOKS = [
  ['session-start.mjs', 'Session start', 'Branch, uncommitted changes, open owner decisions, STATUS.md and a newer blueprint into the context; stages last session\'s metrics'],
  ['pre-tool.mjs', 'Before a shell call or edit', 'Blocks --no-verify, hook bypasses, force push and edits of generated files, .env and keys'],
  ['post-edit.mjs', 'After an edit', 'Runs the `edit` gate on the edited files'],
  ['stop.mjs', 'Before the turn ends', 'Runs the `turn-end` gate; the agent must fix what it reports'],
  ['session-end.mjs', 'Session end', 'Appends exact token counts and gate result to project/metrics/sessions.jsonl']
];
const hookRows = HOOKS.filter(([file]) => existsSync(join(root, '.agents/hooks', file))).map(([file, when, what]) => [`<code>${file}</code>`, esc(when), md(what)]);

// Folders
const PURPOSE = {
  'docs/tutorials': 'Learning by doing', 'docs/how-to': 'Solving a concrete task', 'docs/reference': 'Exact contracts and settings',
  'docs/explanation': 'How and why the product works', 'docs/adr': 'Architecture decisions (NNNN-slug.md)', 'docs/assets': 'Images for the docs',
  'project/proposals': 'Ideas and designs before a phase', 'project/phases': 'One folder per phase: plan.md, protocol.md, evidence/',
  'project/reviews': 'Dated assessments: security, CVE, architecture', 'project/protocols': 'Dated and release test protocols',
  'project/releases': 'Release notes X.Y.Z.md', 'project/evidence': 'Evidence across phases and per release',
  'project/process': 'How the work is done', 'project/templates': 'Templates for every record', 'project/metrics': 'Session metrics (hook-written)',
  'project/handoffs': 'Work waiting for owner authority', 'project/archive': 'Superseded material'
};
const folderRows = ['docs', 'project'].flatMap((top) => [...new Set(files.filter((f) => f.startsWith(`${top}/`) && f.split('/').length > 2).map((f) => f.split('/').slice(0, 2).join('/')))].sort()
  .map((dir) => [`<code>${esc(dir)}/</code>`, esc(PURPOSE[dir] ?? '')]));

// Rules: the hard stops in AGENTS.md
const agents = read('AGENTS.md');
const stopSection = agents.split(/^## /m).find((section) => /^Hard (stops|rules)/i.test(section)) ?? '';
const stops = stopSection.split('\n').filter((line) => /^(- |\d+\. )/.test(line)).map((line) => `<li>${md(line.replace(/^(- |\d+\. )/, ''))}</li>`);

// CI
const workflows = files.filter((f) => /^\.(github|gitea)\/workflows\/[^/]+\.ya?ml$/.test(f));

const flow = ['Ticket or proposal', 'Phase plan', 'Chunk = session + commit', 'pre-commit / pre-push', 'PR + CI', 'Release + Zot scan'];
const svg = `<svg viewBox="0 0 960 120" role="img" aria-label="Flow from idea to release">${flow.map((label, i) => {
  const x = 8 + i * 158;
  return `<g><rect x="${x}" y="30" width="140" height="56" rx="10" class="box"/><text x="${x + 70}" y="62" text-anchor="middle" class="label">${esc(label)}</text>${i < flow.length - 1 ? `<path d="M${x + 142} 58 h14" class="arrow"/><path d="M${x + 150} 53 l6 5 -6 5" class="arrow"/>` : ''}</g>`;
}).join('')}</svg>`;

const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(project)} workflow</title>
<!-- Generated by tooling/scripts/render-overview.mjs; do not edit by hand. -->
<style>
:root { --bg:#fbfaf7; --fg:#1d1d1b; --muted:#6b6b66; --line:#e2dfd6; --card:#ffffff; --accent:#2f5d8a; --code:#f1efe8; }
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --bg:#161615; --fg:#ecebe6; --muted:#a3a29b; --line:#33322e; --card:#1f1e1c; --accent:#8fb4dc; --code:#2a2926; } }
:root[data-theme="dark"] { --bg:#161615; --fg:#ecebe6; --muted:#a3a29b; --line:#33322e; --card:#1f1e1c; --accent:#8fb4dc; --code:#2a2926; }
* { box-sizing: border-box; }
body { margin:0; background:var(--bg); color:var(--fg); font:15px/1.55 system-ui, -apple-system, "Segoe UI", sans-serif; }
main { max-width: 1040px; margin: 0 auto; padding: 32px 16px 64px; }
h1 { font-size: 28px; margin: 0 0 4px; } h2 { font-size: 19px; margin: 40px 0 8px; border-bottom: 1px solid var(--line); padding-bottom: 6px; }
p { margin: 6px 0 12px; } .muted { color: var(--muted); }
code { background: var(--code); padding: 1px 5px; border-radius: 4px; font-size: 13px; overflow-wrap: anywhere; }
.wrap { overflow-x: auto; } table { border-collapse: collapse; width: 100%; background: var(--card); }
th, td { text-align: left; vertical-align: top; padding: 8px 10px; border-bottom: 1px solid var(--line); }
th { font-size: 13px; color: var(--muted); font-weight: 600; }
svg { width: 100%; height: auto; } .box { fill: var(--card); stroke: var(--accent); stroke-width: 1.5; }
.label { fill: var(--fg); font-size: 12.5px; } .arrow { stroke: var(--accent); stroke-width: 2; fill: none; }
ul { padding-left: 20px; }
</style>
</head>
<body>
<main>
<h1>${esc(project)}: how this repository works</h1>
<p class="muted">repo-blueprint ${esc(manifest.version ?? '?')}${manifest.overlays?.length ? `, overlays ${esc(manifest.overlays.join(', '))}` : ''}. Generated from <code>tooling/gates.yaml</code>, <code>.agents/</code> and <code>AGENTS.md</code> by <code>tooling/scripts/render-overview.mjs</code>.</p>

<h2>From idea to release</h2>
${svg}
<p>Work starts as a ticket or a proposal, becomes a phase with a plan, and is delivered one chunk at a time: one agent session, one commit. Hooks run checks on every edit and at the end of every turn; git hooks run them on commit and push; the full CI runs locally in a container and on self-hosted runners. Choices only the owner can make go to <code>project/review-queue.md</code>, work that needs owner authority to <code>project/handoffs/</code>.</p>

<h2>Quality gates by stage</h2>
${table(['Stage', 'When', 'Checks'], stageRows)}

<h2>Checks</h2>
${table(['Check', 'Command', 'Notes'], checkRows)}

<h2>Agents</h2>
<p>Instructions live in <code>AGENTS.md</code> (Claude Code reads it through <code>CLAUDE.md</code>). Roles and skills live in <code>.agents/</code>; Codex reads the skills there directly, Claude Code gets generated copies in <code>.claude/</code>.</p>
${table(['Tier', 'Claude Code model', 'Codex model and effort'], tierRows)}
<h3>Roles</h3>
${table(['Role', 'Tier', 'Use'], roleRows)}
<h3>Skills</h3>
${table(['Skill', 'Use'], skillRows)}
<h3>Hooks (Claude Code and Codex)</h3>
${table(['Hook', 'When', 'What'], hookRows)}

<h2>Where things go</h2>
${table(['Folder', 'Purpose'], folderRows)}
<p>Names follow the naming standard: English kebab-case, dates first, the folder carries the type (gate <code>names</code>).</p>

<h2>Hard stops</h2>
${stops.length ? `<ul>${stops.join('')}</ul>` : '<p class="muted">No hard stops section in AGENTS.md.</p>'}

<h2>CI</h2>
<p>Full CI on the developer machine: <code>node tooling/scripts/ci-local.mjs</code> (the <code>ci</code> stage in a container with every scanner). ${workflows.length ? `Workflows: ${workflows.map((w) => `<code>${esc(w)}</code>`).join(' ')}.` : 'No hosted workflows.'}</p>
</main>
</body>
</html>
`;

if (process.argv.includes('--check')) {
  const current = read(OUT).replace(/\r\n/g, '\n');
  report('overview', current === html ? [] : [`${OUT} is out of date; run node tooling/scripts/render-overview.mjs and commit it`], { okMessage: `${OUT} is current` });
} else {
  const changed = read(OUT).replace(/\r\n/g, '\n') !== html;
  if (changed) {
    mkdirSync(dirname(join(root, OUT)), { recursive: true });
    writeFileSync(join(root, OUT), html);
  }
  // --stage (pre-commit): put the regenerated page into the commit that changed its sources.
  if (process.argv.includes('--stage')) execFileSync('git', ['add', '--', OUT], { cwd: root });
  console.log(changed ? `wrote ${OUT}` : `✓ overview: ${OUT} is current`);
}
