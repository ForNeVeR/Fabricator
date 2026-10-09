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
    ui.Run(header, title, graph.Count, fun view ->
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
                view.TaskFinished()
        })
    )

/// Applies the resources and their dependencies that are not applied yet. Returns whether all the required actions
/// were successful.
let apply (ui: IExecutionUi) (resources: Resource seq): Async<bool> = async {
    let graph = lower CheckAndApply resources
    let! results = execute ui "Applying changes to the current environment." "Applying" graph
    return results.Values |> Seq.forall (function Errored _ | Blocked -> false | _ -> true)
}

type CheckStatus = AllApplied | NotAllApplied | CheckError

let private isError = function
    | Errored _ -> true
    | _ -> false

/// Checks the resources and all their dependencies.
let check (ui: IExecutionUi) (resources: Resource seq): Async<CheckStatus> = async {
    let graph = lower CheckOnly resources
    let! results = execute ui "Checking the current environment." "Checking" graph
    return
        if results.Values |> Seq.exists isError then CheckError
        elif results.Values |> Seq.contains CheckFailed then NotAllApplied
        else AllApplied
}
