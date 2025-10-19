<!--
SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

Fabricator [![Status Enfer][status-enfer]][andivionian-status-classifier] [![Fabricator.Core on nuget.org][nuget.badge]][nuget]
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
#r "nuget: FVNever.Fabricator.Console, 0.1.0"
#r "nuget: FVNever.Fabricator.Resources, 0.1.0"

open System
open System.IO
open Fabricator.Console
open Fabricator.Resources.Archive
open Fabricator.Resources.Downloads
open Fabricator.Resources.Files
open Fabricator.Resources.Hash
open TruePath

let shawlVersion = "1.7.0"
let shawlHash = Sha256 "EAA4FED710E844CC7968FDB82E816D406ED89C4486AB34C3E5DB2DA7E5927923"

let cacheDir = AbsolutePath @"T:\Temp\fabricator\download-cache"
let shawlUrl = Uri $"https://github.com/mtkennerly/shawl/releases/download/v{shawlVersion}/shawl-v{shawlVersion}-win64.zip"

let shawlDownloadCache = cacheDir / Path.GetFileName shawlUrl.LocalPath
let shawlExecutable = AbsolutePath @"C:\Programs\shawl\shawl.exe"

let installShawl = [
    downloadFile(shawlUrl, shawlHash, shawlDownloadCache)
    unpackArchive(shawlDownloadCache, shawlHash, shawlExecutable.Parent.Value)
    ensureFileExists shawlExecutable
]

let resources = [
    yield! installShawl
]

exit <| EntryPoint.main fsi.CommandLineArgs resources
```

This script will make sure there's an executable `C:\Programs\shawl\shawl.exe` downloaded from the specified URL. This executable might then be used for other resources' setup, e.g., for the [Windows service resource][docs.windows-service].

Prerequisites
-------------
To work with Fabricator, you'll need [.NET SDK][dotnet-sdk] 9.0 or later.

Documentation
-------------
- [Project Documentation Site (API Reference)][docs]
- [Changelog][docs.changelog]
- [Contributor Guide][docs.contributing]
- [Maintainer Guide][docs.maintaining]

License
-------
The project is distributed under the terms of [the MIT license][docs.license].

The license indication in the project's sources is compliant with the [REUSE specification v3.3][reuse.spec].

[andivionian-status-classifier]: https://andivionian.fornever.me/v1/#status-enfer-
[docs.changelog]: CHANGELOG.md
[docs.contributing]: CONTRIBUTING.md
[docs.license]: LICENSE.txt
[docs.maintaining]: MAINTAINING.md
[docs.windows-service]: https://fornever.github.io/Fabricator/api/Fabricator.Resources.WindowsServices.html#Fabricator_Resources_WindowsServices_createWindowsService_System_String_System_String_System_String_
[docs]: https://fornever.github.io/Fabricator/
[dotnet-sdk]: https://dotnet.microsoft.com/
[example]: Fabricator.Example/Program.fs
[nuget.badge]: https://img.shields.io/nuget/v/FVNever.Fabricator.Core
[nuget]: https://www.nuget.org/packages/FVNever.Fabricator.Core
[powershell-dsc]: https://docs.microsoft.com/en-us/powershell/scripting/dsc/getting-started/wingettingstarted
[reuse.spec]: https://reuse.software/spec/
[status-enfer]: https://img.shields.io/badge/status-enfer-orange.svg
