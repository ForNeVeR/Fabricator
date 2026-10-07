<!--
SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

---
uid: Fabricator.Resources
summary: *content
---

[![NuGet package][nuget.badge]][nuget.page]

Fabricator's main concept is [`Resource`][resource]: this is an entity that can be checked for its presence (via `AlreadyApplied` check) or can be applied to the target environment (via `Apply`).

A resource may also depend on other resources (via `DependsOn`). The dependencies are set when a resource is created: all the bundled resources accept them via an optional `dependsOn` parameter. When a resource has a dependency, this means that before a resource is applied, all its dependencies should be applied as well.

Resources sharing some part of the environment that is not safe to change concurrently (e.g., the same hosts file, or a software repository) declare a common concurrency group via `Lock`: Fabricator never checks or applies the resources from the same group concurrently. The bundled resources expose their groups (e.g., `Chocolatey.ConcurrencyGroup` or `HostsFile.ConcurrencyGroup`), so custom resources can join them.

(Note that [`Resource`][resource] is defined in the [`Core`][core] package.)

This assembly contains various resources Fabricator supports out of the box. The user can define new resources by creating [`Resource`][resource] records.

[core]: xref:Fabricator.Core
[resource]: xref:Fabricator.Core.Resource
[nuget.badge]: https://img.shields.io/nuget/v/FVNever.Fabricator.Resources
[nuget.page]: https://www.nuget.org/packages/FVNever.Fabricator.Resources
