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

[<Fact>]
let ``Check of an applied root does not check its dependencies``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    r.IsApplied <- true

    let! status, output = runCheck [ r ]

    Assert.Equal(AllApplied, status)
    assertEvents [ "check R" ] log
    Assert.Contains("R: already applied.", output)
}

[<Fact>]
let ``Check of a non-applied root checks its dependencies recursively``(): Task = task {
    let log = EventLog()
    let r, d, e, f = FakeResource("R", log), FakeResource("D", log), FakeResource("E", log), FakeResource("F", log)
    r.DependOn(d, e)
    d.DependOn f
    e.IsApplied <- true

    let! status, output = runCheck [ r ]

    Assert.Equal(NotAllApplied, status)
    assertEvents [ "check R"; "check D"; "check E"; "check F" ] log
    assertBefore "check R" "check D" log
    assertBefore "check R" "check E" log
    assertBefore "check D" "check F" log
    Assert.Contains("R: not applied.", output)
    Assert.Contains("E: already applied.", output)
}

[<Fact>]
let ``Check does not descend into dependencies of an applied dependency``(): Task = task {
    let log = EventLog()
    let r, d, e = FakeResource("R", log), FakeResource("D", log), FakeResource("E", log)
    r.DependOn d
    d.DependOn e
    d.IsApplied <- true

    let! status, _ = runCheck [ r ]

    Assert.Equal(NotAllApplied, status)
    assertEvents [ "check R"; "check D" ] log
}

[<Fact>]
let ``Check status only accounts for root resources``(): Task = task {
    let log = EventLog()
    let r1, r2, d = FakeResource("R1", log), FakeResource("R2", log), FakeResource("D", log)
    r1.DependOn d
    r2.IsApplied <- true

    let! status, _ = runCheck [ r1; r2 ]

    Assert.Equal(NotAllApplied, status)
    assertEvents [ "check R1"; "check R2"; "check D" ] log

    r1.IsApplied <- true
    let! status, _ = runCheck [ r1; r2 ]
    Assert.Equal(AllApplied, status)
}

[<Fact>]
let ``Check error in a root is reported and dependencies are not checked``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    r.CheckError <- Some(Exception "Check failure")

    let! status, output = runCheck [ r ]

    Assert.Equal(CheckError, status)
    assertEvents [ "check R" ] log
    Assert.Contains("R: error:", output)
    Assert.Contains("Check failure", output)
}

[<Fact>]
let ``Check error in a dependency is reported``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
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
let ``Apply of an applied root does nothing else``(): Task = task {
    let log = EventLog()
    let r, d = FakeResource("R", log), FakeResource("D", log)
    r.DependOn d
    r.IsApplied <- true

    let! success, output = runApply [ r ]

    Assert.True success
    assertEvents [ "check R" ] log
    Assert.Contains("R: already applied.", output)
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
    assertBefore "check R" "check D" log
    assertBefore "check D" "check E" log
    assertBefore "apply E" "apply D" log
    assertBefore "apply D" "apply R" log
    Assert.Contains("R: applying…", output)
    Assert.Contains("R: applied.", output)
}

[<Fact>]
let ``Apply skips applied dependencies and does not check their dependencies``(): Task = task {
    let log = EventLog()
    let r, d, e = FakeResource("R", log), FakeResource("D", log), FakeResource("E", log)
    r.DependOn d
    d.DependOn e
    d.IsApplied <- true

    let! success, _ = runApply [ r ]

    Assert.True success
    assertEvents [ "check R"; "check D"; "apply R" ] log
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
let ``Root check error fails the application``(): Task = task {
    let log = EventLog()
    let r = FakeResource("R", log)
    r.CheckError <- Some(Exception "Check failure")

    let! success, output = runApply [ r ]

    Assert.False success
    assertEvents [ "check R" ] log
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
    // Each application waits until all three have started; this only completes if they run concurrently.
    let log = EventLog()
    let started = ref 0
    let allStarted = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let resources = [ for name in [ "A"; "B"; "C" ] -> FakeResource(name, log) ]
    for resource in resources do
        resource.OnApply <- fun () -> async {
            if Interlocked.Increment &started.contents = 3 then allStarted.SetResult()
            do! Async.AwaitTask(allStarted.Task.WaitAsync(TimeSpan.FromSeconds 10.0))
        }

    let! success, output = runApply resources

    Assert.True(success, output)
    Assert.Equal(3, started.Value)
}
