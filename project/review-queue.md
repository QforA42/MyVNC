---
id: VNC-REC-review-queue
type: review-queue
title: Review queue
status: current
owner: magnus
updated: 2026-01-01
lang: en
wiki:
  slug: review-queue
  page_type: review-queue
  sync: auto
---
# Review queue

Owner decisions an agent could not get when it needed them. The agent took the least irreversible
option and continued; the owner confirms or changes course here. The session-start hook reports how
many rows are `open`. Close a row by setting its status to `decided` and writing the decision.

| # | Date | Decision needed | Option taken by the agent | Alternatives and cost of changing | Reference | Status |
|---|---|---|---|---|---|---|
| 1 | 2026-01-01 | Example: delete this row when the first real decision is added | — | — | — | decided |
