# docs/ — reader documentation

- Diátaxis: `tutorials/` (learn by doing), `how-to/` (solve a task), `reference/` (facts, contracts),
  `explanation/` (why and how it works). One purpose per document. ADRs in `adr/`.
- Every file needs front-matter (`type`, `status`, `owner`, …); see `tooling/schemas/frontmatter/`.
  `type` is the Diátaxis type, `adr` or `spec`. Check: `node tooling/scripts/check-frontmatter.mjs --files <file>`.
- English only; Swedish text is always saved as `<name>.sv.md` (the `language` gate rejects Swedish
  in any other `.md`). An optional Swedish reader version sits next to its source as `<name>.sv.md` with
  `lang: sv`, `translation_of: <source id>` and id `<source id>-sv`; update it when the source changes.
- Docs change in the same commit as the code they describe.
- `wiki.slug` is stable; never derive it from the file path, so files can move without breaking the wiki.
- No status or progress here: that belongs in `project/`.
