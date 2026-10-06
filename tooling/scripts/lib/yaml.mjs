// Minimal YAML subset parser for front-matter and blueprint config files.
// Supports: block maps, nested maps by indentation, block sequences of scalars or maps,
// flow sequences ([a, b]) and flow maps ({ a: b }) of scalars, quoted strings, comments,
// null/true/false, integers and floats. Anchors, multi-line scalars and tags are not supported
// and raise an error, so an unsupported file fails loudly instead of parsing wrongly.

export class YamlError extends Error {
  constructor(message, line) {
    super(line ? `line ${line}: ${message}` : message);
    this.line = line;
  }
}

function stripComment(text) {
  let quote = null;
  for (let index = 0; index < text.length; index++) {
    const char = text[index];
    if (quote) {
      if (char === quote) quote = null;
    } else if (char === '"' || char === "'") {
      quote = char;
    } else if (char === '#' && (index === 0 || /\s/.test(text[index - 1]))) {
      return text.slice(0, index);
    }
  }
  return text;
}

function splitFlow(inner, lineNo) {
  const parts = [];
  let depth = 0;
  let quote = null;
  let current = '';
  for (const char of inner) {
    if (quote) {
      if (char === quote) quote = null;
      current += char;
      continue;
    }
    if (char === '"' || char === "'") quote = char;
    if (char === '[' || char === '{') depth++;
    if (char === ']' || char === '}') depth--;
    if (char === ',' && depth === 0) {
      parts.push(current.trim());
      current = '';
      continue;
    }
    current += char;
  }
  if (quote || depth !== 0) throw new YamlError('unbalanced flow collection', lineNo);
  if (current.trim()) parts.push(current.trim());
  return parts;
}

export function parseScalar(raw, lineNo) {
  const value = raw.trim();
  if (value === '' || value === '~' || value === 'null') return null;
  if (value === 'true') return true;
  if (value === 'false') return false;
  if (/^[|>]/.test(value)) throw new YamlError('block scalars (| or >) are not supported', lineNo);
  if (/^[&*!]/.test(value)) throw new YamlError('anchors, aliases and tags are not supported', lineNo);
  if (value.startsWith('"')) {
    if (!value.endsWith('"') || value.length < 2) throw new YamlError('unterminated double-quoted string', lineNo);
    return JSON.parse(value);
  }
  if (value.startsWith("'")) {
    if (!value.endsWith("'") || value.length < 2) throw new YamlError('unterminated single-quoted string', lineNo);
    return value.slice(1, -1).replaceAll("''", "'");
  }
  if (value.startsWith('[')) {
    if (!value.endsWith(']')) throw new YamlError('unterminated flow sequence', lineNo);
    return splitFlow(value.slice(1, -1), lineNo).map((part) => parseScalar(part, lineNo));
  }
  if (value.startsWith('{')) {
    if (!value.endsWith('}')) throw new YamlError('unterminated flow map', lineNo);
    const result = {};
    for (const part of splitFlow(value.slice(1, -1), lineNo)) {
      const colon = part.indexOf(':');
      if (colon < 0) throw new YamlError(`flow map entry without key: ${part}`, lineNo);
      result[part.slice(0, colon).trim()] = parseScalar(part.slice(colon + 1), lineNo);
    }
    return result;
  }
  if (/^-?\d+$/.test(value)) return Number.parseInt(value, 10);
  if (/^-?\d+\.\d+$/.test(value)) return Number.parseFloat(value);
  return value;
}

function tokenize(text) {
  const lines = [];
  text.split(/\r?\n/).forEach((rawLine, index) => {
    if (/\t/.test(rawLine.match(/^\s*/)[0])) throw new YamlError('tabs are not allowed for indentation', index + 1);
    const line = stripComment(rawLine).replace(/\s+$/, '');
    if (!line.trim()) return;
    lines.push({ indent: line.length - line.trimStart().length, text: line.trim(), lineNo: index + 1 });
  });
  return lines;
}

function keyValue(text, lineNo) {
  const match = text.match(/^("[^"]*"|'[^']*'|[^:]+?):(?:\s+(.*))?$/);
  if (!match) throw new YamlError(`expected "key: value", got: ${text}`, lineNo);
  const key = match[1].replace(/^["']|["']$/g, '');
  return { key, rest: match[2] ?? '' };
}

function parseBlock(lines, start, indent) {
  const first = lines[start];
  if (first.text.startsWith('- ') || first.text === '-') return parseSequence(lines, start, indent);
  return parseMap(lines, start, indent);
}

function parseMap(lines, start, indent) {
  const result = {};
  let index = start;
  while (index < lines.length && lines[index].indent === indent) {
    const { text, lineNo } = lines[index];
    if (text.startsWith('- ')) throw new YamlError('sequence item where a map key was expected', lineNo);
    const { key, rest } = keyValue(text, lineNo);
    if (Object.hasOwn(result, key)) throw new YamlError(`duplicate key "${key}"`, lineNo);
    index++;
    if (rest !== '') {
      result[key] = parseScalar(rest, lineNo);
    } else if (index < lines.length && lines[index].indent > indent) {
      [result[key], index] = parseBlock(lines, index, lines[index].indent);
    } else if (index < lines.length && lines[index].indent === indent && lines[index].text.startsWith('- ')) {
      [result[key], index] = parseSequence(lines, index, indent);
    } else {
      result[key] = null;
    }
  }
  if (index < lines.length && lines[index].indent > indent) throw new YamlError('unexpected indentation', lines[index].lineNo);
  return [result, index];
}

function parseSequence(lines, start, indent) {
  const result = [];
  let index = start;
  while (index < lines.length && lines[index].indent === indent && (lines[index].text.startsWith('- ') || lines[index].text === '-')) {
    const { text, lineNo } = lines[index];
    const itemText = text === '-' ? '' : text.slice(2).trim();
    index++;
    if (itemText === '') {
      if (index < lines.length && lines[index].indent > indent) {
        let item;
        [item, index] = parseBlock(lines, index, lines[index].indent);
        result.push(item);
      } else {
        result.push(null);
      }
    } else if (/^("[^"]*"|'[^']*'|[^\s:[{"'][^:]*?):(\s|$)/.test(itemText) && !itemText.startsWith('{')) {
      // "- key: value" starts a map whose further keys are indented to the item text column.
      const itemIndent = indent + 2;
      const synthetic = [{ indent: itemIndent, text: itemText, lineNo }];
      while (index < lines.length && lines[index].indent >= itemIndent) synthetic.push(lines[index++]);
      const [item] = parseMap(synthetic, 0, itemIndent);
      result.push(item);
    } else {
      result.push(parseScalar(itemText, lineNo));
    }
  }
  return [result, index];
}

export function parseYaml(text) {
  const lines = tokenize(text);
  if (!lines.length) return {};
  const [value, index] = parseBlock(lines, 0, lines[0].indent);
  if (index < lines.length) throw new YamlError('could not parse remaining content', lines[index].lineNo);
  return value;
}

/** Splits a Markdown document into { data, body, hasFrontMatter, bodyStartLine }. */
export function readFrontMatter(markdown) {
  const text = markdown.replace(/^﻿/, '');
  const match = text.match(/^---\r?\n([\s\S]*?)\r?\n---[ \t]*(?:\r?\n|$)/);
  if (!match) return { data: null, body: text, hasFrontMatter: false, bodyStartLine: 1 };
  const data = parseYaml(match[1]);
  const bodyStartLine = match[0].split(/\r?\n/).length;
  return { data, body: text.slice(match[0].length), hasFrontMatter: true, bodyStartLine };
}
