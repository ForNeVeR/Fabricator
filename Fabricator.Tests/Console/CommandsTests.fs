// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Console.CommandsTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Fabricator.Console
open Fabricator.Console.Commands
open Fabricator.Core
open Fabricator.Tests.FakeResource
open Xunit

let private runCheck(roots: FakeResource list) = task {
    use output = new StringWriter()
    let! status = Commands.check output (roots |> Seq.cast<IResource>)
    return status, output.ToString()
}

let private runApply(roots: FakeResource list) = task {
    use output = new StringWriter()
    let! success = Commands.apply output (roots |> Seq.cast<IResource>)
    return success, output.ToString()
}

let private assertEvents (expected: string list) (log: EventLog) =
    Assert.Equal<string list>(List.sort expected, List.sort log.Events)

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
    let r, d, e = FakeResource("R", log), FakeResource("D", log), FakeResource("E", log)
    r.DependOn d
    d.DependOn e
    r.IsApplied <- true
    d.IsApplied <- true
    e.IsApplied <- true

    let! status, output = runCheck [ r ]

    Assert.Equal(AllApplied, status)
    assertEvents [ "check R"; "check D"; "check E" ] log
    Assert.Contains("R: already applied.", output)
    Assert.Contains("D: already applied.", output)
    Assert.Contains("E: already applied.", output)
}

[<Fact>]
let ``Check reports a non-applied dependency of an applied root``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    r.IsApplied <- true

    let! status, output = runCheck [ r ]

    Assert.Equal(NotAllApplied, status)
    assertEvents [ "check R"; "check D" ] log
    Assert.Contains("R: already applied.", output)
    Assert.Contains("D: not applied.", output)
}

[<Fact>]
let ``Check of a non-applied root checks all its dependencies``(): Task = task {
    let log = EventLog()
    let r, d, e, f = FakeResource("R", log), FakeResource("D", log), FakeResource("E", log), FakeResource("F", log)
    r.DependOn(d, e)
    d.DependOn f
    e.IsApplied <- true

    let! status, output = runCheck [ r ]

    Assert.Equal(NotAllApplied, status)
    assertEvents [ "check R"; "check D"; "check E"; "check F" ] log
    Assert.Contains("R: not applied.", output)
    Assert.Contains("E: already applied.", output)
}

[<Fact>]
let ``Checks of dependent resources run in parallel``(): Task = task {
    let log = EventLog()
    let r, d, e = FakeResource("R", log), FakeResource("D", log), FakeResource("E", log)
    r.DependOn d
    d.DependOn e
    let started = requireConcurrency (fun r hook -> r.OnCheck <- hook) [ r; d; e ]

    let! status, output = runCheck [ r ]

    Assert.True((status = NotAllApplied), output)
    Assert.Equal(3, started.Value)
}

[<Fact>]
let ``Check error is reported and does not prevent other checks``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    r.CheckError <- Some(Exception "Check failure")

    let! status, output = runCheck [ r ]

    Assert.Equal(CheckError, status)
    assertEvents [ "check R"; "check D" ] log
    Assert.Contains("R: error:", output)
    Assert.Contains("Check failure", output)
    Assert.Contains("D: not applied.", output)
}

[<Fact>]
let ``Check error in a dependency is reported``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    r.IsApplied <- true
    d.CheckError <- Some(Exception "Check failure")

    let! status, _ = runCheck [ r ]

    Assert.Equal(CheckError, status)
}

[<Fact>]
let ``Check of no resources succeeds``(): Task = task {
    let! status, _ = runCheck []
    Assert.Equal(AllApplied, status)
}

[<Fact>]
let ``Check reports a dependency cycle``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    d.DependOn r

    let! status, output = runCheck [ r ]

    Assert.Equal(CheckError, status)
    Assert.Empty log.Events
    Assert.Contains("Dependency cycle detected: R → D → R.", output)
}

[<Fact>]
let ``Apply of fully applied resources only checks them``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    r.IsApplied <- true
    d.IsApplied <- true

    let! success, output = runApply [ r ]

    Assert.True success
    assertEvents [ "check R"; "check D" ] log
    Assert.Contains("R: already applied.", output)
    Assert.Contains("D: already applied.", output)
}

[<Fact>]
let ``Apply applies a non-applied dependency of an applied root``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    r.IsApplied <- true

    let! success, output = runApply [ r ]

    Assert.True success
    assertEvents [ "check R"; "check D"; "apply D" ] log
    Assert.Contains("D: applied.", output)
}

[<Fact>]
let ``Apply applies dependencies before dependents``(): Task = task {
    let log = EventLog()
    let r, d, e = FakeResource("R", log), FakeResource("D", log), FakeResource("E", log)
    r.DependOn d
    d.DependOn e

    let! success, output = runApply [ r ]

    Assert.True success
    assertEvents [ "check R"; "check D"; "check E"; "apply E"; "apply D"; "apply R" ] log
    assertBefore "apply E" "apply D" log
    assertBefore "apply D" "apply R" log
    Assert.Contains("R: applying…", output)
    Assert.Contains("R: applied.", output)
}

[<Fact>]
let ``Apply skips applied dependencies but still applies their dependencies``(): Task = task {
    let log = EventLog()
    let r, d, e = FakeResource("R", log), FakeResource("D", log), FakeResource("E", log)
    r.DependOn d
    d.DependOn e
    d.IsApplied <- true

    let! success, _ = runApply [ r ]

    Assert.True success
    assertEvents [ "check R"; "check D"; "check E"; "apply E"; "apply R" ] log
    assertBefore "apply E" "apply R" log
}

[<Fact>]
let ``Dependency is applied only after its dependents are checked``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    r.OnCheck <- fun () -> async {
        do! Async.Sleep 100
        log.Add "check R finished"
    }

    let! success, _ = runApply [ r ]

    Assert.True success
    assertBefore "check R finished" "apply D" log
}

[<Fact>]
let ``Apply processes a shared dependency once``(): Task = task {
    let log = EventLog()
    let r1, r2, d = FakeResource("R1", log), FakeResource("R2", log), FakeResource("D", log)
    r1.DependOn d
    r2.DependOn d

    let! success, _ = runApply [ r1; r2 ]

    Assert.True success
    assertEvents [ "check R1"; "check R2"; "check D"; "apply D"; "apply R1"; "apply R2" ] log
    assertBefore "apply D" "apply R1" log
    assertBefore "apply D" "apply R2" log
}

[<Fact>]
let ``Apply processes a root that is also a dependency once``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d

    let! success, _ = runApply [ r; d ]

    Assert.True success
    assertEvents [ "check R"; "check D"; "apply D"; "apply R" ] log
    assertBefore "apply D" "apply R" log
}

[<Fact>]
let ``Failed dependency blocks its dependents but not the independent resources``(): Task = task {
    let log = EventLog()
    let r, d, independent = FakeResource("R", log), FakeResource("D", log), FakeResource("Independent", log)
    r.DependOn d
    d.ApplyError <- Some(Exception "Apply failure")

    let! success, output = runApply [ r; independent ]

    Assert.False success
    assertEvents [ "check R"; "check D"; "check Independent"; "apply D"; "apply Independent" ] log
    Assert.Contains("D: error:", output)
    Assert.Contains("Apply failure", output)
    Assert.Contains("R: skipped because a dependency has failed.", output)
    Assert.Contains("Independent: applied.", output)
}

[<Fact>]
let ``Dependency check error blocks the dependent application``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    d.CheckError <- Some(Exception "Check failure")

    let! success, output = runApply [ r ]

    Assert.False success
    assertEvents [ "check R"; "check D" ] log
    Assert.Contains("R: skipped because a dependency has failed.", output)
}

[<Fact>]
let ``Root check error fails the application but dependencies are still applied``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    r.CheckError <- Some(Exception "Check failure")

    let! success, output = runApply [ r ]

    Assert.False success
    assertEvents [ "check R"; "check D"; "apply D" ] log
    Assert.DoesNotContain("skipped", output)
}

[<Fact>]
let ``Apply reports a dependency cycle``(): Task = task {
    let log = EventLog()
    let r = FakeResource("R", log)
    r.DependOn r

    let! success, output = runApply [ r ]

    Assert.False success
    Assert.Empty log.Events
    Assert.Contains("Dependency cycle detected: R → R.", output)
}

[<Fact>]
let ``Independent resources are applied in parallel``(): Task = task {
    let log = EventLog()
    let resources = [ for name in [ "A"; "B"; "C" ] -> FakeResource(name, log) ]
    let started = requireConcurrency (fun r hook -> r.OnApply <- hook) resources

    let! success, output = runApply resources

    Assert.True(success, output)
    Assert.Equal(3, started.Value)
}
