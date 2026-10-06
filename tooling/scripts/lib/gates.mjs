// Loads tooling/gates.yaml with its variables and environment, shared by gate.mjs, ci-local.mjs and
// render-overview.mjs.
//   vars: values substituted as ${name} in a check's run, requires, match and cwd. Defaults below;
//         a repository overrides them in gates.yaml (for example a .NET solution in the root).
//   env:  environment for every check (for example DOTNET_ROLL_FORWARD); the caller's environment wins.
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { parseYaml } from './yaml.mjs';

export const DEFAULT_VARS = {
  dotnetDir: 'apps/backend', // folder with Directory.Build.props and global.json
  dotnetTarget: 'apps/backend', // folder or .sln/.slnx that dotnet build/test/format/restore get
  dotnetUnitTestArgs: '--filter "Category!=Integration"', // dotnet-test: no Docker-backed tests
  dotnetIntegrationTestArgs: '--filter "Category=Integration"', // dotnet-test-integration (verify)
  npmDir: '.', // folder with package.json and package-lock.json for the node-npm overlay
  npmDirs: null // folders the CI container and doctor install with npm ci; default [npmDir]
};

export function loadGates(root) {
  const path = join(root, 'tooling/gates.yaml');
  const config = existsSync(path) ? parseYaml(readFileSync(path, 'utf8')) : { stages: {}, checks: {} };
  const vars = { ...DEFAULT_VARS, ...(config.vars ?? {}) };
  vars.npmDirs = [vars.npmDirs ?? vars.npmDir].flat().map(String);
  const env = Object.fromEntries(Object.entries(config.env ?? {}).map(([key, value]) => [key, String(value)]));
  const expand = (value) => (typeof value === 'string' ? value.replace(/\$\{(\w+)\}/g, (match, name) => vars[name] ?? match) : value);
  for (const check of Object.values(config.checks ?? {})) {
    for (const key of ['run', 'match', 'cwd']) if (check[key] !== undefined) check[key] = expand(check[key]);
    if (check.requires) check.requires = check.requires.map(expand);
  }
  return { config, vars, env, expand };
}
