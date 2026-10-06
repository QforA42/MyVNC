// Minimal JSON Schema validator for the subset the blueprint schemas use:
// type, enum, const, pattern, format (date), minLength, maxLength, minimum, maximum,
// required, properties, additionalProperties (boolean or schema), items, minItems, uniqueItems.
// Kept dependency-free so a repository can adopt the blueprint without installing anything.

const DATE = /^\d{4}-\d{2}-\d{2}$/;

function typeOf(value) {
  if (value === null) return 'null';
  if (Array.isArray(value)) return 'array';
  if (Number.isInteger(value)) return 'integer';
  return typeof value;
}

function matchesType(value, expected) {
  const actual = typeOf(value);
  const list = Array.isArray(expected) ? expected : [expected];
  return list.some((type) => type === actual || (type === 'number' && actual === 'integer'));
}

/** Returns a list of { path, message } errors; an empty list means valid. */
export function validate(value, schema, path = '') {
  const errors = [];
  const at = path || '(root)';
  if (schema.type && !matchesType(value, schema.type)) {
    errors.push({ path: at, message: `must be ${[].concat(schema.type).join(' or ')}, got ${typeOf(value)}` });
    return errors;
  }
  if (schema.const !== undefined && value !== schema.const) errors.push({ path: at, message: `must be ${JSON.stringify(schema.const)}` });
  if (schema.enum && !schema.enum.includes(value)) errors.push({ path: at, message: `must be one of: ${schema.enum.join(', ')} (got ${JSON.stringify(value)})` });
  if (typeof value === 'string') {
    if (schema.pattern && !new RegExp(schema.pattern, 'u').test(value)) errors.push({ path: at, message: `does not match ${schema.pattern}` });
    if (schema.format === 'date' && !DATE.test(value)) errors.push({ path: at, message: 'must be an ISO date (YYYY-MM-DD)' });
    if (schema.minLength !== undefined && value.length < schema.minLength) errors.push({ path: at, message: `must be at least ${schema.minLength} characters` });
    if (schema.maxLength !== undefined && value.length > schema.maxLength) errors.push({ path: at, message: `must be at most ${schema.maxLength} characters` });
  }
  if (typeof value === 'number') {
    if (schema.minimum !== undefined && value < schema.minimum) errors.push({ path: at, message: `must be >= ${schema.minimum}` });
    if (schema.maximum !== undefined && value > schema.maximum) errors.push({ path: at, message: `must be <= ${schema.maximum}` });
  }
  if (Array.isArray(value)) {
    if (schema.minItems !== undefined && value.length < schema.minItems) errors.push({ path: at, message: `must have at least ${schema.minItems} items` });
    if (schema.uniqueItems && new Set(value.map((item) => JSON.stringify(item))).size !== value.length) errors.push({ path: at, message: 'items must be unique' });
    if (schema.items) value.forEach((item, index) => errors.push(...validate(item, schema.items, `${path}[${index}]`)));
  }
  if (typeOf(value) === 'object') {
    for (const key of schema.required ?? []) {
      if (!Object.hasOwn(value, key)) errors.push({ path: at, message: `is missing required field "${key}"` });
    }
    const properties = schema.properties ?? {};
    for (const [key, child] of Object.entries(value)) {
      const childPath = path ? `${path}.${key}` : key;
      if (properties[key]) {
        errors.push(...validate(child, properties[key], childPath));
      } else if (schema.additionalProperties === false) {
        errors.push({ path: childPath, message: 'is not an allowed field' });
      } else if (typeof schema.additionalProperties === 'object') {
        errors.push(...validate(child, schema.additionalProperties, childPath));
      }
    }
  }
  return errors;
}

/** Merges a base object schema with a type-specific one (properties, required). */
export function mergeObjectSchemas(base, extra) {
  return {
    ...base,
    properties: { ...(base.properties ?? {}), ...(extra?.properties ?? {}) },
    required: [...new Set([...(base.required ?? []), ...(extra?.required ?? [])])],
    additionalProperties: extra?.additionalProperties ?? base.additionalProperties
  };
}
