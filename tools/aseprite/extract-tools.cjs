const fs = require('node:fs');
const path = require('node:path');

// Extract schemas from the Aseprite tool registration modules without loading its server.
class Shape {
  constructor(schema) { this.schema = schema; this.required = true; }
  optional() { this.required = false; return this; }
  describe(description) { this.schema.description = description; return this; }
  min(value) { this.schema[this.schema.type === 'number' ? 'minimum' : 'minLength'] = value; return this; }
  max(value) { this.schema[this.schema.type === 'number' ? 'maximum' : 'maxLength'] = value; return this; }
}
function object(properties) {
  const required = Object.entries(properties).filter(([, v]) => v.required).map(([k]) => k);
  return new Shape({ type: 'object', properties: Object.fromEntries(Object.entries(properties).map(([k,v]) => [k,v.schema])), required, additionalProperties: false });
}
const z = {
  string: () => new Shape({ type: 'string' }), number: () => new Shape({ type: 'number' }),
  boolean: () => new Shape({ type: 'boolean' }), object,
  array: item => new Shape({ type: 'array', items: item.schema }),
  enum: values => new Shape({ type: 'string', enum: values })
};
const tools = [];
const server = { tool(name, description, properties) { tools.push({ name, description, schema: object(properties).schema }); } };
for (const file of fs.readdirSync(process.argv[2]).filter(f => f.endsWith('.js')).sort()) {
  const source = fs.readFileSync(path.join(process.argv[2], file), 'utf8');
  const match = source.match(/export function (register\w+)\(/);
  if (!match) throw new Error(`Missing registration: ${file}`);
  const code = source.replace(/^import .*;\r?\n/gm, '').replace('export function', 'function');
  new Function('z', 'server', 'aseprite', `${code}\n${match[1]}(server, aseprite);`)(z, server, {});
}
if (new Set(tools.map(t => t.name)).size !== tools.length) throw new Error('Duplicate tools');
fs.writeFileSync(process.argv[3], JSON.stringify(tools, null, 2) + '\n');
console.log(`Extracted ${tools.length} tool schemas`);
