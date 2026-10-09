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

Progress and Logs
-----------------
In an interactive terminal, Fabricator shows a live display with the overall progress of the execution and the list of the running resource checks and applications, with the status and progress of each. Below the display, the logs of the checks and applications are printed in order: the log of the earliest started one is shown live, while the logs of the others are buffered until it finishes, so the lines of different resources never interleave. When the output is redirected, only the logs are printed.

Resources report their status, progress, and log via the `ResourceContext` passed to `AlreadyApplied` and `Apply`:

```fsharp
Apply = fun ctx -> async {
    ctx.Reporter.Status "Preparing"
    ctx.Reporter.Log "Some message."
    do! ctx.Reporter.WithProgress("Processing", Some 40L, Items, fun progress -> async {
        for i in 1L .. 40L do
            // …
            progress.Report i
    })
}
```

To see all the kinds of progress reporting in action, run the [example][] in the demo mode: `dotnet run --project Fabricator.Example -- demo apply` (or `demo check`). The demo resources only change files in a temporary directory.

Packages
--------
- [Fabricator.Console][console]
- [Fabricator.Core][core]
- [Fabricator.Resources][resources]

[console]: xref:Fabricator.Console
[core]: xref:Fabricator.Core
[powershell-dsc]: https://docs.microsoft.com/en-us/powershell/scripting/dsc/getting-started/wingettingstarted
[resource]: xref:Fabricator.Core.Resource
[resources]: xref:Fabricator.Resources
