// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Console

open System
open System.Collections.Generic
open Fabricator.Console.Lowering
open Fabricator.Core
open Spectre.Console

/// The final state of a resource after an execution.
[<RequireQualifiedAccess>]
type internal ReportItemState =
    /// The resource check has passed, no action required.
    | AlreadyApplied
    /// The resource check has not passed (only reported when checking).
    | NotApplied
    /// The resource has been applied successfully.
    | Applied
    /// The resource was not processed because some of its dependencies have failed.
    | Skipped
    /// The resource check has failed with an error.
    | CheckFailed
    /// The resource application has failed with an error.
    | ApplyFailed

/// A line of the report: a resource and its final state.
type internal ReportItem =
    {
        ResourceName: string
        State: ReportItemState
    }

/// The final states of all the resources processed by an execution.
type internal Report =
    {
        Items: ReportItem list
    }

/// The overall result of a check execution.
type internal CheckStatus = AllApplied | NotAllApplied | CheckError

module internal Report =

    /// <summary>
    /// Lists all the resources reachable from the roots, each one once, with the dependencies going before their
    /// dependents.
    /// </summary>
    /// <remarks>
    /// The dependencies of a resource are ordered by name, since the order of
    /// <see cref="P:Fabricator.Core.Resource.DependsOn"/> is not stable between runs.
    /// </remarks>
    let private orderResources(roots: Resource seq): Resource list =
        let visited = HashSet<Resource>()
        let result = ResizeArray<Resource>()
        let rec walk(resource: Resource) =
            if visited.Add resource then
                for dependency in resource.DependsOn |> Seq.sortWith (fun a b ->
                    String.CompareOrdinal(a.PresentableName, b.PresentableName)) do
                    walk dependency
                result.Add resource
        for root in roots do
            walk root
        List.ofSeq result

    let private unexpected (resource: Resource) (outcomes: 'a): 'b =
        raise <| InvalidOperationException $"Unexpected outcome for resource \"{resource}\": {outcomes}."

    let private checkState (resource: Resource) (outcome: TaskOutcome) =
        match outcome with
        | CheckPassed -> ReportItemState.AlreadyApplied
        | CheckFailed -> ReportItemState.NotApplied
        | Errored _ -> ReportItemState.CheckFailed
        | Blocked -> ReportItemState.Skipped
        | Applied | NotRequired -> unexpected resource outcome

    let private applyState (resource: Resource) (checkOutcome: TaskOutcome) (applyOutcome: TaskOutcome) =
        match checkOutcome, applyOutcome with
        | CheckPassed, NotRequired -> ReportItemState.AlreadyApplied
        | CheckFailed, Applied -> ReportItemState.Applied
        | CheckFailed, Errored _ -> ReportItemState.ApplyFailed
        | Errored _, Blocked -> ReportItemState.CheckFailed
        | Blocked, Blocked -> ReportItemState.Skipped
        | _ -> unexpected resource (checkOutcome, applyOutcome)

    /// <summary>Creates the report of an execution.</summary>
    /// <param name="mode">The execution mode.</param>
    /// <param name="roots">The root resources of the execution.</param>
    /// <param name="results">
    /// The outcomes of all the tasks of the execution, see <see cref="M:Fabricator.Console.Lowering.lower"/>.
    /// </param>
    let create
        (mode: ExecutionMode)
        (roots: Resource seq)
        (results: IReadOnlyDictionary<LoweredTask, TaskOutcome>)
        : Report =
        let state(resource: Resource) =
            let checkOutcome = results[{ Kind = Check; Resource = resource }]
            match mode with
            | CheckOnly -> checkState resource checkOutcome
            | CheckAndApply -> applyState resource checkOutcome results[{ Kind = Apply; Resource = resource }]
        {
            Items = [
                for resource in orderResources roots ->
                    { ResourceName = resource.PresentableName; State = state resource }
            ]
        }

    let private isFailure = function
        | ReportItemState.Skipped | ReportItemState.CheckFailed | ReportItemState.ApplyFailed -> true
        | ReportItemState.AlreadyApplied | ReportItemState.NotApplied | ReportItemState.Applied -> false

    /// Whether all the resources have been processed without failures.
    let isSuccessful(report: Report): bool =
        not(report.Items |> List.exists (fun item -> isFailure item.State))

    /// The overall result of a check execution.
    let checkStatus(report: Report): CheckStatus =
        if not(isSuccessful report) then CheckError
        elif report.Items |> List.exists (fun item -> item.State = ReportItemState.NotApplied) then NotAllApplied
        else AllApplied

    /// <summary>The marker shown before the resource name.</summary>
    /// <param name="useEmoji">Whether to use emoji, or plain ASCII if the output doesn't support them.</param>
    /// <param name="state">The resource state.</param>
    let marker (useEmoji: bool) (state: ReportItemState): string =
        match state with
        | ReportItemState.AlreadyApplied -> if useEmoji then "➖" else "[=]"
        | ReportItemState.NotApplied -> if useEmoji then "🟡" else "[x]"
        | ReportItemState.Applied -> if useEmoji then "✅" else "[x]"
        | ReportItemState.Skipped -> if useEmoji then "⏩" else "[=]"
        | ReportItemState.CheckFailed | ReportItemState.ApplyFailed -> if useEmoji then "❌" else "[x]"

    /// The color of the marker, for the consoles supporting colors.
    let color(state: ReportItemState): Color =
        match state with
        | ReportItemState.AlreadyApplied | ReportItemState.Skipped -> Color.Grey
        | ReportItemState.NotApplied -> Color.Yellow
        | ReportItemState.Applied -> Color.Green
        | ReportItemState.CheckFailed | ReportItemState.ApplyFailed -> Color.Red

    /// The description of the state shown after the resource name.
    let annotation(state: ReportItemState): string =
        match state with
        | ReportItemState.AlreadyApplied -> "already applied"
        | ReportItemState.NotApplied -> "not applied"
        | ReportItemState.Applied -> "applied"
        | ReportItemState.Skipped -> "skipped: a dependency has failed"
        | ReportItemState.CheckFailed -> "failed to check"
        | ReportItemState.ApplyFailed -> "failed to apply"

    /// The text of the report line after the marker.
    let description(item: ReportItem): string =
        $"{item.ResourceName} ({annotation item.State})"

    /// The full text of the report line.
    let formatLine (useEmoji: bool) (item: ReportItem): string =
        $"{marker useEmoji item.State} {description item}"
