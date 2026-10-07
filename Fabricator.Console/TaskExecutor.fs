// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

/// Structured parallel execution of a dependency graph of tasks.
module internal Fabricator.Console.TaskExecutor

open System
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
/// <returns>The results of all the tasks.</returns>
/// <remarks>
/// All the task actions are run as child computations of the returned one, and inherit its cancellation token. If any
/// action fails, the other actions are cancelled, and the returned computation fails with the first exception only
/// after all of the actions have finished; same for the cancellation. So, no actions are left running after the
/// returned computation has completed, no matter how it completed.
/// </remarks>
let execute
    (graph: TaskGraph<'Key>)
    (action: 'Key -> IReadOnlyList<'Key * 'Result> -> Async<'Result>)
    : Async<IReadOnlyDictionary<'Key, 'Result>> = async {
    let dependents = buildDependentsMap graph
    verifyAcyclic graph dependents

    let completions = Dictionary<'Key, TaskCompletionSource<'Result>>()
    for KeyValue(key, _) in graph do
        completions[key] <- TaskCompletionSource<'Result>(TaskCreationOptions.RunContinuationsAsynchronously)

    let processTask(key: 'Key) = async {
        let completion = completions[key]
        try
            let! ct = Async.CancellationToken
            let inputs = ResizeArray()
            for prerequisite in graph[key] do
                let! result = Async.AwaitTask(completions[prerequisite].Task.WaitAsync ct)
                inputs.Add(prerequisite, result)

            let! result = action key inputs
            completion.SetResult result
            return key, result
        with
        | :? OperationCanceledException as e ->
            completion.TrySetCanceled() |> ignore
            raise e
            return Unchecked.defaultof<_> // unreachable
    }

    let! results = graph.Keys |> Seq.map processTask |> Async.Parallel
    let resultMap = Dictionary<'Key, 'Result>()
    for key, result in results do
        resultMap[key] <- result
    return resultMap :> IReadOnlyDictionary<_, _>
}
