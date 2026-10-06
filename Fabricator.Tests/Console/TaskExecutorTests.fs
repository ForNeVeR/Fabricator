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

let private sumInputs(inputs: IReadOnlyList<string * int>) = inputs |> Seq.sumBy snd

[<Fact>]
let ``Single task with no prerequisites executes successfully``(): Task = task {
    let! results = TaskExecutor.execute (graph [ "single", [] ]) (fun _ _ -> Task.FromResult 42)
    Assert.Equal(42, results["single"])
    Assert.Equal(1, results.Count)
}

[<Fact>]
let ``Empty graph completes immediately``(): Task = task {
    let called = ref false
    let! results = TaskExecutor.execute (graph []) (fun _ _ -> called.Value <- true; Task.FromResult 0)
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

    let! _ = TaskExecutor.execute tasks (fun key _ -> task {
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

    let! results = TaskExecutor.execute tasks (fun key inputs -> task {
        if key = "A" then return sumInputs inputs else
        if Interlocked.Increment &started.contents = 3 then allStarted.SetResult()
        do! allStarted.Task.WaitAsync(TimeSpan.FromSeconds 10.0)
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

    let! results = TaskExecutor.execute tasks (fun key inputs -> task {
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
        TaskExecutor.execute tasks (fun key _ -> task {
            executed.Enqueue key
            if key = "failing" then failwith "Intentional failure"
            return 0
        }) :> Task
    )

    Assert.Contains("Intentional failure", ex.Message)
    Assert.Equal<string list>([ "failing" ], Seq.toList executed)
}

[<Fact>]
let ``Unknown prerequisite is rejected before execution``(): unit =
    let called = ref false
    let tasks = graph [
        "A", [ "missing" ]
        "B", []
    ]
    let ex = Assert.Throws<ArgumentException>(fun () ->
        TaskExecutor.execute tasks (fun _ _ -> called.Value <- true; Task.FromResult 0) |> ignore
    )
    Assert.Contains("missing", ex.Message)
    Assert.False called.Value

[<Fact>]
let ``Cycle is rejected before execution``(): unit =
    let called = ref false
    let tasks = graph [
        "A", [ "B" ]
        "B", [ "C" ]
        "C", [ "A" ]
        "independent", []
    ]
    let ex = Assert.Throws<ArgumentException>(fun () ->
        TaskExecutor.execute tasks (fun _ _ -> called.Value <- true; Task.FromResult 0) |> ignore
    )
    Assert.Contains("cycle", ex.Message)
    Assert.False called.Value
