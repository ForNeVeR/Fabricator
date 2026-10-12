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

Secrets
-------
If a resource requires secrets (passwords, access tokens, etc.), it should list them in [`Resource.Secrets`][resource-secrets]. Each [`SecretRef`][secret-ref] names a secret in an [`ISecretStore`][secret-store], and defines which operations of the resource require it: the check, the application, or both.

The secrets are handled the following way:
1. Before checking or applying anything, Fabricator opens every store that might be required by the execution, one by one. While opening, a store may interact with the user via [`ISecretStoreUi`][secret-store-ui], e.g. to ask for its own password. It also receives the names of the secrets it's about to be asked for, which it may use to prefetch them (not recommended for the purpose of security, but can be done). If any store fails to open, nothing is checked or applied.
2. Right before an operation starts, the secrets it requires are fetched from the opened stores, and passed to it via [`ResourceContext.Secrets`][resource-context-secrets]. If a secret fails to be fetched, the operation fails.
3. Right after the operation finishes, its secrets are disposed automatically. Every secret string is single-use: the operation is encouraged to dispose it as soon as it's not needed anymore, even before the operation finishes.
4. After all the operations have finished, the opened stores are closed.

Stores and secret references are compared by reference.

Note that [`SecureString`][secure-string] in the API provides little actual protection. Fabricator uses it mostly to indicate that the string has to be handled carefully, and to clean it up from the memory as soon as possible.

[`PlainSecretStore`][plain-secret-store] is the simplest store implementation: it asks the user to enter every required secret when opening. **It is not secure:** the entered secrets are kept in the process memory for the whole Fabricator session. Only use it for simple cases, when you're fine with entering the secrets manually on every run, and have no security concerns about keeping them in the process memory for longer than strictly needed.

[core]: xref:Fabricator.Core
[reporter]: xref:Fabricator.Core.IReporter
[resource-context]: xref:Fabricator.Core.ResourceContext
[resource-context-null]: xref:Fabricator.Core.ResourceContextModule.Null
[resource-context-secrets]: xref:Fabricator.Core.ResourceContext.Secrets
[resource-secrets]: xref:Fabricator.Core.Resource.Secrets
[secret-ref]: xref:Fabricator.Core.SecretRef
[secret-store]: xref:Fabricator.Core.ISecretStore
[secret-store-ui]: xref:Fabricator.Core.ISecretStoreUi
[plain-secret-store]: xref:Fabricator.Core.PlainSecretStore
[secure-string]: xref:System.Security.SecureString
[nuget.badge]: https://img.shields.io/nuget/v/FVNever.Fabricator.Core
[nuget.page]: https://www.nuget.org/packages/FVNever.Fabricator.Core
