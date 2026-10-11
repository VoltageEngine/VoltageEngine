# Bounded braces fork

This private, repository-local package forks the MIT-licensed `braces@3.0.3` runtime from https://registry.npmjs.org/braces/-/braces-3.0.3.tgz.

Upstream archive SHA-256: `1cd18e862c8640b4568b1425a7df4ee030ff201d45b2da8f9f222d2987494ffc`. The original license and author credits are retained. The private workspace version `3.0.3+voltage.1` identifies the local patch while remaining compatible with consumers of braces 3; it is not an upstream patched release.

[GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm) has no published patched version. The parser now rejects more than 64 nested brace/parenthesis blocks before constructing a deeply nested AST. Compile, expand, stringify, array flattening and expansion parent walks also reject excessive depth, including caller-provided ASTs. Escaped delimiters, quoted text and character classes keep their existing meaning. The existing character and range limits remain.

The npm workspace supplies this package to dependencies that request braces 3; it has no install hooks and does not fetch code. Tests run through `npm run test:security` before the documentation build in CI.

`npm audit` checks registry advisories and does not inspect this local fork. Its mitigation is covered by the nesting and compatibility regression tests; a clean audit alone is not evidence of the fork's safety. This change addresses the reported stack exhaustion; it does not promise to bound every possible glob expansion or regular expression.

When upstream publishes a verified fix, remove the workspace and direct development dependency, update consumers to that version, remove this fork, and rerun the security tests and complete site build. Before rebasing the fork, compare every runtime file with the archive and retain only the depth guards.
