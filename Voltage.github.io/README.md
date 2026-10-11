# Voltage documentation site

A [Docusaurus 3](https://docusaurus.io/) site that renders the Markdown in the repository's `docs/` folder. Edit pages there; this folder only holds the site configuration, theme and landing page.

```bash
npm ci
npm run start     # live-reloading dev server
npm run test:security  # dependency mitigation regressions
npm run build     # static site in build/, fails on broken links
```

Requires Node 24 or newer. `.github/workflows/docs.yml` builds and publishes the site to GitHub Pages on every push to `main` that touches `docs/` or this folder.

Security-sensitive transitive dependencies use root overrides until Docusaurus adopts their patched releases. The unpatched `braces` dependency uses a tracked workspace [local fork](vendor/braces/README.md) with a nesting limit. Run `npm audit` and `npm run test:security` when changing dependencies; audit does not scan the fork itself.

Validated with a clean `npm ci`, seven dependency regression tests, the production site build, and a zero-vulnerability npm audit. Follow up by replacing the local braces workspace with a verified upstream fix when available, and removing the other overrides once Docusaurus requests patched versions itself. Review dependency updates with the same checks.
