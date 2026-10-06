#!/usr/bin/env node
// Runtime proof for "done": builds and starts the Compose stack under a separate project name, waits for
// health checks, requests the smoke URLs from tooling/config/verify.json and always tears the stack down.
// The stack is isolated from a running development stack: no fixed container names and every published
// port moved to a free host port (smoke URLs follow). Set "isolatePorts": false to keep the ports.
// Usage: node tooling/scripts/compose-smoke.mjs [--keep]
//   tooling/config/verify.json: { "composeFile": "deploy/compose/compose.yaml", "smoke": ["http://localhost:8080/health"] }
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { basename, join } from 'node:path';
import { remapUrl, smokeOverride } from './lib/compose.mjs';
import { root } from './lib/repo.mjs';

const configPath = join(root, 'tooling/config/verify.json');
const config = existsSync(configPath) ? JSON.parse(readFileSync(configPath, 'utf8')) : {};
const composeFile = config.composeFile ?? 'deploy/compose/compose.yaml';
const project = `${basename(root).toLowerCase().replace(/[^a-z0-9-]/g, '')}-verify`;
const files = ['-f', composeFile];
const compose = (...args) => spawnSync('docker', ['compose', ...files, '-p', project, ...args], { cwd: root, stdio: 'inherit' });

/** Distinct free TCP ports: all servers stay open until every port is known. */
async function freePorts(count) {
  const servers = await Promise.all(Array.from({ length: count }, () => new Promise((resolve, reject) => {
    const server = createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => resolve(server));
  })));
  const ports = servers.map((server) => server.address().port);
  await Promise.all(servers.map((server) => new Promise((resolve) => server.close(resolve))));
  return ports;
}

let smoke = config.smoke ?? [];
if (config.isolatePorts !== false) {
  const resolved = spawnSync('docker', ['compose', ...files, '-p', project, 'config', '--format', 'json'], { cwd: root, encoding: 'utf8' });
  if (resolved.status === 0) {
    const services = JSON.parse(resolved.stdout).services ?? {};
    const published = new Set(Object.values(services).flatMap((service) => (service.ports ?? []).filter((port) => port.published).map((port) => Number(port.published))));
    const pool = await freePorts(published.size);
    const { yaml, portMap } = smokeOverride(services, () => pool.shift());
    const override = join(root, '.local/compose-smoke.override.yaml');
    mkdirSync(join(root, '.local'), { recursive: true });
    writeFileSync(override, yaml);
    files.push('-f', override);
    smoke = smoke.map((url) => remapUrl(url, portMap));
    if (portMap.size) console.log(`Isolated ports: ${[...portMap].map(([from, to]) => `${from}→${to}`).join(', ')}`);
  }
}

let failed = false;
try {
  const up = compose('up', '-d', '--build', '--wait', '--wait-timeout', String(config.waitSeconds ?? 300));
  if (up.status !== 0) throw new Error('docker compose up --wait failed (a service did not become healthy)');
  for (const url of smoke) {
    const response = await fetch(url, { signal: AbortSignal.timeout(15_000) }).catch((error) => ({ ok: false, status: error.message }));
    console.log(`${response.ok ? '✓' : '✗'} smoke ${url} → ${response.status}`);
    if (!response.ok) failed = true;
  }
} catch (error) {
  console.error(`✗ compose-smoke: ${error.message}`);
  compose('ps');
  compose('logs', '--tail', '60');
  failed = true;
} finally {
  if (!process.argv.includes('--keep')) compose('down', '-v', '--remove-orphans');
}
if (failed) process.exitCode = 1;
else console.log('✓ compose-smoke: stack healthy and smoke URLs answered');
