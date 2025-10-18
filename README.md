<!--
SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

Fabricator [![Status Zero][status-zero]][andivionian-status-classifier]
==========
Fabricator is a hackable DevOps platform, similar to
PowerShell's [Desired State Configuration][powershell-dsc] in concept.

Core Concept
------------
With Fabricator, the user describes the desired state of their environment, and
Fabricator does its best to lead the configuration to this desired state, when asked
to do so.

Fabricator's "script" is an F# `.fsx` script, where you may use all your
favorite refactoring and code inspection tools; you may wrap or augment
Fabricator calls with your code if you want to.

Fabricator offers a DSL and a set of basic tasks to configure the environment, everything
is available via NuGet and easily extendable.

Also, Fabricator is portable across the platforms supported by .NET.

Basic Workflow
--------------
To start using Fabricator, you should:
1. Create a new F# script file, with contents similar to the [example][].
2. Run the script with `dotnet fsi ./script.fsx check` — this will check the environment and show the changes that are about to be performed.
3. If everything's alright, run the script via `dotnet fsi ./script.fsx apply` — this will apply the changes to the current environment.

Quick script example:

```fsharp
#r "nuget: FVNever.Fabricator, 0.0.0"

let shawlVersion = "1.7.0"
let shawlHash = Sha256 "EAA4FED710E844CC7968FDB82E816D406ED89C4486AB34C3E5DB2DA7E5927923"

let cacheDir = AbsolutePath @"T:\Temp\fabricator\download-cache"
let shawlUrl = Uri $"https://github.com/mtkennerly/shawl/releases/download/v{shawlVersion}/shawl-v{shawlVersion}-win64.zip"

let shawlDownloadCache = cacheDir / fileName shawlUrl
let shawlExecutable = AbsolutePath @"C:\Programs\shawl\shawl.exe"

let installShawl = [
    downloadFile(shawlUrl, shawlHash, shawlDownloadCache)
    unpackArchive(shawlDownloadCache, shawlHash, shawlExecutable.Parent.Value)
    ensureFileExists shawlExecutable
]

let resources = [
    yield! installShawl
]

[<EntryPoint>]
let main(args: string[]): int =
    EntryPoint.main args resources
```

This script will make sure there's an executable `C:\Programs\shawl\shawl.exe` downloaded from the specified URL. This executable might then be used for other resources' setup, e.g. for `Fabricator.Resources.WindowsServices.windowsService`.

Prerequisites
-------------
To work with Fabricator, you'll need [.NET SDK][dotnet-sdk] 9.0 or later.

Documentation
-------------
- [API Reference][docs.api]
- [Contributor Guide][docs.contributing]
- [Maintainer Guide][docs.maintaining]

License
-------
The project is distributed under the terms of [the MIT license][docs.license].

The license indication in the project's sources is compliant with the [REUSE specification v3.3][reuse.spec].

[andivionian-status-classifier]: https://github.com/ForNeVeR/andivionian-status-classifier#status-zero-
[docs.api]: https://fornever.github.io/Fabricator/
[docs.contributing]: CONTRIBUTING.md
[docs.license]: LICENSE.txt
[docs.maintaining]: MAINTAINING.md
[dotnet-sdk]: https://dotnet.microsoft.com/
[example]: Fabricator.Example/Program.fs
[powershell-dsc]: https://docs.microsoft.com/en-us/powershell/scripting/dsc/getting-started/wingettingstarted
[reuse]: https://reuse.software/
[status-zero]: https://img.shields.io/badge/status-zero-lightgrey.svg
