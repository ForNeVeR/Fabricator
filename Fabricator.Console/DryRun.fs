// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Console

open Fabricator.Console.Lowering
open Fabricator.Core
open Spectre.Console

/// The actions an execution would perform, listed instead of performing them.
type internal DryRunPlan =
    {
        /// The tasks of the execution, the dependencies going before their dependents.
        Actions: LoweredTask[]
    }

module internal DryRun =

    /// <summary>Lists the actions of an execution, without running anything.</summary>
    /// <param name="mode">The execution mode.</param>
    /// <param name="roots">The root resources of the execution. Their dependencies are listed as well.</param>
    /// <remarks>
    /// The resources go in the order of <see cref="M:Fabricator.Console.Lowering.orderResources"/>. Every resource has
    /// a check action and, in <see cref="F:Fabricator.Console.Lowering.ExecutionMode.CheckAndApply"/> mode, an apply
    /// action right after it.
    /// </remarks>
    let create (mode: ExecutionMode) (roots: Resource seq): DryRunPlan =
        {
            Actions = [|
                for resource in orderResources roots do
                    yield { Kind = Check; Resource = resource }
                    if mode = CheckAndApply then yield { Kind = Apply; Resource = resource }
            |]
        }

    /// <summary>The marker shown before every action.</summary>
    /// <param name="useEmoji">Whether to use emoji, or plain ASCII if the output doesn't support them.</param>
    let marker(useEmoji: bool): string =
        // The variation selector requests the emoji presentation, since the text one is the default for this character.
        if useEmoji then "⏸️" else "[ ]"

    /// The color of the marker, for the consoles supporting colors.
    let color: Color = Color.Grey

    /// The text of the action line after the marker.
    let description(action: LoweredTask): string =
        match action.Kind with
        | Check -> $"{action.Resource.PresentableName}: check"
        | Apply -> $"{action.Resource.PresentableName}: apply if not applied"

    /// The full text of the action line.
    let formatLine (useEmoji: bool) (action: LoweredTask): string =
        $"{marker useEmoji} {description action}"

    /// The text of the summary line.
    let formatSummary(plan: DryRunPlan): string =
        match plan.Actions.Length with
        | 1 -> "1 action"
        | n -> $"{n} actions"
