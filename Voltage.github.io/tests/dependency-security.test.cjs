'use strict';

const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const path = require('node:path');
const test = require('node:test');
const braces = require('braces');
const semver = require('semver');

test('patched registry dependencies meet the advisory fix versions', () => {
  const minimums = {
    'brace-expansion': '1.1.21',
    compression: '1.8.2',
    dompurify: '3.4.16',
    'fast-uri': '3.1.8',
    'http-cache-semantics': '4.2.1',
    joi: '17.13.8',
    katex: '0.18.2',
    'postcss-selector-parser': '7.1.6',
    'proxy-addr': '2.0.8',
    'serialize-javascript': '7.1.2',
    'shell-quote': '1.11.0',
    'source-map-js': '1.2.2',
    tinypool: '2.1.2',
  };
  const lock = require('../package-lock.json');
  for (const [name, minimum] of Object.entries(minimums)) {
    const copies = Object.entries(lock.packages).filter(([location]) =>
      location.endsWith(`/node_modules/${name}`) || location === `node_modules/${name}`);
    assert.ok(copies.length, `Missing dependency: ${name}`);
    for (const [location, pkg] of copies) {
      assert.ok(semver.gte(pkg.version, minimum), `${location}: ${pkg.version} < ${minimum}`);
    }
  }
  assert.equal(require('braces/package.json').version, '3.0.3+voltage.1');
  const forkPath = path.resolve(__dirname, '../vendor/braces/index.js');
  assert.equal(require.resolve('braces'), forkPath);
  for (const consumer of ['chokidar', 'micromatch']) {
    assert.equal(require.resolve('braces', { paths: [path.dirname(require.resolve(consumer))] }), forkPath);
  }
  assert.equal(Object.entries(lock.packages).filter(([location]) => location.endsWith('/node_modules/braces') || location === 'node_modules/braces').length, 1);
});

test('ordinary brace patterns, ranges and glob consumers retain their behavior', () => {
  assert.deepEqual(braces('assets/*.{png,jpg}'), ['assets/*.(png|jpg)']);
  assert.deepEqual(braces.expand('a/{b,{c,d}}/e'), ['a/b/e', 'a/c/e', 'a/d/e']);
  assert.deepEqual(braces.expand('file-{01..03}'), ['file-01', 'file-02', 'file-03']);
  assert.deepEqual(braces.expand('{a,b}{1,2}'), ['a1', 'a2', 'b1', 'b2']);
  assert.deepEqual(braces.expand('{a,a,b}', { nodupes: true }), ['a', 'b']);
  assert.deepEqual(braces.expand('\\{a,b\\}'), ['{a,b}']);
  assert.deepEqual(braces.expand('"{a,b}"'), ['{a,b}']);
  assert.deepEqual(braces.expand('[{a,b}]'), ['[{a,b}]']);
  assert.equal(braces.stringify(braces.parse('docs/{editor,assets}/**/*.md')), 'docs/{editor,assets}/**/*.md');
  assert.deepEqual(require('micromatch')(['a.md', 'b.mdx', 'c.png'], '*.{md,mdx}'), ['a.md', 'b.mdx']);
  assert.equal(require('anymatch')('docs/**/*.{md,mdx}', 'docs/editor/index.md'), true);
});

test('reported deep inputs are rejected promptly even with a constrained stack', () => {
  const script = `
    const assert = require('node:assert/strict');
    const braces = require(${JSON.stringify(require.resolve('braces'))});
    for (const [open, close, depth] of [['{', '}', 4000], ['(', ')', 4000], ['{(', ')}', 2000]]) {
      const pattern = open.repeat(depth) + 'a,b' + close.repeat(depth);
      for (const method of [braces, braces.parse, braces.compile, braces.expand, braces.stringify]) {
        assert.throws(() => method(pattern), error => error instanceof SyntaxError && /maximum depth/.test(error.message));
      }
    }
  `;
  const result = spawnSync(process.execPath, ['--stack-size=512', '-e', script], { timeout: 5000, encoding: 'utf8' });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stderr);
});

test('deep or cyclic caller-provided ASTs cannot bypass the parser limit', () => {
  const script = `
    const assert = require('node:assert/strict');
    const braces = require(${JSON.stringify(require.resolve('braces'))});
    const deep = () => {
      const root = { type: 'root', nodes: [] };
      let parent = root;
      for (let i = 0; i < 2000; i++) {
        const child = { type: 'paren', nodes: [], parent };
        parent.nodes.push(child);
        parent = child;
      }
      parent.nodes.push({ type: 'text', value: 'x' });
      return root;
    };
    for (const method of [braces.compile, braces.expand, braces.stringify]) {
      assert.throws(() => method(deep()), SyntaxError);
      const cycle = { type: 'root', nodes: [] };
      cycle.nodes.push(cycle);
      assert.throws(() => method(cycle), SyntaxError);
    }
    const parentCycle = { type: 'paren', nodes: [] };
    parentCycle.parent = parentCycle;
    assert.throws(() => braces.expand(parentCycle), SyntaxError);
  `;
  const result = spawnSync(process.execPath, ['--stack-size=512', '-e', script], { timeout: 5000, encoding: 'utf8' });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stderr);
});

test('bounded nesting and literal delimiters remain usable', () => {
  const nested = '{'.repeat(64) + 'x' + '}'.repeat(64);
  assert.equal(braces.stringify(nested), nested);
  assert.doesNotThrow(() => braces.compile(nested));
  assert.doesNotThrow(() => braces.expand(nested));
  assert.throws(() => braces.compile('{'.repeat(65) + 'x' + '}'.repeat(65)), SyntaxError);
  const quoted = '"' + '{'.repeat(2000) + '"';
  assert.equal(braces.stringify(quoted), '{'.repeat(2000));
  assert.equal(braces.stringify('[' + '{'.repeat(2000) + ']'), '[' + '{'.repeat(2000) + ']');
  assert.equal(braces.stringify('\\{'.repeat(2000)), '{'.repeat(2000));
  assert.throws(() => braces.expand('{1..1001}'), RangeError);
});

test('updated selector and math renderers support the documentation APIs', () => {
  const selectorParser = require('postcss-selector-parser');
  const selector = '.editor > a:hover, #asset[data-type="image"]';
  assert.equal(selectorParser().processSync(selector), selector);
  const rendered = require('katex').renderToString('E = mc^2', { throwOnError: true });
  assert.match(rendered, /katex/);
  assert.match(rendered, /<math/);
});

test('patched Tinypool retains Docusaurus worker import and workerData behavior', async () => {
  const { default: Tinypool } = await import('tinypool');
  const pool = new Tinypool({
    filename: path.resolve(__dirname, 'fixtures/ssg-worker.cjs'),
    minThreads: 2,
    maxThreads: 2,
    concurrentTasksPerWorker: 1,
    runtime: 'worker_threads',
    isolateWorkers: false,
    workerData: { params: { site: 'Voltage' } },
  });
  try {
    const results = await Promise.all([pool.run({ id: 1 }), pool.run({ id: 2 })]);
    assert.deepEqual(results.map(result => result.task), [1, 2]);
    for (const result of results) {
      assert.equal(result.site, 'Voltage');
      assert.ok(result.workerId > 0);
    }
  } finally {
    await pool.destroy();
  }
});
