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
In an interactive terminal, Fabricator shows a live display with the overall progress of the execution and the list of the running resource checks and applications, with the status and progress of each. Above the display, the logs of the checks and applications are printed in order: the log of the earliest started one is shown live, while the logs of the others are buffered until it finishes, so the lines of different resources never interleave. When the output is redirected or the terminal does not support ANSI escape sequences, only the logs are printed.

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
    return ChangeWithNoDescription
}
```

To see all the kinds of progress reporting in action, run the [example][] in the demo mode: `dotnet run --project Fabricator.Example -- demo apply` (or `demo check`). The demo resources only change files in a temporary directory.

Change Reporting
----------------
After the execution, Fabricator prints a report listing every resource with its final state. Under each resource that is not applied (for `check`) or has been applied (for `apply`), the report shows the change the resource requires or has made, as returned by the resource's `AlreadyApplied` and `Apply` functions. A change ([`ResourceChange`][resource-change]) is one of:
- `NoChanges`: the resource is already applied (returned from `AlreadyApplied`), or no changes were made (returned from `Apply`);
- `TextDiff`: a change of a text entity, e.g. a file content, shown as a patch;
- `NamedChange`: a human-readable description of the change, possibly multi-line, e.g. `new service "name"`;
- `ChangeWithNoDescription`: a change the resource cannot describe; it is not shown in the report.

```fsharp
AlreadyApplied = fun _ -> async {
    return if directory.ExistsDirectory() then NoChanges else NamedChange $"new directory \"{directory.Value}\""
}
Apply = fun _ -> async {
    directory.CreateDirectory()
    return NamedChange $"new directory \"{directory.Value}\""
}
```

The report ends with a short summary of the number of resources in each state, e.g. `3 already applied, 2 to apply, 1 errored`.

To only print the resource states without the change details, pass the `--brief` flag, e.g. `dotnet fsi ./script.fsx check --brief`.

Secrets
-------
A resource may require secrets, e.g. an access token for an API it calls. A secret is referenced by a `SecretRef`: the store to get it from, and its name in the store. The resource should list the secrets in `Secrets` as `SecretRequest`s, each one with the reference and the operations requiring the secret. The operation receives the secrets via its `ResourceContext`, by their references:

```fsharp
let token = { Store = store; Name = SecretName "api-token" }

// In the resource:
{
    Secrets = ImmutableArray.Create { Secret = token; Mode = SecretRequestMode.ApplyOnly }
    Apply = fun ctx -> async {
        do // Dispose the secret as soon as it's not needed anymore.
            use value = ctx.Secrets[token]
            // … call the API using the value …
        // … continue without the secret …
        return NamedChange "updated the remote entity"
    }
}
```

A resource taking a secret from the user (e.g. `DigitalOceanDns.Domain`) should accept a `SecretRef`, and decide itself which operations require it.

Fabricator opens all the required stores before checking or applying anything, so a store asking the user for input can do it in the beginning of the execution. See [`Fabricator.Core`][core] for the details.

To see the secrets in action, run the [demo][] in the secret demo mode: `dotnet run --project Fabricator.Example -- demo-secret apply` (or `demo-secret check`). Note that the demo uses `PlainSecretStore`, which is insecure: it asks you to enter the secrets on every run, and keeps them in the process memory for the whole session.

Packages
--------
- [Fabricator.Console][console]
- [Fabricator.Core][core]
- [Fabricator.Resources][resources]

[console]: xref:Fabricator.Console
[core]: xref:Fabricator.Core
[demo]: https://github.com/ForNeVeR/Fabricator/tree/HEAD/Fabricator.Example/Demo.fs
[powershell-dsc]: https://docs.microsoft.com/en-us/powershell/scripting/dsc/getting-started/wingettingstarted
[resource-change]: xref:Fabricator.Core.ResourceChange
[resource]: xref:Fabricator.Core.Resource
[resources]: xref:Fabricator.Resources
