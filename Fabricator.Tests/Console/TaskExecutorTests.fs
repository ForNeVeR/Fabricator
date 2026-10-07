// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Console.TaskExecutorTests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Fabricator.Console
open Xunit

let private graph(edges: (string * string list) list): TaskExecutor.TaskGraph<string> =
    let result = Dictionary<string, IReadOnlyList<string>>()
    for key, prerequisites in edges do
        result[key] <- Seq.toArray prerequisites
    result

let private execute tasks action = TaskExecutor.execute tasks action |> Async.StartAsTask

let private sumInputs(inputs: IReadOnlyList<string * int>) = inputs |> Seq.sumBy snd

let private timeout = TimeSpan.FromSeconds 10.0

[<Fact>]
let ``Single task with no prerequisites executes successfully``(): Task = task {
    let! results = execute (graph [ "single", [] ]) (fun _ _ -> async.Return 42)
    Assert.Equal(42, results["single"])
    Assert.Equal(1, results.Count)
}

[<Fact>]
let ``Empty graph completes immediately``(): Task = task {
    let called = ref false
    let! results = execute (graph []) (fun _ _ -> called.Value <- true; async.Return 0)
    Assert.Empty results
    Assert.False called.Value
}

[<Fact>]
let ``Prerequisites execute before dependents``(): Task = task {
    let executionOrder = ConcurrentQueue<string>()
    let tasks = graph [
        "A", [ "B"; "C" ]
        "B", [ "D" ]
        "C", [ "D" ]
        "D", []
    ]

    let! _ = execute tasks (fun key _ -> async {
        executionOrder.Enqueue key
        return 0
    })

    let order = Seq.toList executionOrder
    let indexOf name = List.findIndex ((=) name) order
    Assert.Equal(4, order.Length)
    Assert.True(indexOf "D" < indexOf "B", $"D should be before B: {order}")
    Assert.True(indexOf "D" < indexOf "C", $"D should be before C: {order}")
    Assert.True(indexOf "B" < indexOf "A", $"B should be before A: {order}")
    Assert.True(indexOf "C" < indexOf "A", $"C should be before A: {order}")
}

[<Fact>]
let ``Independent tasks run concurrently``(): Task = task {
    // Each of B, C, D waits until all three have started; this only completes if they run concurrently.
    let started = ref 0
    let allStarted = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let tasks = graph [
        "A", [ "B"; "C"; "D" ]
        "B", []
        "C", []
        "D", []
    ]

    let! results = execute tasks (fun key inputs -> async {
        if key = "A" then return sumInputs inputs else
        if Interlocked.Increment &started.contents = 3 then allStarted.SetResult()
        do! Async.AwaitTask(allStarted.Task.WaitAsync timeout)
        return 1
    })

    Assert.Equal(3, results["A"])
}

[<Fact>]
let ``Diamond dependency pattern passes results and executes each task once``(): Task = task {
    let executionCounts = ConcurrentDictionary<string, int>()
    //     A
    //    / \
    //   B   C
    //    \ /
    //     D
    let tasks = graph [
        "A", [ "B"; "C" ]
        "B", [ "D" ]
        "C", [ "D" ]
        "D", []
    ]

    let! results = execute tasks (fun key inputs -> async {
        executionCounts.AddOrUpdate(key, 1, fun _ v -> v + 1) |> ignore
        return
            match key with
            | "D" -> 1
            | "B" -> sumInputs inputs + 1
            | "C" -> sumInputs inputs + 10
            | "A" ->
                Assert.Equal<string list>([ "B"; "C" ], inputs |> Seq.map fst |> Seq.toList)
                sumInputs inputs
            | other -> failwithf $"Unexpected task {other}."
    })

    // D=1, B=D+1=2, C=D+10=11, A=B+C=13
    Assert.Equal(13, results["A"])
    for key in [ "A"; "B"; "C"; "D" ] do
        Assert.Equal(1, executionCounts[key])
}

[<Fact>]
let ``Task failure propagates exception and dependents are not executed``(): Task = task {
    let executed = ConcurrentQueue<string>()
    let tasks = graph [
        "root", [ "failing" ]
        "failing", []
    ]

    let! ex = Assert.ThrowsAnyAsync<Exception>(fun () ->
        execute tasks (fun key _ -> async {
            executed.Enqueue key
            if key = "failing" then failwith "Intentional failure"
            return 0
        }) :> Task
    )

    Assert.Contains("Intentional failure", ex.Message)
    Assert.Equal<string list>([ "failing" ], Seq.toList executed)
}

[<Fact>]
let ``Task failure cancels the running tasks and waits for them to finish``(): Task = task {
    let runningStarted = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let runningFinished = ref false
    let tasks = graph [
        "failing", []
        "running", []
    ]

    let! ex = Assert.ThrowsAnyAsync<Exception>(fun () ->
        execute tasks (fun key _ -> async {
            match key with
            | "failing" ->
                do! Async.AwaitTask(runningStarted.Task.WaitAsync timeout)
                return failwith "Intentional failure"
            | _ ->
                try
                    runningStarted.SetResult()
                    do! Async.Sleep timeout
                    return 0
                finally
                    // Make sure the executor waits for this task even if it takes time to stop.
                    Thread.Sleep 100
                    runningFinished.Value <- true
        }) :> Task
    )

    Assert.Contains("Intentional failure", ex.Message)
    Assert.True(runningFinished.Value, "The running task should have finished before the execution completed.")
}

[<Fact>]
let ``Cancellation reaches the running tasks``(): Task = task {
    use cts = new CancellationTokenSource()
    let runningStarted = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let observedCancellation = ref false
    let tasks = graph [
        "dependent", [ "running" ]
        "running", []
    ]
    let executed = ConcurrentQueue<string>()

    let execution =
        Async.StartAsTask(
            TaskExecutor.execute tasks (fun key _ -> async {
                executed.Enqueue key
                let! ct = Async.CancellationToken
                try
                    runningStarted.SetResult()
                    do! Async.Sleep timeout
                finally
                    observedCancellation.Value <- ct.IsCancellationRequested
                return 0
            }),
            cancellationToken = cts.Token
        )
    do! runningStarted.Task.WaitAsync timeout
    cts.Cancel()

    let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(fun () -> execution :> Task)
    Assert.True(observedCancellation.Value, "The running task should observe the cancellation.")
    Assert.Equal<string list>([ "running" ], Seq.toList executed)
}

[<Fact>]
let ``Unknown prerequisite is rejected before execution``(): Task = task {
    let called = ref false
    let tasks = graph [
        "A", [ "missing" ]
        "B", []
    ]
    let! ex = Assert.ThrowsAsync<ArgumentException>(fun () ->
        execute tasks (fun _ _ -> called.Value <- true; async.Return 0) :> Task
    )
    Assert.Contains("missing", ex.Message)
    Assert.False called.Value
}

[<Fact>]
let ``Cycle is rejected before execution``(): Task = task {
    let called = ref false
    let tasks = graph [
        "A", [ "B" ]
        "B", [ "C" ]
        "C", [ "A" ]
        "independent", []
    ]
    let! ex = Assert.ThrowsAsync<ArgumentException>(fun () ->
        execute tasks (fun _ _ -> called.Value <- true; async.Return 0) :> Task
    )
    Assert.Contains("cycle", ex.Message)
    Assert.False called.Value
}
