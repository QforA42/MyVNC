#!/usr/bin/env node
// Generates project/backlog.md from the ticket system (ADR 0005 in repo-blueprint): the tracker is
// the source of truth, the file is a read-only view for agents and for the DocSaga sync.
// Configure tooling/config/backlog.json:
//   { "source": "github", "repo": "QforA42/Flowable" }                       (uses the gh CLI)
//   { "source": "gitea", "repo": "vb210/flowable", "url": "https://gitea.lan" } (GITEA_TOKEN env)
// Optional: "priorityLabels": ["P1","P2","P3"], "groupLabels": ["phase","bug","enhancement"].
// Usage: node tooling/scripts/render-backlog.mjs [--check]   (--check fails if the file is stale)
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { readJson, root, today } from './lib/repo.mjs';

const config = readJson('tooling/config/backlog.json');
const projectId = (existsSync(join(root, 'tooling/blueprint.json')) && readJson('tooling/blueprint.json').values?.PREFIX) || 'PRJ';
const priorityLabels = config.priorityLabels ?? ['P1', 'P2', 'P3'];
const groupLabels = config.groupLabels ?? ['phase', 'bug', 'enhancement'];

async function fetchIssues() {
  if (config.source === 'github') {
    const issues = JSON.parse(execFileSync('gh', ['issue', 'list', '--repo', config.repo, '--state', 'open', '--limit', '1000', '--json', 'number,title,labels,assignees,updatedAt'], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }));
    return issues.map((issue) => ({ ref: `github:${config.repo}#${issue.number}`, url: `https://github.com/${config.repo}/issues/${issue.number}`, number: issue.number, title: issue.title, labels: issue.labels.map((label) => label.name), updated: issue.updatedAt.slice(0, 10) }));
  }
  if (config.source === 'gitea') {
    const token = process.env.GITEA_TOKEN;
    if (!token) throw new Error('GITEA_TOKEN is not set (read it from Infisical; never commit it)');
    const result = [];
    for (let page = 1; ; page++) {
      const response = await fetch(`${config.url.replace(/\/$/, '')}/api/v1/repos/${config.repo}/issues?state=open&type=issues&limit=50&page=${page}`, { headers: { Authorization: `token ${token}` } });
      if (!response.ok) throw new Error(`Gitea returned HTTP ${response.status}`);
      const batch = await response.json();
      result.push(...batch.map((issue) => ({ ref: `gitea:${config.repo}#${issue.number}`, url: issue.html_url, number: issue.number, title: issue.title, labels: issue.labels.map((label) => label.name), updated: issue.updated_at.slice(0, 10) })));
      if (batch.length < 50) break;
    }
    return result;
  }
  throw new Error(`Unsupported backlog source "${config.source}" (github or gitea; Ticketeer follows when it has a list API)`);
}

const issues = await fetchIssues();
const priority = (issue) => priorityLabels.find((label) => issue.labels.includes(label)) ?? '—';
const group = (issue) => groupLabels.find((label) => issue.labels.includes(label)) ?? 'other';
const escape = (text) => text.replaceAll('|', '\\|');
const sections = [...groupLabels, 'other']
  .map((name) => {
    const rows = issues.filter((issue) => group(issue) === name).sort((a, b) => priority(a).localeCompare(priority(b)) || a.number - b.number);
    if (!rows.length) return '';
    return [`## ${name} (${rows.length})`, '', '| Ticket | Title | Priority | Labels | Updated |', '|---|---|---|---|---|', ...rows.map((issue) => `| [${issue.ref}](${issue.url}) | ${escape(issue.title)} | ${priority(issue)} | ${issue.labels.filter((label) => label !== name && !priorityLabels.includes(label)).join(', ')} | ${issue.updated} |`), ''].join('\n');
  })
  .filter(Boolean);

const body = [`# Backlog`, '', `Generated from ${config.source} \`${config.repo}\` by \`tooling/scripts/render-backlog.mjs\`. Do not edit: change the tickets.`, '', `${issues.length} open ticket(s).`, '', ...sections].join('\n');
const path = join(root, 'project/backlog.md');
const previous = existsSync(path) ? readFileSync(path, 'utf8') : '';
const previousBody = previous.replace(/^---[\s\S]*?\n---\n/, '');
if (process.argv.includes('--check')) {
  if (previousBody.trim() !== body.trim()) {
    console.error('✗ backlog: project/backlog.md is stale; run node tooling/scripts/render-backlog.mjs');
    process.exitCode = 1;
  } else console.log('✓ backlog: up to date');
} else if (previousBody.trim() === body.trim()) {
  console.log('✓ backlog: unchanged');
} else {
  const frontMatter = ['---', `id: ${projectId}-REC-backlog`, 'type: backlog', 'title: Backlog', 'status: current', 'owner: tooling', `updated: ${today()}`, 'wiki:', '  slug: backlog', '  page_type: backlog', '  sync: auto', '---', ''].join('\n');
  writeFileSync(path, `${frontMatter}${body}\n`);
  console.log(`✓ backlog: wrote ${issues.length} ticket(s) to project/backlog.md`);
}
