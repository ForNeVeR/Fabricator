Fabricator [![Status Zero][status-zero]][andivionian-status-classifier]
==========
Fabricator is a hackable DevOps platform, similar to
PowerShell [Desired State Configuration][powershell-dsc] in concept (to say,
PowerShell team weren't the first ones to invent the concept, but have chosen
the most descriptive name), and to [Propellor][propellor] in implementation.

Core Concept
------------
With Fabricator, the user describes the desired state of their cluster, and
Fabricator does its best to lead the cluster to this desired state, when asked
to do so.

Fabricator "script" is an ordinary .NET project, where you may use all your
favorite refactoring and code inspection tools; you may wrap or augment
Fabricator calls with your code if you want to.

Fabricator offers a DSL and a set of tasks to configure the cluster, everything
is available via NuGet and easily extendable.

Also, Fabricator is portable across the platforms supported by .NET Core: both
the control machine and any of the nodes across the cluster may run any
supported operating systems.

Detailed Workflow
-----------------
Whenever the user wants to apply their changes to the cluster, they may, for
each device:

- run the Fabricator-created binary locally on that device (via `dotnet run`, if
  .NET SDK is installed, or via other means)
- upload the Fabricator-created binary to a remove host and run there, providing
  the runtime for it (if required)
- make Fabricator to upload the binary (essentially cloning itself to a remote
  host) and run via the runtime already existing on the host

Usually, .NET SDK should only be available locally, and shouldn't be necessary
on remote.

When Fabricator is started on a remote host, it should be able to identify the
host and required actions. It could do that either by passing command-line
argument to itself, or by reading the hostname (if available).

[andivionian-status-classifier]: https://github.com/ForNeVeR/andivionian-status-classifier#status-zero-
[powershell-dsc]: https://docs.microsoft.com/en-us/powershell/scripting/dsc/getting-started/wingettingstarted
[propellor]: http://propellor.branchable.com/

[status-zero]: https://img.shields.io/badge/status-zero-lightgrey.svg
