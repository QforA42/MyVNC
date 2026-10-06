#!/usr/bin/env node
// SessionStart (Claude and Codex). Plain stdout becomes context for the agent, so every session starts
// with the current status, open owner decisions and the workflow pointer, without reading files first.
// It also records the session start (time, HEAD) in .local/sessions/ for the session metrics.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { agentName, readEvent, repoRoot } from './lib.mjs';

const event = readEvent();
const root = repoRoot(event);
const git = (args) => {
  try {
    return execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
  } catch {
    return '';
  }
};

const sessionId = String(event.session_id ?? `local-${Date.now()}`).replace(/[^A-Za-z0-9_-]/g, '');
const sessionsDir = join(root, '.local/sessions');
mkdirSync(sessionsDir, { recursive: true });
const file = join(sessionsDir, `${sessionId}.json`);
if (!existsSync(file) || event.source === 'startup') {
  writeFileSync(file, JSON.stringify({ session: sessionId, agent: agentName(event), model: event.model ?? null, started: new Date().toISOString(), head: git(['rev-parse', 'HEAD']) || null, branch: git(['branch', '--show-current']) || null }));
}

// The session-end hook appends to project/metrics/sessions.jsonl after the last commit of a session.
// Stage those lines now so they travel with the next commit instead of staying uncommitted
// (.gitattributes merges the file with merge=union, so branches never conflict on it).
const metrics = 'project/metrics/sessions.jsonl';
if (git(['status', '--porcelain', '--', metrics])) git(['add', '--', metrics]);

const read = (path) => (existsSync(join(root, path)) ? readFileSync(join(root, path), 'utf8') : '');
const strip = (text) => text.replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n/, '').trim();
const status = strip(read('project/STATUS.md')).split('\n').slice(0, 80).join('\n');
const openDecisions = strip(read('project/review-queue.md')).split('\n').filter((line) => /^\|\s*\d+/.test(line) && /\|\s*open\s*\|/i.test(line));
const dirty = git(['status', '--porcelain']).split('\n').filter((line) => line && !line.endsWith(metrics)).length;

// One line when the blueprint checkout next to this repository is newer than the applied version.
let blueprint = '';
if (existsSync(join(root, 'tooling/scripts/blueprint-status.mjs'))) {
  try {
    blueprint = execFileSync('node', ['tooling/scripts/blueprint-status.mjs', '--quiet'], { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], timeout: 5000 }).trim();
  } catch {
    blueprint = '';
  }
}

// Once per clone: tools and dependencies the local gates need but this machine lacks.
let doctor = '';
if (existsSync(join(root, 'tooling/scripts/doctor.mjs'))) {
  try {
    doctor = execFileSync('node', ['tooling/scripts/doctor.mjs', '--once', '--quiet'], { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], timeout: 5000 }).trim();
  } catch {
    doctor = '';
  }
}

const lines = [
  `Repository: ${root.split(/[\\/]/).pop()} on branch ${git(['branch', '--show-current']) || '(detached)'}${dirty ? `, ${dirty} uncommitted change(s) you did not make in this session: preserve them` : ''}.`,
  'Workflow: project/agent-workflow.md. Gates: node tooling/scripts/gate.mjs <quick|pre-push|verify>. Hooks check edits and the end of each turn.',
  openDecisions.length ? `Open owner decisions in project/review-queue.md: ${openDecisions.length}. Do not decide them yourself.` : 'No open owner decisions.',
  ...(blueprint ? [`${blueprint} Tell the owner; do not update unasked.`] : []),
  ...(doctor ? [`${doctor}\nSuggest these installs to the owner once, in your first reply; install only when the owner asks (skill setup-machine).`] : []),
  '',
  '--- project/STATUS.md ---',
  status || '(missing: create project/STATUS.md)'
];
process.stdout.write(`${lines.join('\n')}\n`);
