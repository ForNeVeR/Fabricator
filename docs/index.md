---
_disableBreadcrumb: true
---

<!--
SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

Fabricator
==========
Fabricator is a hackable DevOps platform, similar to
PowerShell's [Desired State Configuration][powershell-dsc] in concept.

Core Concepts
-------------
The main core concept in Fabricator is a [_resource_][resource].

Every entity controlled by Fabricator is a _resource_. A _resource_ knows a target state, knows how to check if the system corresponds to this state, and how to transform the system to this target state.

An example of a resource is a file, or a service.

Full system state, from Fabricator's point of view, corresponds to a set of resources. A resource may depend on other resources: its dependencies have to be applied before the resource itself.

When executing the `apply` command, Fabricator will apply each resource's dependencies first, then check the resource's state, and apply the resource if it isn't applied yet. Independent resources are checked and applied in parallel. The `check` command checks all the resources (including the dependencies) in parallel.

Packages
-------------
- [Fabricator.Console][console]
- [Fabricator.Core][core]
- [Fabricator.Resources][resources]

[console]: xref:Fabricator.Console
[core]: xref:Fabricator.Core
[powershell-dsc]: https://docs.microsoft.com/en-us/powershell/scripting/dsc/getting-started/wingettingstarted
[resource]: xref:Fabricator.Core.Resource
[resources]: xref:Fabricator.Resources
