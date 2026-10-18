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
open Fabricator.Tests.TestWriters
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

let private runDryRun (args: string list) =
    let log = EventLog()
    let dependency = FakeResource("Dependency", log)
    let r = FakeResource("R", log, dependency)
    use output = new AsciiWriter()
    let exitCode = EntryPoint.run (ExecutionUi.PlainUi output) args [ r.Resource ]
    exitCode, output.ToString(), log

[<Theory>]
[<InlineData("check", "--dry-run")>]
[<InlineData("--dry-run", "check")>]
let ``Check dry run lists the checks without running them``(arg1: string, arg2: string): unit =
    let exitCode, output, log = runDryRun [ arg1; arg2 ]
    Assert.Equal(EntryPoint.ExitCodes.Success, exitCode)
    Assert.Empty log.Events
    Assert.Contains("[ ] Dependency: check", output)
    Assert.Contains("[ ] R: check", output)
    Assert.DoesNotContain("apply if not applied", output)

[<Theory>]
[<InlineData("apply", "--dry-run")>]
[<InlineData("--dry-run", "apply")>]
let ``Apply dry run lists the checks and applications without running them``(arg1: string, arg2: string): unit =
    let exitCode, output, log = runDryRun [ arg1; arg2 ]
    Assert.Equal(EntryPoint.ExitCodes.Success, exitCode)
    Assert.Empty log.Events
    Assert.Contains("[ ] Dependency: check", output)
    Assert.Contains("[ ] Dependency: apply if not applied", output)
    Assert.Contains("[ ] R: check", output)
    Assert.Contains("[ ] R: apply if not applied", output)

[<Fact>]
let ``Dry run accepts the brief flag``(): unit =
    let exitCode, output, log = runDryRun [ "apply"; "--brief"; "--dry-run" ]
    Assert.Equal(EntryPoint.ExitCodes.Success, exitCode)
    Assert.Empty log.Events
    Assert.Contains("[ ] R: apply if not applied", output)

[<Theory>]
[<InlineData "check">]
[<InlineData "apply">]
let ``Dry run does not open the secret stores``(command: string): unit =
    let secret = { Store = PlainSecretStore(); Name = SecretName "token" }
    let r = FakeResource("R", EventLog(), None, [||], [ { Secret = secret; Mode = SecretRequestMode.CheckAndApply } ])
    use output = new StringWriter()
    // No input to read the secret from: opening the store would fail.
    let exitCode = EntryPoint.run (ExecutionUi.PlainUi output) [ command; "--dry-run" ] [ r.Resource ]
    Assert.Equal(EntryPoint.ExitCodes.Success, exitCode)
    Assert.DoesNotContain("Enter the secret", output.ToString())

/// A writer failing on any output.
type private FailingWriter() =
    inherit TextWriter()
    override _.Encoding = Encoding.UTF8
    override _.WriteLine(_: string | null): unit = raise <| IOException "Output failure."

[<Theory>]
[<InlineData "check">]
[<InlineData "apply">]
let ``Dry run output failure results in an execution error``(command: string): unit =
    let r = FakeResource("R", EventLog())
    let exitCode = EntryPoint.run (ExecutionUi.PlainUi(new FailingWriter())) [ command; "--dry-run" ] [ r.Resource ]
    Assert.Equal(EntryPoint.ExitCodes.ExecutionError, exitCode)

[<Theory>]
[<InlineData "check">]
[<InlineData "apply">]
let ``Dry run listing failure results in an execution error without output``(command: string): unit =
    let resources: Resource seq = seq { failwith "Broken resources." }
    use output = new StringWriter()
    let exitCode = EntryPoint.run (ExecutionUi.PlainUi output) [ command; "--dry-run" ] resources
    Assert.Equal(EntryPoint.ExitCodes.ExecutionError, exitCode)
    Assert.Equal("", output.ToString())
