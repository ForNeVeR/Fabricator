// SPDX-FileCopyrightText: 2021-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module internal Fabricator.Console.Commands

open System.Collections.Generic
open System.IO
open Fabricator.Console.Lowering
open Fabricator.Core

let private name(t: LoweredTask) = t.Resource.PresentableName

let private execute (output: TextWriter) (graph: TaskExecutor.TaskGraph<LoweredTask>)
                    : Async<IReadOnlyDictionary<LoweredTask, TaskOutcome>> =
    let onStarted(t: LoweredTask) =
        match t.Kind with
        | Check -> ()
        | Apply -> output.WriteLine $"{name t}: applying…"

    let report (t: LoweredTask) (outcome: TaskOutcome) =
        match t.Kind, outcome with
        | Check, CheckPassed -> output.WriteLine $"{name t}: already applied."
        | Check, CheckFailed -> output.WriteLine $"{name t}: not applied."
        | Check, Blocked -> output.WriteLine $"{name t}: skipped because a dependency has failed."
        | Apply, Applied -> output.WriteLine $"{name t}: applied."
        | _, Errored e -> output.WriteLine $"{name t}: error:\n{e}"
        | _ -> ()

    let locks = Lowering.createLocks graph
    TaskExecutor.execute graph (fun t inputs -> async {
        let! outcome = Lowering.run locks onStarted t inputs
        report t outcome
        return outcome
    })

let private isError = function
    | Errored _ -> true
    | _ -> false

/// Applies the resources and their dependencies that are not applied yet. Returns whether all the required actions
/// were successful.
let apply (output: TextWriter) (resources: Resource seq): Async<bool> = async {
    output.WriteLine "Applying changes to the current environment."
    let graph = Lowering.lower CheckAndApply resources
    let! results = execute (TextWriter.Synchronized output) graph
    return results.Values |> Seq.forall (function Errored _ | Blocked -> false | _ -> true)
}

type CheckStatus = AllApplied | NotAllApplied | CheckError

/// Checks the resources and all their dependencies.
let check (output: TextWriter) (resources: Resource seq): Async<CheckStatus> = async {
    output.WriteLine "Checking the current environment."
    let graph = Lowering.lower CheckOnly resources
    let! results = execute (TextWriter.Synchronized output) graph
    return
        if results.Values |> Seq.exists isError then CheckError
        elif results.Values |> Seq.contains CheckFailed then NotAllApplied
        else AllApplied
}
