// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

/// Parallel execution of a dependency graph of tasks, based on Kahn's algorithm.
module internal Fabricator.Console.TaskExecutor

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading.Tasks

/// <summary>
/// A task graph: each key maps to the list of its prerequisites, i.e., the tasks that should be completed before this
/// one starts.
/// </summary>
type TaskGraph<'Key> = IReadOnlyDictionary<'Key, IReadOnlyList<'Key>>

/// Builds a reverse dependency map: task -> list of tasks that have it as a prerequisite.
let private buildDependentsMap(graph: TaskGraph<'Key>): Dictionary<'Key, ResizeArray<'Key>> =
    let dependents = Dictionary<'Key, ResizeArray<'Key>>()
    for KeyValue(key, _) in graph do
        dependents[key] <- ResizeArray()
    for KeyValue(key, prerequisites) in graph do
        for prerequisite in prerequisites do
            match dependents.TryGetValue prerequisite with
            | true, list -> list.Add key
            | false, _ -> invalidArg (nameof graph) $"Task \"{key}\" has unknown prerequisite \"{prerequisite}\"."
    dependents

/// Runs Kahn's algorithm without executing anything, to make sure the graph contains no cycles.
let private verifyAcyclic (graph: TaskGraph<'Key>) (dependents: Dictionary<'Key, ResizeArray<'Key>>) =
    let inDegree = Dictionary<'Key, int>()
    for KeyValue(key, prerequisites) in graph do
        inDegree[key] <- prerequisites.Count

    let ready = Queue<'Key>()
    for KeyValue(key, degree) in inDegree do
        if degree = 0 then ready.Enqueue key

    let mutable processedCount = 0
    while ready.Count > 0 do
        let key = ready.Dequeue()
        processedCount <- processedCount + 1
        for dependent in dependents[key] do
            inDegree[dependent] <- inDegree[dependent] - 1
            if inDegree[dependent] = 0 then ready.Enqueue dependent

    if processedCount <> graph.Count then
        let stuck =
            inDegree
            |> Seq.filter (fun kv -> kv.Value > 0)
            |> Seq.map (fun kv -> string kv.Key)
            |> String.concat ", "
        invalidArg (nameof graph) $"The task graph contains a cycle involving the following tasks: {stuck}."

/// <summary>
/// Executes all the tasks from the <paramref name="graph"/> with maximum parallelism, starting each task as soon as
/// all its prerequisites have been completed.
/// </summary>
/// <param name="graph">The task graph. Every prerequisite should be a key of the graph, and the graph should contain
/// no cycles; both are verified before any task starts.</param>
/// <param name="action">The task action. Receives the task key and the results of its prerequisites (in the same
/// order as the prerequisites are listed in the graph).</param>
/// <returns>The results of all the tasks. Faults with the first exception thrown by any action; no new tasks are
/// started after that.</returns>
let execute
    (graph: TaskGraph<'Key>)
    (action: 'Key -> IReadOnlyList<'Key * 'Result> -> Task<'Result>)
    : Task<IReadOnlyDictionary<'Key, 'Result>> =
    let dependents = buildDependentsMap graph
    verifyAcyclic graph dependents

    let results = ConcurrentDictionary<'Key, 'Result>()
    let resultSnapshot() = Dictionary results :> IReadOnlyDictionary<_, _>
    if graph.Count = 0 then Task.FromResult(resultSnapshot()) else

    // Number of unfinished prerequisites for each task:
    let inDegree = Dictionary<'Key, int>()
    for KeyValue(key, prerequisites) in graph do
        inDegree[key] <- prerequisites.Count

    let stateLock = obj()
    let mutable completedCount = 0
    let tcs = TaskCompletionSource<IReadOnlyDictionary<'Key, 'Result>>(
        TaskCreationOptions.RunContinuationsAsynchronously
    )

    let rec start(key: 'Key): unit =
        Task.Run(Func<Task>(fun () -> processTask key)) |> ignore

    and processTask(key: 'Key): Task = task {
        try
            let inputs =
                graph[key]
                |> Seq.map (fun prerequisite -> prerequisite, results[prerequisite])
                |> Seq.toArray
            let! result = action key inputs
            results[key] <- result

            let readyTasks = ResizeArray()
            lock stateLock (fun () ->
                completedCount <- completedCount + 1
                for dependent in dependents[key] do
                    inDegree[dependent] <- inDegree[dependent] - 1
                    if inDegree[dependent] = 0 then
                        readyTasks.Add dependent

                if completedCount = graph.Count then
                    tcs.TrySetResult(resultSnapshot()) |> ignore
            )

            if not tcs.Task.IsCompleted then
                for ready in readyTasks do
                    start ready
        with
        | ex -> tcs.TrySetException ex |> ignore
    }

    for KeyValue(key, degree) in Seq.toArray inDegree do
        if degree = 0 then start key

    tcs.Task
