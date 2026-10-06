#!/usr/bin/env node
// SessionEnd (Claude and Codex): appends one line of session metrics to project/metrics/sessions.jsonl.
// Tokens come from the agent's own transcript and are recorded only when they can be counted exactly;
// otherwise the field says "unavailable" (never an estimate). Commits are those made since the session
// started (recorded by session-start.mjs). The line is committed with the next commit.
import { execFileSync } from 'node:child_process';
import { appendFileSync, existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
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

const sessionId = String(event.session_id ?? '').replace(/[^A-Za-z0-9_-]/g, '');
const startFile = join(root, '.local/sessions', `${sessionId}.json`);
const start = sessionId && existsSync(startFile) ? JSON.parse(readFileSync(startFile, 'utf8')) : {};

/** Exact token totals from a Claude or Codex transcript, or null when they cannot be counted. */
export function tokensFromTranscript(path) {
  if (!path || !existsSync(path)) return null;
  const lines = readFileSync(path, 'utf8').split('\n').filter(Boolean);
  let codexTotal = null;
  const claude = new Map();
  const models = new Set();
  for (const line of lines) {
    let entry;
    try {
      entry = JSON.parse(line);
    } catch {
      continue;
    }
    // Codex rollout: token_count events carry running totals; the last one is the session total.
    const info = entry?.payload?.type === 'token_count' ? entry.payload.info : null;
    if (info?.total_token_usage) codexTotal = info.total_token_usage;
    if (entry?.payload?.model) models.add(entry.payload.model);
    // Claude transcript: assistant messages carry usage; streamed chunks repeat the same message id.
    const message = entry?.type === 'assistant' ? entry.message : null;
    if (message?.usage) {
      claude.set(message.id ?? `${claude.size}`, message.usage);
      if (message.model) models.add(message.model);
    }
  }
  if (codexTotal) {
    return { models: [...models], tokens: { input: codexTotal.input_tokens ?? 0, cached_input: codexTotal.cached_input_tokens ?? 0, output: codexTotal.output_tokens ?? 0, reasoning: codexTotal.reasoning_output_tokens ?? 0, total: codexTotal.total_tokens ?? null } };
  }
  if (claude.size) {
    const sum = (key) => [...claude.values()].reduce((total, usage) => total + (usage[key] ?? 0), 0);
    return { models: [...models], tokens: { input: sum('input_tokens'), cache_write: sum('cache_creation_input_tokens'), cache_read: sum('cache_read_input_tokens'), output: sum('output_tokens') } };
  }
  return null;
}

const usage = tokensFromTranscript(event.transcript_path);
const commits = start.head ? git(['rev-list', '--reverse', `${start.head}..HEAD`]).split('\n').filter(Boolean) : [];
const refs = commits.length ? [...new Set(git(['log', '--format=%(trailers:key=Refs,key=Closes,valueonly)', `${start.head}..HEAD`]).split(/[\s,]+/).filter(Boolean))] : [];
const filesChanged = start.head ? git(['diff', '--name-only', start.head]).split('\n').filter(Boolean).length : null;
const lastGatePath = join(root, '.local/gate-last.json');
const lastGate = existsSync(lastGatePath) ? JSON.parse(readFileSync(lastGatePath, 'utf8')) : null;
const ended = new Date();

const record = {
  session: sessionId || null,
  agent: start.agent ?? agentName(event),
  models: usage?.models?.length ? usage.models : [event.model ?? start.model].filter(Boolean),
  started: start.started ?? null,
  ended: ended.toISOString(),
  minutes: start.started ? Math.round((ended - new Date(start.started)) / 60_000) : null,
  branch: start.branch ?? (git(['branch', '--show-current']) || null),
  tokens: usage?.tokens ?? 'unavailable',
  commits: commits.map((hash) => hash.slice(0, 10)),
  tickets: refs,
  files_changed: filesChanged,
  last_gate: lastGate && start.started && lastGate.at >= start.started ? { stage: lastGate.stage, ok: lastGate.ok } : null,
  reason: event.reason ?? null
};

mkdirSync(join(root, 'project/metrics'), { recursive: true });
appendFileSync(join(root, 'project/metrics/sessions.jsonl'), `${JSON.stringify(record)}\n`);
if (existsSync(startFile)) rmSync(startFile);
