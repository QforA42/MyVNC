#!/usr/bin/env node
// Validates YAML front-matter in docs/ and project/ against tooling/schemas/frontmatter.
// Usage: node tooling/scripts/check-frontmatter.mjs [--files a.md b.md] [--fix-updated]
//   --files        check only these files (pre-commit passes the staged ones)
//   --fix-updated  set `updated:` to today in the given files before checking
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { readFrontMatter } from './lib/yaml.mjs';
import { mergeObjectSchemas, validate } from './lib/schema.mjs';
import { listFiles, readJson, report, root, today } from './lib/repo.mjs';

const args = process.argv.slice(2);
const filesIndex = args.indexOf('--files');
const explicit = filesIndex >= 0 ? args.slice(filesIndex + 1).filter((arg) => !arg.startsWith('--')) : null;
const fixUpdated = args.includes('--fix-updated');

const config = readJson('tooling/config/frontmatter.json');
const base = readJson(`${config.schemasDir}/base.schema.json`);
const typeSchemas = new Map();
const schemaFor = (type) => {
  const name = config.types[type];
  if (!name) return null;
  if (!typeSchemas.has(name)) typeSchemas.set(name, mergeObjectSchemas(base, readJson(`${config.schemasDir}/${name}.schema.json`)));
  return typeSchemas.get(name);
};

const inScope = (file) =>
  file.endsWith('.md') &&
  config.include.some((prefix) => file.startsWith(prefix)) &&
  !config.exclude.some((pattern) =>
    pattern.startsWith('**/')
      ? `/${file}`.includes(`/${pattern.slice(3)}`)
      : file === pattern || file.startsWith(pattern) || file.endsWith(`/${pattern}`));
// Only these language codes mark a translation (<name>.<lang>.md); other dotted names such as
// core.if.md are ordinary documents.
const translationLangs = config.translationLangs ?? ['sv'];

const files = (explicit ?? listFiles()).map((file) => file.replaceAll('\\', '/')).filter(inScope).filter((file) => existsSync(join(root, file)));
const errors = [];
const ids = new Map();
const translations = [];

for (const file of files) {
  let text = readFileSync(join(root, file), 'utf8');
  if (fixUpdated && /^---\r?\n[\s\S]*?^updated:/m.test(text)) {
    const fixed = text.replace(/^(updated:\s*).*$/m, `$1${today()}`);
    if (fixed !== text) writeFileSync(join(root, file), fixed);
    text = fixed;
  }
  let parsed;
  try {
    parsed = readFrontMatter(text);
  } catch (error) {
    errors.push(`${file}: front-matter is not valid YAML (${error.message})`);
    continue;
  }
  if (!parsed.hasFrontMatter) {
    errors.push(`${file}: missing front-matter (start the file with a --- block; see tooling/schemas/frontmatter)`);
    continue;
  }
  const data = parsed.data ?? {};
  const schema = schemaFor(data.type);
  if (!schema) {
    errors.push(`${file}: unknown type "${data.type}" (allowed: ${Object.keys(config.types).join(', ')})`);
    continue;
  }
  for (const error of validate(data, schema)) errors.push(`${file}: ${error.path} ${error.message}`);
  if (config.idPrefix && typeof data.id === 'string' && !data.id.startsWith(`${config.idPrefix}-`)) {
    errors.push(`${file}: id must start with "${config.idPrefix}-"`);
  }
  if (typeof data.id === 'string') {
    if (ids.has(data.id)) errors.push(`${file}: id ${data.id} is also used by ${ids.get(data.id)}`);
    else ids.set(data.id, file);
  }
  if (data.created && data.updated && data.updated < data.created) errors.push(`${file}: updated is before created`);
  // Translations: docs are written in primaryLang (en); a translation sits next to its source as
  // <name>.<lang>.md, declares lang and translation_of, and uses the id "<source id>-<lang>".
  const primary = config.primaryLang ?? 'en';
  const suffix = file.match(/\.([a-z]{2})\.md$/)?.[1];
  if (suffix && suffix !== primary && translationLangs.includes(suffix)) {
    if (data.lang !== suffix) errors.push(`${file}: a .${suffix}.md translation needs lang: ${suffix}`);
    if (!data.translation_of) errors.push(`${file}: a translation needs translation_of: <source document id>`);
    else {
      translations.push({ file, source: data.translation_of });
      if (data.id !== `${data.translation_of}-${suffix}`) errors.push(`${file}: translation id must be ${data.translation_of}-${suffix}`);
    }
  } else {
    if (data.lang && data.lang !== primary) errors.push(`${file}: documents are written in ${primary}; put a translation in <name>.${data.lang}.md`);
    if (data.translation_of) errors.push(`${file}: translation_of is only allowed in <name>.<lang>.md translations`);
  }
  const title = parsed.body.match(/^#\s+(.+)$/m)?.[1]?.trim();
  if (!title) errors.push(`${file}: body has no "# " heading`);
}

// A translation must point at an existing source (checked when the whole tree is scanned).
if (!explicit) {
  for (const { file, source } of translations) if (!ids.has(source)) errors.push(`${file}: translation_of ${source} matches no document`);
}

report('front-matter', errors, { okMessage: `${files.length} document(s)` });
