#!/usr/bin/env node
// Container CVE gate backed by the Zot registry's built-in Trivy scanning.
// Queries Zot's search API (CVEListForImage) per image and fails on findings at or above the
// threshold that are not accepted in tooling/security/accepted-risks.json. Expired acceptances fail too.
//
// Usage:
//   node tooling/scripts/zot-cve.mjs --images-from-env          IMAGES="zot.lan.vb210.se/app/api@sha256:… …"
//   node tooling/scripts/zot-cve.mjs --images-from-release      images: of the newest note in project/releases/
//   node tooling/scripts/zot-cve.mjs --images-from-release --all images: of every released note (weekly re-scan)
//   node tooling/scripts/zot-cve.mjs <image> [<image> …]
// Env: ZOT_URL (default https://zot.lan.vb210.se), ZOT_FAIL_SEVERITY (default HIGH,CRITICAL),
//      ZOT_TOKEN (optional bearer token), ZOT_OUT (optional path for a JSON report).
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { readFrontMatter } from './lib/yaml.mjs';
import { readJson, root, today } from './lib/repo.mjs';

const ZOT_URL = (process.env.ZOT_URL ?? 'https://zot.lan.vb210.se').replace(/\/$/, '');
const FAIL = (process.env.ZOT_FAIL_SEVERITY ?? 'HIGH,CRITICAL').split(',').map((value) => value.trim().toUpperCase());
const args = process.argv.slice(2);

function releaseImages(all) {
  const dir = join(root, 'project/releases');
  if (!existsSync(dir)) return [];
  const notes = readdirSync(dir)
    .filter((name) => name.endsWith('.md'))
    .map((name) => readFrontMatter(readFileSync(join(dir, name), 'utf8')).data)
    .filter((data) => data?.type === 'release' && Array.isArray(data.images));
  const byVersion = (a, b) => b.version.localeCompare(a.version, undefined, { numeric: true });
  if (all) return notes.filter((note) => note.status === 'released').flatMap((note) => note.images);
  return notes.sort(byVersion)[0]?.images ?? [];
}

const images = args.includes('--images-from-env')
  ? (process.env.IMAGES ?? '').split(/[\s,]+/).filter(Boolean)
  : args.includes('--images-from-release')
    ? releaseImages(args.includes('--all'))
    : args.filter((arg) => !arg.startsWith('--'));

if (!images.length) {
  console.log('○ zot-cve: no images to check (set IMAGES, or images: in the release note)');
  process.exit(0);
}

/** "zot.lan.vb210.se/flowable/api@sha256:…" -> "flowable/api@sha256:…" (Zot expects repo:tag or repo@digest). */
export function zotImageRef(image) {
  const [first, ...rest] = image.split('/');
  return rest.length && (first.includes('.') || first.includes(':')) ? rest.join('/') : image;
}

async function cveList(image) {
  const query = `{ CVEListForImage(image: ${JSON.stringify(zotImageRef(image))}) { Tag CVEList { Id Severity Title PackageList { Name InstalledVersion FixedVersion } } } }`;
  const headers = process.env.ZOT_TOKEN ? { Authorization: `Bearer ${process.env.ZOT_TOKEN}` } : {};
  for (let attempt = 1; attempt <= 5; attempt++) {
    const response = await fetch(`${ZOT_URL}/v2/_zot/ext/search?query=${encodeURIComponent(query)}`, { headers, signal: AbortSignal.timeout(60_000) });
    if (!response.ok) throw new Error(`Zot search returned HTTP ${response.status} for ${image}`);
    const body = await response.json();
    const list = body?.data?.CVEListForImage?.CVEList;
    if (Array.isArray(list)) return list;
    const message = (body?.errors ?? []).map((error) => error.message).join('; ');
    // Zot scans on first request; a fresh push can answer "scan in progress" for a while.
    if (attempt < 5 && /progress|not.*ready|scan/i.test(message)) {
      await new Promise((resolve) => setTimeout(resolve, attempt * 5_000));
      continue;
    }
    throw new Error(`Zot returned no CVE list for ${image}${message ? `: ${message}` : ''}`);
  }
  throw new Error(`Zot did not finish scanning ${image}`);
}

const risks = existsSync(join(root, 'tooling/security/accepted-risks.json')) ? readJson('tooling/security/accepted-risks.json').risks ?? [] : [];
const errors = [];
for (const risk of risks) {
  if (!risk.reviewBy || risk.reviewBy < today()) errors.push(`accepted risk ${risk.id} (${risk.package}) expired on ${risk.reviewBy}; review it`);
}
const accepted = (id) => risks.some((risk) => risk.id === id && (risk.scope ?? 'image') === 'image' && risk.reviewBy >= today());

const report = [];
for (const image of images) {
  let list;
  try {
    list = await cveList(image);
  } catch (error) {
    errors.push(error.message);
    continue;
  }
  const counts = {};
  for (const cve of list) {
    const severity = String(cve.Severity ?? 'UNKNOWN').toUpperCase();
    counts[severity] = (counts[severity] ?? 0) + 1;
    const blocking = FAIL.includes(severity) && !accepted(cve.Id);
    const packages = (cve.PackageList ?? []).map((pkg) => `${pkg.Name} ${pkg.InstalledVersion}${pkg.FixedVersion ? ` → ${pkg.FixedVersion}` : ' (no fix)'}`).join(', ');
    report.push({ image, id: cve.Id, severity, packages, accepted: accepted(cve.Id), blocking });
    if (blocking) errors.push(`${image}: ${severity} ${cve.Id} ${packages}`);
  }
  const summary = Object.entries(counts).map(([severity, count]) => `${severity} ${count}`).join(', ') || 'no CVEs';
  console.log(`  ${image}: ${summary}`);
}

if (process.env.ZOT_OUT) writeFileSync(process.env.ZOT_OUT, `${JSON.stringify({ zot: ZOT_URL, checked: today(), images, findings: report }, null, 2)}\n`);
if (errors.length) {
  console.error(`✗ zot-cve: ${errors.length} blocking problem(s) at ${FAIL.join('/')}`);
  for (const error of errors) console.error(`  ${error}`);
  process.exitCode = 1;
} else {
  console.log(`✓ zot-cve: ${images.length} image(s), nothing blocking at ${FAIL.join('/')}`);
}
