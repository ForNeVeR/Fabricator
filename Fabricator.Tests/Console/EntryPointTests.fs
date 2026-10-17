// SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Tests

open System.IO
open System.Text
open Fabricator.Console
open Fabricator.Core
open Fabricator.Core.Secrets
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

let private runWithChange (args: string list) =
    let r = FakeResource("R", EventLog())
    r.Change <- NamedChange "the change"
    use output = new StringWriter()
    let exitCode = EntryPoint.run (ExecutionUi.PlainUi output) args [ r.Resource ]
    exitCode, output.ToString()

[<Theory>]
[<InlineData "check">]
[<InlineData "apply">]
let ``Report shows the changes by default``(command: string): unit =
    let _, output = runWithChange [ command ]
    Assert.Contains("    the change", output)

[<Theory>]
[<InlineData("check", "--brief")>]
[<InlineData("--brief", "check")>]
[<InlineData("apply", "--brief")>]
[<InlineData("--brief", "apply")>]
let ``Brief report omits the changes``(arg1: string, arg2: string): unit =
    let exitCode, output = runWithChange [ arg1; arg2 ]
    let expectedExitCode =
        if arg1 = "check" || arg2 = "check" then EntryPoint.ExitCodes.NotAllApplied else EntryPoint.ExitCodes.Success
    Assert.Equal(expectedExitCode, exitCode)
    Assert.Contains("R (", output)
    Assert.DoesNotContain("the change", output)

[<Fact>]
let ``Apply passes the secrets entered into the plain store``(): unit =
    let store = PlainSecretStore()
    let secret = { Store = store; Name = SecretName "token" }
    let r = FakeResource("R", EventLog(), None, [||], [ { Secret = secret; Mode = SecretRequestMode.ApplyOnly } ])
    let mutable received = None
    r.OnApplyWithContext <- fun ctx -> async { received <- Some(Materialize ctx.Secrets[secret]) }
    use output = new StringWriter()
    let ui = ExecutionUi.PlainUi(output, ExecutionUi.SecretInput.ofReader(new StringReader "my token\n"))

    let exitCode = EntryPoint.run ui [ "apply" ] [ r.Resource ]

    Assert.Equal(EntryPoint.ExitCodes.Success, exitCode)
    Assert.Equal(Some "my token", received)
    let text = output.ToString()
    Assert.Contains("Enter the secret \"token\": ", text)
    Assert.DoesNotContain("my token", text)

[<Fact>]
let ``Store opening failure results in an execution error``(): unit =
    let secret = { Store = PlainSecretStore(); Name = SecretName "token" }
    let r = FakeResource("R", EventLog(), None, [||], [ { Secret = secret; Mode = SecretRequestMode.CheckOnly } ])
    use output = new StringWriter()
    // No input to read the secret from.
    let exitCode = EntryPoint.run (ExecutionUi.PlainUi output) [ "check" ] [ r.Resource ]
    Assert.Equal(EntryPoint.ExitCodes.ExecutionError, exitCode)

[<Fact>]
let ``Unknown flag is an argument error``(): unit =
    let exitCode, _ = runWithChange [ "check"; "--bogus" ]
    Assert.Equal(EntryPoint.ExitCodes.InvalidArgs, exitCode)
