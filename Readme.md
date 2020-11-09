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

[andivionian-status-classifier]: https://github.com/ForNeVeR/andivionian-status-classifier#status-zero-
[powershell-dsc]: https://docs.microsoft.com/en-us/powershell/scripting/dsc/getting-started/wingettingstarted
[propellor]: http://propellor.branchable.com/

[status-zero]: https://img.shields.io/badge/status-zero-lightgrey.svg
