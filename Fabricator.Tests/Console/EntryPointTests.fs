// SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Tests

open System.IO
open System.Text
open Fabricator.Console
open Fabricator.Tests.FakeResource

open Xunit

[<Fact>]
let ``Entry point should return argument parse error when called without arguments``(): unit =
    let exitCode = EntryPoint.main Array.empty Array.empty
    Assert.Equal(EntryPoint.ExitCodes.InvalidArgs, exitCode)

/// A writer failing on the report lines.
type private ReportFailingWriter() =
    inherit TextWriter()
    override _.Encoding = Encoding.UTF8
    override _.WriteLine(value: string | null) =
        match value with
        | NonNull line when line.Contains "(already applied)" -> raise <| IOException "Output failure."
        | _ -> ()

[<Theory>]
[<InlineData "apply">]
[<InlineData "check">]
let ``Report output failure results in an execution error``(command: string): unit =
    let r = FakeResource("R", EventLog())
    r.IsApplied <- true
    let ui = ExecutionUi.PlainUi(new ReportFailingWriter())
    let exitCode = EntryPoint.run ui [ command ] [ r.Resource ]
    Assert.Equal(EntryPoint.ExitCodes.ExecutionError, exitCode)
