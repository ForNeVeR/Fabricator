// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Console

open System
open System.Collections.Generic
open DiffPlex.Renderer
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
    /// The resource check has thrown an error.
    | CheckErrored
    /// The resource application has thrown an error.
    | ApplyErrored

/// A line of the report: a resource, its final state, and the change related to the state.
type internal ReportItem =
    {
        ResourceName: string
        State: ReportItemState
        /// <summary>
        /// The change required to apply the resource for <see cref="F:Fabricator.Console.ReportItemState.NotApplied"/>,
        /// the change made for <see cref="F:Fabricator.Console.ReportItemState.Applied"/>, or
        /// <see cref="F:Fabricator.Core.ResourceChange.NoChanges"/> for the other states.
        /// </summary>
        Change: ResourceChange
    }

/// The kind of a change detail line, defining its presentation.
[<RequireQualifiedAccess>]
type internal DetailKind =
    /// A line of a change description, or a context line of a patch.
    | Plain
    /// A patch file header: the line starting with <c>---</c> or <c>+++</c>.
    | FileHeader
    /// A patch hunk header: the line starting with <c>@@</c>.
    | HunkHeader
    /// A line added by a patch.
    | Added
    /// A line removed by a patch.
    | Removed

/// A line of the change details shown under a report line.
type internal DetailLine =
    {
        Text: string
        Kind: DetailKind
    }

/// A part of the report summary: the number of the resources in some state.
type internal SummaryPart =
    {
        Text: string
        /// The state the part is presented as.
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
        | CheckPassed -> ReportItemState.AlreadyApplied, NoChanges
        | ChangeNeeded change -> ReportItemState.NotApplied, change
        | Errored _ -> ReportItemState.CheckErrored, NoChanges
        | Blocked -> ReportItemState.Skipped, NoChanges
        | Applied _ | NotRequired -> unexpected resource outcome

    let private applyState (resource: Resource) (checkOutcome: TaskOutcome) (applyOutcome: TaskOutcome) =
        match checkOutcome, applyOutcome with
        | CheckPassed, NotRequired -> ReportItemState.AlreadyApplied, NoChanges
        | ChangeNeeded _, Applied change -> ReportItemState.Applied, change
        | ChangeNeeded _, Errored _ -> ReportItemState.ApplyErrored, NoChanges
        | Errored _, Blocked -> ReportItemState.CheckErrored, NoChanges
        | Blocked, Blocked -> ReportItemState.Skipped, NoChanges
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
        let stateAndChange(resource: Resource) =
            let checkOutcome = results[{ Kind = Check; Resource = resource }]
            match mode with
            | CheckOnly -> checkState resource checkOutcome
            | CheckAndApply -> applyState resource checkOutcome results[{ Kind = Apply; Resource = resource }]
        {
            Items = [
                for resource in orderResources roots ->
                    let state, change = stateAndChange resource
                    { ResourceName = resource.PresentableName; State = state; Change = change }
            ]
        }

    let private isFailure = function
        | ReportItemState.Skipped | ReportItemState.CheckErrored | ReportItemState.ApplyErrored -> true
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
        | ReportItemState.NotApplied -> if useEmoji then "🟡" else "[ ]"
        | ReportItemState.Applied -> if useEmoji then "✅" else "[x]"
        | ReportItemState.Skipped -> if useEmoji then "⏩" else "[-]"
        | ReportItemState.CheckErrored | ReportItemState.ApplyErrored -> if useEmoji then "❌" else "[!]"

    /// The color of the marker, for the consoles supporting colors.
    let color(state: ReportItemState): Color =
        match state with
        | ReportItemState.AlreadyApplied | ReportItemState.Skipped -> Color.Grey
        | ReportItemState.NotApplied -> Color.Yellow
        | ReportItemState.Applied -> Color.Green
        | ReportItemState.CheckErrored | ReportItemState.ApplyErrored -> Color.Red

    /// The description of the state shown after the resource name.
    let annotation(state: ReportItemState): string =
        match state with
        | ReportItemState.AlreadyApplied -> "already applied"
        | ReportItemState.NotApplied -> "not applied"
        | ReportItemState.Applied -> "applied"
        | ReportItemState.Skipped -> "skipped: a dependency has failed"
        | ReportItemState.CheckErrored -> "failed to check"
        | ReportItemState.ApplyErrored -> "failed to apply"

    /// The text of the report line after the marker.
    let description(item: ReportItem): string =
        $"{item.ResourceName} ({annotation item.State})"

    /// The full text of the report line.
    let formatLine (useEmoji: bool) (item: ReportItem): string =
        $"{marker useEmoji item.State} {description item}"

    /// The numbers of the resources in every state, except the states no resources are in.
    let summary(report: Report): SummaryPart list =
        let count (states: ReportItemState list) =
            report.Items |> List.filter (fun item -> List.contains item.State states) |> List.length
        [
            [ ReportItemState.AlreadyApplied ], "already applied"
            [ ReportItemState.NotApplied ], "to apply"
            [ ReportItemState.Applied ], "applied"
            [ ReportItemState.CheckErrored; ReportItemState.ApplyErrored ], "errored"
            [ ReportItemState.Skipped ], "blocked"
        ]
        |> List.choose (fun (states, label) ->
            match count states with
            | 0 -> None
            | n -> Some { Text = $"{n} {label}"; State = List.head states }
        )

    /// The text of the report summary line.
    let formatSummary(report: Report): string =
        summary report |> Seq.map _.Text |> String.concat ", "

    /// Splits the text into lines. Returns the lines and whether the text ends with a line break, which doesn't start
    /// a new line.
    let private splitLines(text: string): string[] * bool =
        let endsWithLineBreak = text.EndsWith '\n'
        let text = if endsWithLineBreak then text.Substring(0, text.Length - 1) else text
        let lines =
            if text.Length = 0 && endsWithLineBreak then Seq.singleton ""
            elif text.Length = 0 then []
            else text.Split '\n' |> Seq.map _.TrimEnd('\r')
        lines |> Seq.toArray, endsWithLineBreak

    let private noFinalLineBreakMarker = @"\ No newline at end of file"

    let private line (kind: DetailKind) (text: string) = { Text = text; Kind = kind }

    let private newFilePatch (name: string) (text: string): DetailLine seq =
        let lines, endsWithLineBreak = splitLines text
        seq {
            yield line DetailKind.FileHeader "--- /dev/null"
            yield line DetailKind.FileHeader $"+++ {name} (new)"
            if not <| Array.isEmpty lines then
                yield line DetailKind.HunkHeader $"@@ -0,0 +1,{lines.Length} @@"
                yield! lines |> Seq.map (fun l -> line DetailKind.Added ("+" + l))
                if not endsWithLineBreak then yield line DetailKind.Plain noFinalLineBreakMarker
        }

    let private trimFinalLineBreak(text: string) =
        if text.EndsWith "\r\n" then text.Substring(0, text.Length - 2)
        elif text.EndsWith '\n' then text.Substring(0, text.Length - 1)
        else text

    let private changedFilePatch (name: string) (oldText: string) (newText: string): DetailLine seq =
        // DiffPlex presents the final line break as an additional empty line, so it is removed when both texts have it.
        let oldText, newText =
            if oldText.EndsWith '\n' && newText.EndsWith '\n'
            then trimFinalLineBreak oldText, trimFinalLineBreak newText
            else oldText, newText
        let patch = UnidiffRenderer.GenerateUnidiff(oldText, newText, name, name, ignoreWhitespace = false)
        let lines, _ = splitLines patch
        lines |> Seq.mapi (fun i text ->
            let kind =
                if i < 2 then DetailKind.FileHeader
                elif text.StartsWith "@@" then DetailKind.HunkHeader
                elif text.StartsWith '+' then DetailKind.Added
                elif text.StartsWith '-' then DetailKind.Removed
                else DetailKind.Plain
            line kind text
        )

    /// The lines describing the change, to show under the report line.
    let details(change: ResourceChange): DetailLine seq =
        match change with
        | NoChanges | ChangeWithNoDescription -> []
        | NamedChange description -> splitLines description |> fst |> Seq.map (line DetailKind.Plain)
        | TextDiff { Name = name; OldText = None; NewText = newText } -> newFilePatch name newText
        | TextDiff { Name = name; OldText = Some oldText; NewText = newText } -> changedFilePatch name oldText newText

    /// The indentation of the detail lines relative to the report line.
    let detailIndent = "    "

    /// The style of a detail line, for the consoles supporting colors.
    let detailStyle(kind: DetailKind): Style =
        match kind with
        | DetailKind.Plain -> Style.Plain
        | DetailKind.FileHeader -> Style(decoration = Decoration.Bold)
        | DetailKind.HunkHeader -> Style(foreground = Color.Aqua)
        | DetailKind.Added -> Style(foreground = Color.Green)
        | DetailKind.Removed -> Style(foreground = Color.Red)
