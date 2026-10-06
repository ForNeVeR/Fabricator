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
        Resource: IResource
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
let private collectAll(roots: IResource seq): HashSet<IResource> =
    let visited = HashSet<IResource>()
    let rec walk(resource: IResource) =
        if visited.Add resource then
            for dependency in resource.DependsOn do
                walk dependency
    for root in roots do
        walk root
    visited

/// <summary>Finds a dependency cycle in the resource graph reachable from <paramref name="roots"/>.</summary>
/// <param name="roots">The roots of the resource graph.</param>
/// <returns>
/// The resources forming the cycle, with the first resource repeated in the end (e.g. <c>[A; B; A]</c>); or
/// <c>None</c> if there are no cycles.
/// </returns>
let findCycle(roots: IResource seq): IResource[] option =
    let finished = HashSet<IResource>()
    let onPath = HashSet<IResource>()
    let path = Stack<IResource>()

    let rec visit(resource: IResource): IResource[] option =
        if finished.Contains resource then None
        elif onPath.Contains resource then
            let cycleTail =
                path
                |> Seq.takeWhile (fun r -> r <> resource) // Stack enumerates from the top
                |> Seq.rev
            let cycle = seq {
                yield resource
                yield! cycleTail
                yield resource
            }

            Some(cycle |> Seq.toArray)
        else
            onPath.Add resource |> ignore
            path.Push resource
            let result = resource.DependsOn |> Seq.tryPick visit
            path.Pop() |> ignore
            onPath.Remove resource |> ignore
            finished.Add resource |> ignore
            result

    roots |> Seq.tryPick visit

/// Collects all the resources depending on the passed one, directly or transitively.
let private collectDependents (dependents: Dictionary<IResource, ResizeArray<IResource>>) (resource: IResource) =
    let visited = HashSet<IResource>()
    let rec walk(r: IResource) =
        for dependent in dependents[r] do
            if visited.Add dependent then
                walk dependent
    walk resource
    visited

let private buildGraph (mode: ExecutionMode) (roots: IResource[]): TaskExecutor.TaskGraph<LoweredTask> =
    let check r = { Kind = Check; Resource = r }
    let apply r = { Kind = Apply; Resource = r }

    let allResources = collectAll roots
    let dependents = Dictionary<IResource, ResizeArray<IResource>>()
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
/// <returns>The lowered graph, or an error containing a dependency cycle in the resource graph.</returns>
let lower (mode: ExecutionMode) (roots: IResource seq): Result<TaskExecutor.TaskGraph<LoweredTask>, IResource[]> =
    let roots = Seq.toArray roots
    match findCycle roots with
    | Some cycle -> Error cycle
    | None -> Ok(buildGraph mode roots)

let private isFailure = function
    | Errored _ | Blocked -> true
    | CheckPassed | CheckFailed | Applied | NotRequired -> false

let private runCheck(resource: IResource): Task<TaskOutcome> = task {
    try
        let! applied = Async.StartAsTask(resource.AlreadyApplied())
        return if applied then CheckPassed else CheckFailed
    with
    | ex -> return Errored ex
}

let private runApply(resource: IResource): Task<TaskOutcome> = task {
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
