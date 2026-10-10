// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Resources.WindowsServicesTests

open Fabricator.Console
open Fabricator.Resources
open Xunit

let private serialize account commandLine =
    WindowsServiceYaml.serialize { AccountName = account; CommandLine = commandLine }

[<Fact>]
let ``Service state is serialized to YAML``(): unit =
    Assert.Equal(
        "AccountName: NT AUTHORITY\\Network Service\nCommandLine: C:\\service.exe run\n",
        (serialize @"NT AUTHORITY\Network Service" @"C:\service.exe run").ReplaceLineEndings "\n"
    )

[<Fact>]
let ``Service change diff only shows the changed property``(): unit =
    let change =
        Diffs.textChange
            "service"
            (Some(serialize "LocalSystem" @"C:\service.exe run"))
            (serialize "LocalSystem" @"C:\service.exe run --verbose")
    let changedLines =
        Report.details change
        |> Seq.filter (fun line -> line.Kind = DetailKind.Added || line.Kind = DetailKind.Removed)
        |> Seq.map _.Text
    Assert.Equal<string seq>(
        [ @"-CommandLine: C:\service.exe run"; @"+CommandLine: C:\service.exe run --verbose" ],
        changedLines
    )
