// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Console.SecretsTests

open System
open System.Collections.Concurrent
open System.IO
open System.Security
open System.Threading
open System.Threading.Tasks
open Fabricator.Console
open Fabricator.Core
open Fabricator.Core.Secrets
open Fabricator.Tests.FakeResource
open Fabricator.Tests.FakeSecretStore
open Xunit

let private runCheck(roots: FakeResource list) = task {
    use output = new StringWriter()
    let! report = Commands.check (ExecutionUi.PlainUi output) (roots |> Seq.map _.Resource) |> Async.StartAsTask
    return report, output.ToString()
}

let private runApply(roots: FakeResource list) = task {
    use output = new StringWriter()
    let! report = Commands.apply (ExecutionUi.PlainUi output) (roots |> Seq.map _.Resource) |> Async.StartAsTask
    return report, output.ToString()
}

let private withSecrets (name: string) (log: EventLog) (secrets: SecretRequest list) =
    FakeResource(name, log, None, [||], secrets)

let private eventsStartingWith (prefix: string) (log: EventLog) =
    log.Events |> Seq.filter _.StartsWith(prefix)

let private assertBefore (first: string) (second: string) (log: EventLog) =
    Assert.True(log.IndexOf first < log.IndexOf second, $"\"{first}\" should happen before \"{second}\": {log}")

/// Records the secrets passed to the resource's actions, as (action, name, value) entries.
let private recordSecrets (resource: FakeResource) =
    let received = ConcurrentQueue<string * string * string>()
    let record action (ctx: ResourceContext) = async {
        for KeyValue(secret, value) in ctx.Secrets do
            received.Enqueue(action, string secret.Name, Materialize value)
    }
    resource.OnCheckWithContext <- record "check"
    resource.OnApplyWithContext <- record "apply"
    received

[<Fact>]
let ``Check only opens the stores of the secrets required by checks``(): Task = task {
    let log = EventLog()
    let a, b, c = FakeSecretStore("A", log), FakeSecretStore("B", log), FakeSecretStore("C", log)
    let r1 = withSecrets "R1" log [ a.Request("x", SecretRequestMode.CheckOnly) ]
    let r2 = withSecrets "R2" log [ b.Request("y", SecretRequestMode.ApplyOnly) ]
    let r3 = withSecrets "R3" log [ c.Request("z", SecretRequestMode.CheckAndApply) ]

    let! report, output = runCheck [ r1; r2; r3 ]

    Assert.True((Report.checkStatus report = NotAllApplied), output)
    Assert.Equal<string seq>([ "open A [x]"; "open C [z]" ], eventsStartingWith "open " log)
}

[<Fact>]
let ``Apply opens every store once with the distinct names of its secrets``(): Task = task {
    let log = EventLog()
    let store = FakeSecretStore("S", log)
    let d = withSecrets "D" log [
        store.Request("x", SecretRequestMode.CheckOnly)
        store.Request("y", SecretRequestMode.ApplyOnly)
    ]
    let r = FakeResource("R", log, None, [| d |], [
        store.Request("x", SecretRequestMode.ApplyOnly)
        store.Request("z", SecretRequestMode.CheckAndApply)
    ])

    let! report, output = runApply [ r ]

    Assert.True(Report.isSuccessful report, output)
    Assert.Equal<string seq>([ "open S [x, y, z]" ], eventsStartingWith "open " log)
}

[<Fact>]
let ``Stores are opened one by one before any task starts``(): Task = task {
    let log = EventLog()
    let a, b = FakeSecretStore("A", log), FakeSecretStore("B", log)
    for store in [ a; b ] do
        store.OnOpen <- fun () -> Async.Sleep 50
    let r1 = withSecrets "R1" log [ a.Request("x", SecretRequestMode.CheckOnly) ]
    let r2 = withSecrets "R2" log [ b.Request("y", SecretRequestMode.CheckOnly) ]

    let! _, _ = runCheck [ r1; r2 ]

    Assert.Equal<string seq>(
        [ "open A [x]"; "opened A"; "open B [y]"; "opened B" ],
        log.Events |> Seq.filter (fun e -> e.StartsWith "open")
    )
    assertBefore "opened B" "check R1" log
    assertBefore "opened B" "check R2" log
}

[<Fact>]
let ``Each action receives the secrets of its kind``(): Task = task {
    let log = EventLog()
    let store = FakeSecretStore("S", log)
    let r = withSecrets "R" log [
        store.Request("c", SecretRequestMode.CheckOnly)
        store.Request("a", SecretRequestMode.ApplyOnly)
        store.Request("b", SecretRequestMode.CheckAndApply)
    ]
    let received = recordSecrets r

    let! report, output = runApply [ r ]

    Assert.True(Report.isSuccessful report, output)
    Assert.Equal<Set<string * string * string>>(
        set [
            "check", "c", "S:c"
            "check", "b", "S:b"
            "apply", "a", "S:a"
            "apply", "b", "S:b"
        ],
        Set.ofSeq received
    )
    Assert.Equal(4, received.Count)
}

[<Fact>]
let ``Secrets are fetched right before the action``(): Task = task {
    let log = EventLog()
    let store = FakeSecretStore("S", log)
    let d = FakeResource("D", log)
    let r = FakeResource("R", log, None, [| d |], [ store.Request("x", SecretRequestMode.ApplyOnly) ])

    let! report, output = runApply [ r ]

    Assert.True(Report.isSuccessful report, output)
    assertBefore "apply D" "get S x" log
    assertBefore "check R" "get S x" log
    assertBefore "get S x" "apply R" log
}

[<Fact>]
let ``Apply-only secret is not fetched if the application is not required``(): Task = task {
    let log = EventLog()
    let store = FakeSecretStore("S", log)
    let r = withSecrets "R" log [ store.Request("x", SecretRequestMode.ApplyOnly) ]
    r.IsApplied <- true

    let! report, output = runApply [ r ]

    Assert.True(Report.isSuccessful report, output)
    Assert.Contains("open S [x]", log.Events)
    Assert.Empty(eventsStartingWith "get " log)
}

[<Fact>]
let ``Secrets are disposed after the action``(): Task = task {
    let log = EventLog()
    let store = FakeSecretStore("S", log)
    let request = store.Request("x", SecretRequestMode.CheckAndApply)
    let r = withSecrets "R" log [ request ]
    let captured = ConcurrentQueue<SecureString>()
    let capture (ctx: ResourceContext) = async {
        let value = ctx.Secrets[request.Secret]
        Assert.Equal("S:x", Materialize value)
        captured.Enqueue value
    }
    r.OnCheckWithContext <- capture
    r.OnApplyWithContext <- capture

    let! report, output = runApply [ r ]

    Assert.True(Report.isSuccessful report, output)
    Assert.Equal(2, captured.Count)
    Assert.Equal(2, store.Returned.Count)
    for value in Seq.append captured store.Returned do
        Assert.Throws<ObjectDisposedException>(fun () -> value.Length |> ignore) |> ignore
}

[<Fact>]
let ``Action may dispose its secrets itself``(): Task = task {
    let log = EventLog()
    let store = FakeSecretStore("S", log)
    let request = store.Request("x", SecretRequestMode.ApplyOnly)
    let r = withSecrets "R" log [ request ]
    r.OnApplyWithContext <- fun ctx -> async { ctx.Secrets[request.Secret].Dispose() }

    let! report, output = runApply [ r ]

    Assert.True(Report.isSuccessful report, output)
}

[<Fact>]
let ``Secret fetch failure fails only the resource requiring it``(): Task = task {
    let log = EventLog()
    let store = FakeSecretStore("S", log)
    store.GetError <- Some(Exception "Fetch failure")
    let r = withSecrets "R" log [ store.Request("x", SecretRequestMode.CheckAndApply) ]
    let dependent = FakeResource("Dependent", log, r)
    let independent = FakeResource("Independent", log)

    let! report, output = runApply [ dependent; independent ]

    Assert.False(Report.isSuccessful report, output)
    Assert.Equal<(string * ReportItemState) list>(
        [
            "R", ReportItemState.CheckErrored
            "Dependent", ReportItemState.Skipped
            "Independent", ReportItemState.Applied
        ],
        report.Items |> List.map (fun i -> i.ResourceName, i.State)
    )
    Assert.DoesNotContain("check R", log.Events)
    Assert.Contains("R: error:", output)
    Assert.Contains("Fetch failure", output)
    Assert.Contains("dispose S", log.Events)
}

[<Fact>]
let ``Store opening failure aborts the execution``(): Task = task {
    let log = EventLog()
    let a, b = FakeSecretStore("A", log), FakeSecretStore("B", log)
    b.OpenError <- Some(InvalidOperationException "Open failure")
    let r1 = withSecrets "R1" log [ a.Request("x", SecretRequestMode.CheckOnly) ]
    let r2 = withSecrets "R2" log [ b.Request("y", SecretRequestMode.CheckOnly) ]

    let! ex = Assert.ThrowsAsync<InvalidOperationException>(fun () -> runApply [ r1; r2 ] :> Task)

    Assert.Equal("Open failure", ex.Message)
    Assert.Equal<string seq>([ "open A [x]"; "opened A"; "open B [y]"; "dispose A" ], log.Events)
}

[<Fact>]
let ``Stores are closed after all the tasks have finished``(): Task = task {
    let log = EventLog()
    let a, b = FakeSecretStore("A", log), FakeSecretStore("B", log)
    let d = withSecrets "D" log [ a.Request("x", SecretRequestMode.ApplyOnly) ]
    let r = FakeResource("R", log, None, [| d |], [ b.Request("y", SecretRequestMode.ApplyOnly) ])

    let! report, output = runApply [ r ]

    Assert.True(Report.isSuccessful report, output)
    for store in [ "A"; "B" ] do
        assertBefore "apply R" $"dispose {store}" log
    Assert.Equal(2, eventsStartingWith "dispose " log |> Seq.length)
}

[<Fact>]
let ``Stores are closed after a failed execution``(): Task = task {
    let log = EventLog()
    let store = FakeSecretStore("S", log)
    let r = withSecrets "R" log [ store.Request("x", SecretRequestMode.ApplyOnly) ]
    r.ApplyError <- Some(Exception "Apply failure")

    let! report, output = runApply [ r ]

    Assert.False(Report.isSuccessful report, output)
    assertBefore "apply R" "dispose S" log
}

[<Fact>]
let ``Stores are closed after a cancelled execution``(): Task = task {
    let log = EventLog()
    let store = FakeSecretStore("S", log)
    let applyStarted = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let r = withSecrets "R" log [ store.Request("x", SecretRequestMode.ApplyOnly) ]
    r.OnApply <- fun () -> async {
        applyStarted.SetResult()
        do! Async.Sleep(TimeSpan.FromSeconds 10.0)
    }

    use cts = new CancellationTokenSource()
    use output = new StringWriter()
    let execution =
        Async.StartAsTask(Commands.apply (ExecutionUi.PlainUi output) [ r.Resource ], cancellationToken = cts.Token)
    do! applyStarted.Task.WaitAsync(TimeSpan.FromSeconds 10.0)
    cts.Cancel()

    let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(fun () -> execution :> Task)
    Assert.Contains("dispose S", log.Events)
    Assert.Single(store.Returned) |> ignore
    Assert.Throws<ObjectDisposedException>(fun () -> (Seq.head store.Returned).Length |> ignore) |> ignore
}

[<Fact>]
let ``Stores are compared by reference``(): Task = task {
    let log = EventLog()
    let a, b = EqualToEverythingStore("A", log), EqualToEverythingStore("B", log)
    Assert.True(a.Equals b)
    let r = withSecrets "R" log [ a.Request("x", SecretRequestMode.CheckOnly); b.Request("x", SecretRequestMode.CheckOnly) ]
    let received = recordSecrets r

    let! report, output = runCheck [ r ]

    Assert.True((Report.checkStatus report = NotAllApplied), output)
    Assert.Equal<string seq>([ "open A [x]"; "open B [x]" ], eventsStartingWith "open " log)
    Assert.Equal<Set<string * string * string>>(
        set [ "check", "x", "A:x"; "check", "x", "B:x" ],
        Set.ofSeq received
    )
}

[<Fact>]
let ``Secret references with equal contents are different keys``(): Task = task {
    let log = EventLog()
    let store = FakeSecretStore("S", log)
    let first, second = store.Ref "x", store.Ref "x"
    let r = withSecrets "R" log [
        { Secret = first; Mode = SecretRequestMode.CheckOnly }
        { Secret = second; Mode = SecretRequestMode.CheckOnly }
    ]
    let mutable count = 0
    r.OnCheckWithContext <- fun ctx -> async {
        count <- ctx.Secrets.Count
        Assert.Equal("S:x", Materialize ctx.Secrets[first])
        Assert.Equal("S:x", Materialize ctx.Secrets[second])
    }

    let! report, output = runCheck [ r ]

    Assert.True((Report.checkStatus report = NotAllApplied), output)
    Assert.Equal(2, count)
    Assert.Equal<string seq>([ "open S [x]" ], eventsStartingWith "open " log)
}

[<Fact>]
let ``The same secret reference listed twice is fetched once``(): Task = task {
    let log = EventLog()
    let store = FakeSecretStore("S", log)
    let secret = store.Ref "x"
    let r = withSecrets "R" log [
        { Secret = secret; Mode = SecretRequestMode.CheckOnly }
        { Secret = secret; Mode = SecretRequestMode.CheckAndApply }
    ]

    let! report, output = runCheck [ r ]

    Assert.True((Report.checkStatus report = NotAllApplied), output)
    Assert.Equal<string seq>([ "get S x" ], eventsStartingWith "get " log)
}
