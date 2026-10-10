// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Console

open System
open System.Collections.Generic
open DiffPlex
open DiffPlex.Chunkers
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
        if text.Length = 0 then [||], false
        else
            let lines = text.Split([| "\r\n"; "\r"; "\n" |], StringSplitOptions.None)
            let endsWithLineBreak = text.EndsWith '\n' || text.EndsWith '\r'
            (if endsWithLineBreak then Array.take (lines.Length - 1) lines else lines), endsWithLineBreak

    let private noFinalLineBreakMarker = @"\ No newline at end of file"

    let private line (kind: DetailKind) (text: string) = { Text = text; Kind = kind }

    /// A patch adding (<see cref="F:Fabricator.Console.DetailKind.Added"/>) or removing
    /// (<see cref="F:Fabricator.Console.DetailKind.Removed"/>) all the lines of the text.
    let private wholeTextPatch (oldHeader: string) (newHeader: string) (kind: DetailKind) (text: string) =
        let lines, endsWithLineBreak = splitLines text
        let hunkHeader, prefix =
            if kind = DetailKind.Added
            then $"@@ -0,0 +1,{lines.Length} @@", "+"
            else $"@@ -1,{lines.Length} +0,0 @@", "-"
        seq {
            yield line DetailKind.FileHeader oldHeader
            yield line DetailKind.FileHeader newHeader
            if not <| Array.isEmpty lines then
                yield line DetailKind.HunkHeader hunkHeader
                yield! lines |> Seq.map (fun l -> line kind (prefix + l))
                if not endsWithLineBreak then yield line DetailKind.Plain noFinalLineBreakMarker
        }

    let private newFilePatch (name: string) (text: string): DetailLine seq =
        wholeTextPatch "--- /dev/null" $"+++ {name} (new)" DetailKind.Added text

    /// <summary>
    /// Marks the last line of a text without the final line break, so it differs from the same line with the line
    /// break. Texts decoded from files never contain it, since they are considered binary otherwise.
    /// </summary>
    let private noFinalLineBreakSentinel = '\u0000'

    /// <summary>
    /// Splits the text into lines without their endings, unlike the default DiffPlex chunker keeping the missing final
    /// line break as a <see cref="F:Fabricator.Console.Report.noFinalLineBreakSentinel"/>.
    /// </summary>
    let private lineChunker = {
        new IChunker with
            member _.Chunk text =
                let lines, endsWithLineBreak = splitLines text
                if not endsWithLineBreak && lines.Length > 0 then
                    lines[lines.Length - 1] <- lines[lines.Length - 1] + string noFinalLineBreakSentinel
                lines
    }

    /// The line ending style of the text, or None if it has no line breaks.
    let private lineEnding(text: string): string option =
        let count (s: string) = (text.Length - text.Replace(s, "").Length) / s.Length
        let crlf = count "\r\n"
        let styles =
            [
                "CRLF", crlf
                "LF", count "\n" - crlf
                "CR", count "\r" - crlf
            ]
            |> List.filter (fun (_, n) -> n > 0)
        match styles with
        | [] -> None
        | [ style, _ ] -> Some style
        | _ -> Some "mixed"

    let private fileHeaders(name: string) = $"--- {name}", $"+++ {name}"

    let private diffPatch (name: string) (oldText: string) (newText: string): DetailLine seq =
        let oldHeader, newHeader = fileHeaders name
        // The lines are compared ignoring their endings, so no hunks are produced if only they differ.
        let diff = Differ.Instance.CreateDiffs(oldText, newText, false, false, lineChunker)
        let patchLines = UnidiffRenderer().Generate(diff, name, name) |> splitLines |> fst
        let hunkLines = patchLines |> Array.skip(min 2 patchLines.Length)
        seq {
            yield line DetailKind.FileHeader oldHeader
            yield line DetailKind.FileHeader newHeader
            match lineEnding oldText, lineEnding newText with
            | Some oldStyle, Some newStyle when oldStyle <> newStyle ->
                yield line DetailKind.Plain $"line endings: {oldStyle} → {newStyle}"
            | _ -> ()
            for text in hunkLines do
                let kind =
                    if text.StartsWith "@@" then DetailKind.HunkHeader
                    elif text.StartsWith '+' then DetailKind.Added
                    elif text.StartsWith '-' then DetailKind.Removed
                    else DetailKind.Plain
                if text.EndsWith noFinalLineBreakSentinel then
                    yield line kind (text.TrimEnd noFinalLineBreakSentinel)
                    yield line DetailKind.Plain noFinalLineBreakMarker
                else
                    yield line kind text
        }

    let private changedFilePatch (name: string) (oldText: string) (newText: string): DetailLine seq =
        let oldHeader, newHeader = fileHeaders name
        // DiffPlex numbers the hunks of an empty text from line 1 instead of 0.
        if oldText.Length = 0 then wholeTextPatch oldHeader newHeader DetailKind.Added newText
        elif newText.Length = 0 then wholeTextPatch oldHeader newHeader DetailKind.Removed oldText
        else diffPatch name oldText newText

    /// The maximum number of the detail lines shown under a report line; the rest are summarized by one more line.
    let MaxDetailLines = 200

    /// The lines describing the change, to show under the report line.
    let details(change: ResourceChange): DetailLine seq =
        let lines =
            match change with
            | NoChanges | ChangeWithNoDescription -> Seq.empty
            | NamedChange description -> splitLines description |> fst |> Seq.map (line DetailKind.Plain)
            | TextDiff { Name = name; OldText = None; NewText = newText } -> newFilePatch name newText
            | TextDiff { Name = name; OldText = Some oldText; NewText = newText } -> changedFilePatch name oldText newText
            |> Seq.toArray
        if lines.Length <= MaxDetailLines then lines
        else
            Array.append
                (Array.take MaxDetailLines lines)
                [| line DetailKind.Plain $"… {lines.Length - MaxDetailLines} more lines" |]

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
