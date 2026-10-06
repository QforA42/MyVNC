Edit agent behavior only in `.agents/` or `tooling/scripts/sync-agents.mjs`. Never hand-edit
generated `.codex/config.toml`, `.codex/hooks.json`, `.codex/agents/`, `.codex/roles/`,
`.claude/agents/` or copied skill directories. Validate generation in a temporary repository
when generated workspace files must remain untouched.

Use subagents for independent, substantial exploration, implementation or review when delegation
improves speed or keeps noisy output out of the main context. Keep small tasks and short checks in
the main agent. Give each subagent a bounded task, allowed files, verification command and expected
summary. Assign disjoint files to writers; wait for results and inspect changes before finishing.
Subagents must report to the parent rather than recursively delegate unless explicitly assigned
that responsibility. Use only the available runtime slots.

Roles and reasoning tiers come from `.agents/roles/` and `.agents/agents.config.json`:
T1 scout/test-runner = low, T2 implementer/doc-writer = medium, T3 architect/reviewer = high.
Use the generated custom agent when supported. Otherwise read the source role and include its
instructions in the delegated task. Request its tier's effort only if the spawn tool supports the
override; a full-history fork may require inheriting the parent's model and effort. Do not invent
model names or unsupported settings. A tier is a reasoning budget, not a guarantee of a cheaper model.
Escalation means report evidence and let the parent reassess scope or delegate deeper reasoning.

Read applicable folder instructions before editing. Gates and git hooks remain the completion
checks; Codex lifecycle hooks provide early feedback and do not replace them. Tool categories in
role front-matter describe responsibilities; they are not Codex tool allowlists. A test runner may
write build artifacts but must not edit source files.
