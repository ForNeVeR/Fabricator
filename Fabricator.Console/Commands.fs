// SPDX-FileCopyrightText: 2021-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module internal Fabricator.Console.Commands

open System.Collections.Generic
open Fabricator.Console.ExecutionUi
open Fabricator.Console.Lowering
open Fabricator.Core

let private name(t: LoweredTask) = t.Resource.PresentableName

/// The message to log after the task has finished with the outcome.
let private outcomeMessage (t: LoweredTask) (outcome: TaskOutcome): string option =
    match t.Kind, outcome with
    | Check, CheckPassed -> Some "already applied."
    | Check, CheckFailed -> Some "not applied."
    | Check, Blocked -> Some "skipped because a dependency has failed."
    | Apply, Applied -> Some "applied."
    | _, Errored e -> Some $"error:\n{e}"
    | _ -> None

let private execute (ui: IExecutionUi) (header: string) (title: string) (graph: TaskExecutor.TaskGraph<LoweredTask>)
                    : Async<IReadOnlyDictionary<LoweredTask, TaskOutcome>> =
    let locks = createLocks graph
    // The last task of its resource: the apply task, or the check task if the resource is only checked.
    let isFinal(t: LoweredTask) = t.Kind = Apply || not(graph.ContainsKey { t with Kind = Apply })
    let totalResources = graph.Keys |> Seq.filter isFinal |> Seq.length
    ui.Run(header, title, totalResources, fun view ->
        TaskExecutor.execute graph (fun t inputs -> async {
            let mutable taskView = None
            let start(t: LoweredTask) =
                let started = view.StartTask(name t)
                taskView <- Some started
                if t.Kind = Apply then started.Reporter.Log "applying…"
                { Reporter = started.Reporter }

            try
                let! outcome = run locks start t inputs
                let message = outcomeMessage t outcome
                match taskView with
                | Some started ->
                    taskView <- None
                    started.Complete message
                | None -> message |> Option.iter (fun m -> view.LogInstant(name t, m))
                return outcome
            finally
                // Make sure the task's log is closed even on cancellation, so it doesn't hold the next logs back.
                taskView |> Option.iter (fun (started: ITaskView) -> started.Complete None)
                if isFinal t then view.ResourceFinished()
        })
    )

/// Applies the resources and their dependencies that are not applied yet. Returns the report of the final state of
/// every resource.
let apply (ui: IExecutionUi) (resources: Resource seq): Async<Report> = async {
    let resources = Seq.toArray resources
    let graph = lower CheckAndApply resources
    let! results = execute ui "Applying changes to the current environment." "Applying" graph
    return Report.create CheckAndApply resources results
}

/// Checks the resources and all their dependencies. Returns the report of the state of every resource.
let check (ui: IExecutionUi) (resources: Resource seq): Async<Report> = async {
    let resources = Seq.toArray resources
    let graph = lower CheckOnly resources
    let! results = execute ui "Checking the current environment." "Checking" graph
    return Report.create CheckOnly resources results
}
