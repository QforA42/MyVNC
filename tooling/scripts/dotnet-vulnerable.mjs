#!/usr/bin/env node
// `dotnet list package --vulnerable` exits 0 even when it finds something, so this wrapper reads its
// JSON output and fails on High/Critical packages that are not accepted in accepted-risks.json.
// Usage: node tooling/scripts/dotnet-vulnerable.mjs <solution-or-folder>
import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { readJson, root, today } from './lib/repo.mjs';

const target = process.argv[2] ?? '.';
const failOn = (process.env.DOTNET_FAIL_SEVERITY ?? 'High,Critical').split(',');
const output = execFileSync('dotnet', ['list', target, 'package', '--vulnerable', '--include-transitive', '--format', 'json'], { cwd: root, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
const result = JSON.parse(output);
const risks = existsSync(join(root, 'tooling/security/accepted-risks.json')) ? readJson('tooling/security/accepted-risks.json').risks ?? [] : [];
const accepted = (url, name) => risks.some((risk) => (risk.scope ?? '') === 'nuget' && (risk.id === url || risk.package === name) && risk.reviewBy >= today());

const errors = [];
for (const project of result.projects ?? []) {
  for (const framework of project.frameworks ?? []) {
    for (const pkg of [...(framework.topLevelPackages ?? []), ...(framework.transitivePackages ?? [])]) {
      for (const vulnerability of pkg.vulnerabilities ?? []) {
        if (failOn.includes(vulnerability.severity) && !accepted(vulnerability.advisoryurl, pkg.id)) {
          errors.push(`${project.path}: ${pkg.id} ${pkg.resolvedVersion} ${vulnerability.severity} ${vulnerability.advisoryurl}`);
        }
      }
    }
  }
}
if (errors.length) {
  console.error(`✗ dotnet-vulnerable: ${errors.length} package vulnerability(ies)`);
  for (const error of errors) console.error(`  ${error}`);
  process.exitCode = 1;
} else {
  console.log('✓ dotnet-vulnerable: nothing at ' + failOn.join('/'));
}
