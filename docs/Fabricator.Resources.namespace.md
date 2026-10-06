<!--
SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

---
uid: Fabricator.Resources
summary: *content
---

[![NuGet package][nuget.badge]][nuget.page]

Fabricator's main concept is [`IResource`][iresource]: this is an entity that can be checked for its presence (via `AlreadyApplied` check) or can be applied to the target environment (via `Apply`). A resource may also depend on other resources (via `DependsOn`).

(Note that [`IResource`][iresource] is defined in the [`Core`][core] package.)

This assembly contains various resources Fabricator supports out of the box. The user can define new resources by implementing the [`IResource`][iresource] interface.

None of the bundled resources has dependencies on its own; use [`Resource.dependsOn`][resource] to add dependencies to any resource.

[core]: xref:Fabricator.Core
[iresource]: xref:Fabricator.Core.IResource
[resource]: xref:Fabricator.Core.Resource
[nuget.badge]: https://img.shields.io/nuget/v/FVNever.Fabricator.Resources
[nuget.page]: https://www.nuget.org/packages/FVNever.Fabricator.Resources
