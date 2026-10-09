// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

/// Conversion of the resource dependency graph to a graph of smaller tasks suitable for the task executor, and the
/// execution semantics of these tasks.
module internal Fabricator.Console.Lowering

open System
open System.Collections.Generic
open System.Threading
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

/// <summary>
/// Converts the resource graph reachable from <paramref name="roots"/> to a graph of lowered tasks.
/// </summary>
/// <param name="mode">The execution mode.</param>
/// <param name="roots">The roots of the resource graph.</param>
/// <remarks>
/// <para>For each resource (including the transitive dependencies of the roots), a check task is created. In
/// <see cref="F:Fabricator.Console.Lowering.ExecutionMode.CheckOnly"/> mode, checks are independent of each other, so
/// the check tasks have no prerequisites.</para>
/// <para>In <see cref="F:Fabricator.Console.Lowering.ExecutionMode.CheckAndApply"/> mode, for each resource an apply
/// task is also created, with the check task of the same resource as its only prerequisite. The prerequisites of a
/// check task are the apply tasks of all the resource's dependencies: this way, a resource is only checked when all its
/// dependencies are already in their desired state.</para>
/// </remarks>
/// <returns>The lowered graph.</returns>
let lower (mode: ExecutionMode) (roots: Resource seq): TaskExecutor.TaskGraph<LoweredTask> =
    let check r = { Kind = Check; Resource = r }
    let apply r = { Kind = Apply; Resource = r }

    let tasks = Dictionary<LoweredTask, IReadOnlyList<LoweredTask>>()
    for resource in collectAll roots do
        match mode with
        | CheckOnly ->
            tasks[check resource] <- Array.empty
        | CheckAndApply ->
            tasks[check resource] <- [| for dependency in resource.DependsOn -> apply dependency |]
            tasks[apply resource] <- [| check resource |]

    tasks

let private isFailure = function
    | Errored _ | Blocked -> true
    | CheckPassed | CheckFailed | Applied | NotRequired -> false

/// Whether the exception should be reported as a resource failure, as opposed to the execution cancellation.
let private isResourceError (ct: CancellationToken) (ex: exn) =
    not (ex :? OperationCanceledException && ct.IsCancellationRequested)

let private runCheck (resource: Resource) (context: ResourceContext): Async<TaskOutcome> = async {
    let! ct = Async.CancellationToken
    try
        let! applied = resource.AlreadyApplied context
        return if applied then CheckPassed else CheckFailed
    with
    | ex when isResourceError ct ex -> return Errored ex
}

let private runApply (resource: Resource) (context: ResourceContext): Async<TaskOutcome> = async {
    let! ct = Async.CancellationToken
    try
        do! resource.Apply context
        return Applied
    with
    | ex when isResourceError ct ex -> return Errored ex
}

/// Semaphores of the resource concurrency groups, one per group.
type Locks = IReadOnlyDictionary<ConcurrencyGroup, SemaphoreSlim>

/// Creates the semaphores for all the concurrency groups of the resources from the graph.
let createLocks(graph: TaskExecutor.TaskGraph<LoweredTask>): Locks =
    upcast (
        graph.Keys
        |> Seq.collect(fun task -> task.Resource.Lock |> Option.toArray)
        |> Seq.distinct
        |> Seq.map(fun group -> KeyValuePair(group, new SemaphoreSlim(1, 1)))
        |> Dictionary
    )

/// <summary>
/// Runs the action while holding the semaphore of the resource's concurrency group, if the resource has one.
/// </summary>
/// <remarks>
/// Every action holds at most one semaphore, and the semaphore is only taken after all the task's prerequisites have
/// completed, so the actions waiting for the semaphores cannot deadlock.
/// </remarks>
let private withLock (locks: Locks) (resource: Resource) (action: unit -> Async<TaskOutcome>) = async {
    match resource.Lock with
    | None -> return! action()
    | Some group ->
        let semaphore = locks[group]
        let! ct = Async.CancellationToken
        do! Async.AwaitTask(semaphore.WaitAsync ct)
        try
            return! action()
        finally
            semaphore.Release() |> ignore
}

/// <summary>Executes a lowered task according to the results of its prerequisites.</summary>
/// <remarks>
/// A check task runs only if none of its prerequisites (the apply tasks of the resource's dependencies) have failed
/// or have been blocked. An apply task runs only if the resource's own check has returned false. The actual resource
/// action (check or apply) is run while holding the semaphore of the resource's concurrency group.
/// </remarks>
/// <param name="locks">
/// The semaphores of the concurrency groups, see <see cref="M:Fabricator.Console.Lowering.createLocks"/>.
/// </param>
/// <param name="start">
/// Called right before the actual resource action (check or apply) starts. Returns the context to pass to the action.
/// </param>
/// <param name="loweredTask">The task to execute.</param>
/// <param name="inputs">The results of the task's prerequisites.</param>
let run
    (locks: Locks)
    (start: LoweredTask -> ResourceContext)
    (loweredTask: LoweredTask)
    (inputs: IReadOnlyList<LoweredTask * TaskOutcome>)
    : Async<TaskOutcome> =
    let resource = loweredTask.Resource
    match loweredTask.Kind with
    | Check when inputs |> Seq.exists (snd >> isFailure) -> async.Return Blocked
    | Check ->
        withLock locks resource (fun () -> runCheck resource (start loweredTask))
    | Apply ->
        let ownCheck =
            inputs
            |> Seq.tryPick (fun (t, outcome) -> if t.Kind = Check && t.Resource = resource then Some outcome else None)
            |> Option.defaultWith (fun () ->
                raise <| InvalidOperationException $"Check result not found for task \"{loweredTask}\".")
        match ownCheck with
        | CheckPassed -> async.Return NotRequired
        | Errored _ | Blocked -> async.Return Blocked
        | CheckFailed ->
            withLock locks resource (fun () -> runApply resource (start loweredTask))
        | Applied | NotRequired ->
            raise <| InvalidOperationException $"Unexpected check outcome for task \"{loweredTask}\": {ownCheck}."
