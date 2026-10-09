<!--
SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

---
uid: Fabricator.Core
summary: *content
---

[![NuGet package][nuget.badge]][nuget.page]

The [`Fabricator.Core`][core] package includes the definitions of the core Fabricator interfaces.

Resource checks and applications receive a [`ResourceContext`][resource-context] pinned to the running operation. Its [`IReporter`][reporter] allows to report:
- a short status of the operation (`Status`), shown next to it in the live progress display;
- the progress of the operation (`WithProgress`), either determinate (with the known total) or not, in abstract items or in bytes;
- the log lines (`Log`). The logs of concurrently running operations are never interleaved: the lines of the operations that are not shown at the moment are buffered until their turn comes.

Use [`ResourceContext.Null`][resource-context-null] to call the resource functions directly, e.g. in tests.

[core]: xref:Fabricator.Core
[reporter]: xref:Fabricator.Core.IReporter
[resource-context]: xref:Fabricator.Core.ResourceContext
[resource-context-null]: xref:Fabricator.Core.ResourceContextModule.Null
[nuget.badge]: https://img.shields.io/nuget/v/FVNever.Fabricator.Core
[nuget.page]: https://www.nuget.org/packages/FVNever.Fabricator.Core
