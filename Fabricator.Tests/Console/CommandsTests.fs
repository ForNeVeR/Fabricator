// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Console.CommandsTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Fabricator.Console
open Fabricator.Core
open Fabricator.Tests.FakeResource
open Xunit

let private runCheck(roots: FakeResource list) = task {
    use output = new StringWriter()
    let! report = Commands.check (ExecutionUi.PlainUi output) (roots |> Seq.map _.Resource) |> Async.StartAsTask
    return Report.checkStatus report, report, output.ToString()
}

let private runApply(roots: FakeResource list) = task {
    use output = new StringWriter()
    let! report = Commands.apply (ExecutionUi.PlainUi output) (roots |> Seq.map _.Resource) |> Async.StartAsTask
    return Report.isSuccessful report, report, output.ToString()
}

let private assertEvents (expected: string list) (log: EventLog) =
    Assert.Equal<string list>(List.sort expected, List.sort log.Events)

let private assertStates (expected: (string * ReportItemState) list) (report: Report) =
    Assert.Equal<(string * ReportItemState) list>(expected, report.Items |> List.map (fun i -> i.ResourceName, i.State))

let private assertBefore (first: string) (second: string) (log: EventLog) =
    Assert.True(log.IndexOf first < log.IndexOf second, $"\"{first}\" should happen before \"{second}\": {log.Events}")

/// Makes all the resources wait in the passed hook until all of them have entered it; this only completes if they
/// run concurrently.
let private requireConcurrency (setHook: FakeResource -> (unit -> Async<unit>) -> unit) (resources: FakeResource list) =
    let started = ref 0
    let allStarted = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    for resource in resources do
        setHook resource (fun () -> async {
            if Interlocked.Increment &started.contents = resources.Length then allStarted.SetResult()
            do! Async.AwaitTask(allStarted.Task.WaitAsync(TimeSpan.FromSeconds 10.0))
        })
    started

[<Fact>]
let ``Check of an applied root checks its dependencies``(): Task = task {
    let log = EventLog()
    let e = FakeResource("E", log)
    let d = FakeResource("D", log, e)
    let r = FakeResource("R", log, d)
    r.IsApplied <- true
    d.IsApplied <- true
    e.IsApplied <- true

    let! status, _, output = runCheck [ r ]

    Assert.Equal(AllApplied, status)
    assertEvents [ "check R"; "check D"; "check E" ] log
    Assert.Contains("R: already applied.", output)
    Assert.Contains("D: already applied.", output)
    Assert.Contains("E: already applied.", output)
}

[<Fact>]
let ``Check reports a non-applied dependency of an applied root``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    let r = FakeResource("R", log, d)
    r.IsApplied <- true

    let! status, report, output = runCheck [ r ]

    Assert.Equal(NotAllApplied, status)
    assertEvents [ "check R"; "check D" ] log
    assertStates [ "D", ReportItemState.NotApplied; "R", ReportItemState.AlreadyApplied ] report
    Assert.Contains("R: already applied.", output)
    Assert.Contains("D: not applied.", output)
}

[<Fact>]
let ``Check of a non-applied root checks all its dependencies``(): Task = task {
    let log = EventLog()
    let f, e = FakeResource("F", log), FakeResource("E", log)
    let d = FakeResource("D", log, f)
    let r = FakeResource("R", log, d, e)
    e.IsApplied <- true

    let! status, _, output = runCheck [ r ]

    Assert.Equal(NotAllApplied, status)
    assertEvents [ "check R"; "check D"; "check E"; "check F" ] log
    Assert.Contains("R: not applied.", output)
    Assert.Contains("E: already applied.", output)
}

[<Fact>]
let ``Checks of dependent resources run in parallel``(): Task = task {
    let log = EventLog()
    let e = FakeResource("E", log)
    let d = FakeResource("D", log, e)
    let r = FakeResource("R", log, d)
    let started = requireConcurrency (fun r hook -> r.OnCheck <- hook) [ r; d; e ]

    let! status, _, output = runCheck [ r ]

    Assert.True((status = NotAllApplied), output)
    Assert.Equal(3, started.Value)
}

[<Fact>]
let ``Check error is reported and does not prevent other checks``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    let r = FakeResource("R", log, d)
    r.CheckError <- Some(Exception "Check failure")

    let! status, report, output = runCheck [ r ]

    Assert.Equal(CheckError, status)
    assertEvents [ "check R"; "check D" ] log
    assertStates [ "D", ReportItemState.NotApplied; "R", ReportItemState.CheckFailed ] report
    Assert.Contains("R: error:", output)
    Assert.Contains("Check failure", output)
    Assert.Contains("D: not applied.", output)
}

[<Fact>]
let ``Check error in a dependency is reported``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    let r = FakeResource("R", log, d)
    r.IsApplied <- true
    d.CheckError <- Some(Exception "Check failure")

    let! status, _, _ = runCheck [ r ]

    Assert.Equal(CheckError, status)
}

[<Fact>]
let ``Check of no resources succeeds``(): Task = task {
    let! status, _, _ = runCheck []
    Assert.Equal(AllApplied, status)
}

[<Fact>]
let ``Apply of fully applied resources only checks them``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    let r = FakeResource("R", log, d)
    r.IsApplied <- true
    d.IsApplied <- true

    let! success, report, output = runApply [ r ]

    Assert.True success
    assertEvents [ "check R"; "check D" ] log
    assertStates [ "D", ReportItemState.AlreadyApplied; "R", ReportItemState.AlreadyApplied ] report
    Assert.Contains("R: already applied.", output)
    Assert.Contains("D: already applied.", output)
}

[<Fact>]
let ``Apply applies a non-applied dependency of an applied root``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    let r = FakeResource("R", log, d)
    r.IsApplied <- true

    let! success, _, output = runApply [ r ]

    Assert.True success
    assertEvents [ "check R"; "check D"; "apply D" ] log
    Assert.Contains("D: applied.", output)
}

[<Fact>]
let ``Apply applies dependencies before dependents``(): Task = task {
    let log = EventLog()
    let e = FakeResource("E", log)
    let d = FakeResource("D", log, e)
    let r = FakeResource("R", log, d)

    let! success, report, output = runApply [ r ]

    Assert.True success
    assertEvents [ "check R"; "check D"; "check E"; "apply E"; "apply D"; "apply R" ] log
    assertStates [ "E", ReportItemState.Applied; "D", ReportItemState.Applied; "R", ReportItemState.Applied ] report
    assertBefore "apply E" "apply D" log
    assertBefore "apply D" "apply R" log
    Assert.Contains("R: applying…", output)
    Assert.Contains("R: applied.", output)
}

[<Fact>]
let ``Apply skips applied dependencies but still applies their dependencies``(): Task = task {
    let log = EventLog()
    let e = FakeResource("E", log)
    let d = FakeResource("D", log, e)
    let r = FakeResource("R", log, d)
    d.IsApplied <- true

    let! success, _, _ = runApply [ r ]

    Assert.True success
    assertEvents [ "check R"; "check D"; "check E"; "apply E"; "apply R" ] log
    assertBefore "apply E" "apply R" log
}

[<Fact>]
let ``Dependent is checked only after its dependencies are applied``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    d.OnApply <- fun () -> async {
        do! Async.Sleep 100
        log.Add "apply D finished"
    }
    let r = FakeResource("R", log, d)

    let! success, _, _ = runApply [ r ]

    Assert.True success
    assertBefore "apply D finished" "check R" log
}

[<Fact>]
let ``Dependent whose check requires its dependency is applied in a single run``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    let r = FakeResource("R", log, d)
    r.OnCheck <- fun () -> async {
        if not d.IsApplied then failwith "D is required to check R."
    }

    let! success, _, output = runApply [ r ]

    Assert.True(success, output)
    assertEvents [ "check D"; "apply D"; "check R"; "apply R" ] log
}

[<Fact>]
let ``Dependent applied by its dependency is not applied again``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    let r = FakeResource("R", log, d)
    d.OnApply <- fun () -> async { r.IsApplied <- true }

    let! success, _, output = runApply [ r ]

    Assert.True(success, output)
    assertEvents [ "check D"; "apply D"; "check R" ] log
    Assert.Contains("R: already applied.", output)
}

[<Fact>]
let ``Apply processes a shared dependency once``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    let r1, r2 = FakeResource("R1", log, d), FakeResource("R2", log, d)

    let! success, _, _ = runApply [ r1; r2 ]

    Assert.True success
    assertEvents [ "check R1"; "check R2"; "check D"; "apply D"; "apply R1"; "apply R2" ] log
    assertBefore "apply D" "apply R1" log
    assertBefore "apply D" "apply R2" log
}

[<Fact>]
let ``Apply processes a root that is also a dependency once``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    let r = FakeResource("R", log, d)

    let! success, _, _ = runApply [ r; d ]

    Assert.True success
    assertEvents [ "check R"; "check D"; "apply D"; "apply R" ] log
    assertBefore "apply D" "apply R" log
}

[<Fact>]
let ``Failed dependency blocks its dependents but not the independent resources``(): Task = task {
    let log = EventLog()
    let d, independent = FakeResource("D", log), FakeResource("Independent", log)
    let r = FakeResource("R", log, d)
    d.ApplyError <- Some(Exception "Apply failure")

    let! success, report, output = runApply [ r; independent ]

    Assert.False success
    assertStates [
        "D", ReportItemState.ApplyFailed
        "R", ReportItemState.Skipped
        "Independent", ReportItemState.Applied
    ] report
    assertEvents [ "check D"; "check Independent"; "apply D"; "apply Independent" ] log
    Assert.Contains("D: error:", output)
    Assert.Contains("Apply failure", output)
    Assert.Contains("R: skipped because a dependency has failed.", output)
    Assert.Contains("Independent: applied.", output)
}

[<Fact>]
let ``Dependency check error blocks the dependent application``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    let r = FakeResource("R", log, d)
    d.CheckError <- Some(Exception "Check failure")

    let! success, report, output = runApply [ r ]

    Assert.False success
    assertEvents [ "check D" ] log
    assertStates [ "D", ReportItemState.CheckFailed; "R", ReportItemState.Skipped ] report
    Assert.Contains("R: skipped because a dependency has failed.", output)
}

[<Fact>]
let ``Root check error fails the application but dependencies are still applied``(): Task = task {
    let log = EventLog()
    let d = FakeResource("D", log)
    let r = FakeResource("R", log, d)
    r.CheckError <- Some(Exception "Check failure")

    let! success, report, output = runApply [ r ]

    Assert.False success
    assertEvents [ "check R"; "check D"; "apply D" ] log
    assertStates [ "D", ReportItemState.Applied; "R", ReportItemState.CheckFailed ] report
    assertBefore "apply D" "check R" log
    Assert.DoesNotContain("skipped", output)
}

[<Fact>]
let ``Independent resources are applied in parallel``(): Task = task {
    let log = EventLog()
    let resources = [ for name in [ "A"; "B"; "C" ] -> FakeResource(name, log) ]
    let started = requireConcurrency (fun r hook -> r.OnApply <- hook) resources

    let! success, _, output = runApply resources

    Assert.True(success, output)
    Assert.Equal(3, started.Value)
}

[<Fact>]
let ``Cancellation stops the application without starting dependents or reporting errors``(): Task = task {
    let log = EventLog()
    let applyStarted = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let d = FakeResource("D", log)
    d.OnApply <- fun () -> async {
        applyStarted.SetResult()
        do! Async.Sleep(TimeSpan.FromSeconds 10.0)
    }
    let r = FakeResource("R", log, d)

    use cts = new CancellationTokenSource()
    use output = new StringWriter()
    let execution = Async.StartAsTask(Commands.apply (ExecutionUi.PlainUi output) [ r.Resource ], cancellationToken = cts.Token)
    do! applyStarted.Task.WaitAsync(TimeSpan.FromSeconds 10.0)
    cts.Cancel()

    let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(fun () -> execution :> Task)
    assertEvents [ "check D"; "apply D" ] log
    Assert.DoesNotContain("error", output.ToString())
}
/// Tracks the maximum number of resource actions running at the same time.
type private ConcurrencyMeter() =
    let lockObj = obj()
    let mutable current = 0
    let mutable maximum = 0
    member _.Maximum = lock lockObj (fun () -> maximum)
    member _.Measure(): Async<unit> = async {
        lock lockObj (fun () ->
            current <- current + 1
            maximum <- max maximum current
        )
        do! Async.Sleep 50
        lock lockObj (fun () -> current <- current - 1)
    }

[<Fact>]
let ``Resources from the same concurrency group are never processed concurrently``(): Task = task {
    let log = EventLog()
    let group = Some(ConcurrencyGroup "Group")
    let resources = [ for name in [ "A"; "B"; "C" ] -> FakeResource(name, log, group, [||]) ]
    let meter = ConcurrencyMeter()
    for resource in resources do
        resource.OnCheck <- meter.Measure
        resource.OnApply <- meter.Measure

    let! success, _, output = runApply resources

    Assert.True(success, output)
    assertEvents [ "check A"; "check B"; "check C"; "apply A"; "apply B"; "apply C" ] log
    Assert.Equal(1, meter.Maximum)
}

[<Fact>]
let ``Resources from different concurrency groups are processed in parallel``(): Task = task {
    let log = EventLog()
    let resources = [
        FakeResource("A", log, Some(ConcurrencyGroup "A"), [||])
        FakeResource("B", log, Some(ConcurrencyGroup "B"), [||])
        FakeResource("NoGroup", log)
    ]
    let started = requireConcurrency (fun r hook -> r.OnApply <- hook) resources

    let! success, _, output = runApply resources

    Assert.True(success, output)
    Assert.Equal(3, started.Value)
}
/// Asserts that the lines of the output matching the pattern go one after another, without any other lines between.
let private assertContiguous (output: string) (pattern: string) =
    let lines = output.Split('\n') |> Array.map _.TrimEnd('\r')
    let indices =
        lines
        |> Array.indexed
        |> Array.filter (fun (_, line) -> Text.RegularExpressions.Regex.IsMatch(line, pattern))
        |> Array.map fst
    Assert.True(indices.Length > 1, $"Lines matching \"{pattern}\" not found in output:\n{output}")
    Assert.True(
        Array.last indices - indices[0] = indices.Length - 1,
        $"Lines matching \"{pattern}\" are interleaved with other lines:\n{output}"
    )

[<Fact>]
let ``Logs of concurrently applied resources are not interleaved``(): Task = task {
    let log = EventLog()
    let names = [ "A"; "B"; "C" ]
    let resources = [ for name in names -> FakeResource(name, log) ]
    let started = requireConcurrency (fun r hook -> r.OnApply <- hook) resources
    for resource in resources do
        resource.OnApplyWithContext <- fun ctx -> async {
            for i in 1 .. 5 do
                ctx.Reporter.Log $"line {i}"
                do! Async.Sleep 10
        }

    let! success, _, output = runApply resources

    Assert.True(success, output)
    Assert.Equal(3, started.Value)
    for name in names do
        assertContiguous output $"^{name}: (applying…|line \d|applied\.)$"
        Assert.Contains($"{name}: line 5", output)
}

[<Fact>]
let ``Status and progress reporting is allowed in plain output``(): Task = task {
    let log = EventLog()
    let r = FakeResource("R", log)
    r.OnApplyWithContext <- fun ctx -> async {
        ctx.Reporter.Status "Working"
        let! result = ctx.Reporter.WithProgress("Progress", Some 10L, Items, fun progress -> async {
            for i in 1L .. 10L do progress.Report i
            return 42
        })
        ctx.Reporter.Log $"result {result}"
    }

    let! success, _, output = runApply [ r ]

    Assert.True(success, output)
    Assert.Contains("R: result 42", output)
    Assert.DoesNotContain("Working", output)
}

/// A writer failing to write the lines containing the passed text.
type private FailingWriter(failOn: string) =
    inherit TextWriter()
    override _.Encoding = Text.Encoding.UTF8
    override _.WriteLine(value: string | null) =
        match value with
        | NonNull line when line.Contains failOn -> raise <| IOException "Output failure."
        | _ -> ()

[<Fact>]
let ``Output failure cancels the execution and is reported``(): Task = task {
    let log = EventLog()
    let r = FakeResource("R", log)
    r.OnApply <- fun () -> Async.Sleep(TimeSpan.FromSeconds 30.0)

    let stopwatch = Diagnostics.Stopwatch.StartNew()
    let ui = ExecutionUi.PlainUi(new FailingWriter "applying")
    let! ex = Assert.ThrowsAsync<IOException>(fun () -> Commands.apply ui [ r.Resource ] |> Async.StartAsTask :> Task)

    Assert.Equal("Output failure.", ex.Message)
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds 20.0, $"The execution took {stopwatch.Elapsed}.")
}
