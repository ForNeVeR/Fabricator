// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

/// Conversion of the resource dependency graph to a graph of smaller tasks suitable for the task executor, and the
/// execution semantics of these tasks.
module internal Fabricator.Console.Lowering

open System
open System.Collections.Generic
open System.Threading.Tasks
open Fabricator.Core

type ExecutionMode =
    /// Only check the resources.
    | CheckOnly
    /// Check the resources and apply the ones that are not applied yet.
    | CheckAndApply

type TaskKind =
    | Check
    | Apply

type LoweredTask =
    {
        Kind: TaskKind
        Resource: Resource
    }
    override this.ToString() = $"{this.Kind} {this.Resource.PresentableName}"


type TaskOutcome =
    /// The resource check has returned true.
    | CheckPassed
    /// The resource check has returned false.
    | CheckFailed
    /// The resource has been applied successfully.
    | Applied
    /// The task was not required to run.
    | NotRequired
    /// The task has failed.
    | Errored of exn
    /// The task was not run because some of the tasks it requires have failed.
    | Blocked

/// Collects all the resources reachable from the roots (including the roots themselves).
let private collectAll(roots: Resource seq): HashSet<Resource> =
    let visited = HashSet<Resource>()
    let rec walk(resource: Resource) =
        if visited.Add resource then
            for dependency in resource.DependsOn do
                walk dependency
    for root in roots do
        walk root
    visited

/// Collects all the resources depending on the passed one, directly or transitively.
let private collectDependents (dependents: Dictionary<Resource, ResizeArray<Resource>>) (resource: Resource) =
    let visited = HashSet<Resource>()
    let rec walk(r: Resource) =
        for dependent in dependents[r] do
            if visited.Add dependent then
                walk dependent
    walk resource
    visited

/// <summary>
/// Converts the resource graph reachable from <paramref name="roots"/> to a graph of lowered tasks.
/// </summary>
/// <param name="mode">The execution mode.</param>
/// <param name="roots">The roots of the resource graph.</param>
/// <remarks>
/// <para>For each resource (including the transitive dependencies of the roots), a check task is created. Checks are
/// independent of each other, so the check tasks have no prerequisites.</para>
/// <para>In <see cref="F:Fabricator.Console.Lowering.ExecutionMode.CheckAndApply"/> mode, for each resource an apply
/// task is also created. Its prerequisites are the check task of the same resource, the apply tasks of all the
/// resource's dependencies, and the check tasks of all the resources depending on it (directly or transitively): this
/// way, a resource is never changed while a resource depending on it is still being checked.</para>
/// </remarks>
/// <returns>The lowered graph.</returns>
let lower (mode: ExecutionMode) (roots: Resource seq): TaskExecutor.TaskGraph<LoweredTask> =
    let check r = { Kind = Check; Resource = r }
    let apply r = { Kind = Apply; Resource = r }

    let allResources = collectAll roots
    let dependents = Dictionary<Resource, ResizeArray<Resource>>()
    for resource in allResources do
        dependents[resource] <- ResizeArray()
    for resource in allResources do
        for dependency in resource.DependsOn do
            dependents[dependency].Add resource

    let tasks = Dictionary<LoweredTask, IReadOnlyList<LoweredTask>>()
    for resource in allResources do
        tasks[check resource] <- Array.empty
        match mode with
        | CheckOnly -> ()
        | CheckAndApply ->
            tasks[apply resource] <- [|
                check resource
                for dependency in resource.DependsOn do
                    apply dependency
                for dependent in collectDependents dependents resource do
                    check dependent
            |]

    tasks

let private isFailure = function
    | Errored _ | Blocked -> true
    | CheckPassed | CheckFailed | Applied | NotRequired -> false

let private runCheck(resource: Resource): Task<TaskOutcome> = task {
    try
        let! applied = Async.StartAsTask(resource.AlreadyApplied())
        return if applied then CheckPassed else CheckFailed
    with
    | ex -> return Errored ex
}

let private runApply(resource: Resource): Task<TaskOutcome> = task {
    try
        do! Async.StartAsTask(resource.Apply())
        return Applied
    with
    | ex -> return Errored ex
}

/// <summary>Executes a lowered task according to the results of its prerequisites.</summary>
/// <remarks>
/// Check tasks always run. An apply task runs only if the resource's own check has returned false and none of the
/// apply tasks of its dependencies have failed or have been blocked. Results of the checks of other resources (the
/// ones depending on this resource) are ignored.
/// </remarks>
/// <param name="onStarted">Called before the actual resource action (check or apply) starts.</param>
/// <param name="loweredTask">The task to execute.</param>
/// <param name="inputs">The results of the task's prerequisites.</param>
let run
    (onStarted: LoweredTask -> unit)
    (loweredTask: LoweredTask)
    (inputs: IReadOnlyList<LoweredTask * TaskOutcome>)
    : Task<TaskOutcome> =
    let resource = loweredTask.Resource
    match loweredTask.Kind with
    | Check ->
        onStarted loweredTask
        runCheck resource
    | Apply ->
        let ownCheck =
            inputs
            |> Seq.tryPick (fun (t, outcome) -> if t.Kind = Check && t.Resource = resource then Some outcome else None)
            |> Option.defaultWith (fun () ->
                raise <| InvalidOperationException $"Check result not found for task \"{loweredTask}\".")
        match ownCheck with
        | CheckPassed -> Task.FromResult NotRequired
        | Errored _ -> Task.FromResult Blocked
        | CheckFailed when inputs |> Seq.exists (fun (t, outcome) -> t.Kind = Apply && isFailure outcome) ->
            Task.FromResult Blocked
        | CheckFailed ->
            onStarted loweredTask
            runApply resource
        | Applied | NotRequired | Blocked ->
            raise <| InvalidOperationException $"Unexpected check outcome for task \"{loweredTask}\": {ownCheck}."
