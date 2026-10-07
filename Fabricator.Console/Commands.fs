// SPDX-FileCopyrightText: 2021-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module internal Fabricator.Console.Commands

open System.Collections.Generic
open System.IO
open System.Threading.Tasks
open Fabricator.Console.Lowering
open Fabricator.Core

let private name(t: LoweredTask) = t.Resource.PresentableName

let private reportCycle (output: TextWriter) (cycle: IResource seq) =
    let path = cycle |> Seq.map _.PresentableName |> String.concat " → "
    output.WriteLine $"Dependency cycle detected: {path}."

let private execute (output: TextWriter) (graph: TaskExecutor.TaskGraph<LoweredTask>)
                    : Task<IReadOnlyDictionary<LoweredTask, TaskOutcome>> =
    let onStarted(t: LoweredTask) =
        match t.Kind with
        | Check -> ()
        | Apply -> output.WriteLine $"{name t}: applying…"

    let report (t: LoweredTask) (inputs: IReadOnlyList<LoweredTask * TaskOutcome>) (outcome: TaskOutcome) =
        match t.Kind, outcome with
        | Check, CheckPassed -> output.WriteLine $"{name t}: already applied."
        | Check, CheckFailed -> output.WriteLine $"{name t}: not applied."
        | Apply, Applied -> output.WriteLine $"{name t}: applied."
        | Apply, Blocked when inputs |> Seq.contains ({ t with Kind = Check }, CheckFailed) ->
            // Only report the tasks blocked by dependencies, not by the resource's own check failure.
            output.WriteLine $"{name t}: skipped because a dependency has failed."
        | _, Errored e -> output.WriteLine $"{name t}: error:\n{e}"
        | _ -> ()

    TaskExecutor.execute graph (fun t inputs -> task {
        let! outcome = Lowering.run onStarted t inputs
        report t inputs outcome
        return outcome
    })

let private isError = function
    | Errored _ -> true
    | _ -> false

/// Applies the resources and their dependencies that are not applied yet. Returns whether all the required actions
/// were successful.
let apply (output: TextWriter) (resources: IResource seq): Task<bool> = task {
    output.WriteLine "Applying changes to the current environment."
    match Lowering.lower CheckAndApply resources with
    | Error cycle ->
        reportCycle output cycle
        return false
    | Ok graph ->
        let! results = execute (TextWriter.Synchronized output) graph
        return results.Values |> Seq.forall (function Errored _ | Blocked -> false | _ -> true)
}

type CheckStatus = AllApplied | NotAllApplied | CheckError

/// Checks the resources and all their dependencies.
let check (output: TextWriter) (resources: IResource seq): Task<CheckStatus> = task {
    output.WriteLine "Checking the current environment."
    match Lowering.lower CheckOnly resources with
    | Error cycle ->
        reportCycle output cycle
        return CheckError
    | Ok graph ->
        let! results = execute (TextWriter.Synchronized output) graph
        return
            if results.Values |> Seq.exists isError then CheckError
            elif results.Values |> Seq.contains CheckFailed then NotAllApplied
            else AllApplied
}
