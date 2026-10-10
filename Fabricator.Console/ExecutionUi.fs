// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

/// Presentation of the execution: the ordered log of the tasks and, for interactive consoles, the live progress
/// display.
module internal Fabricator.Console.ExecutionUi

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Fabricator.Core
open Spectre.Console
open Spectre.Console.Rendering

let formatBytes(bytes: int64): string =
    if bytes >= 1024L * 1024L * 1024L then
        $"%.2f{float bytes / float (1024L * 1024L * 1024L)} GiB"
    elif bytes >= 1024L * 1024L then
        $"%.2f{float bytes / float (1024L * 1024L)} MiB"
    elif bytes >= 1024L then
        $"%.2f{float bytes / float 1024L} KiB"
    else
        $"%d{bytes} bytes"

/// A running task.
type ITaskView =
    /// The reporter to pass to the resource function. Every logged line is prefixed with the task name.
    abstract Reporter: IReporter
    /// <summary>
    /// Finishes the task: writes the final message (if any) to its log, closes the log and removes the task from the
    /// display.
    /// </summary>
    abstract Complete: finalMessage: string option -> unit

/// The view of a single execution (a group of tasks — i.e. the whole "check" or "apply" run).
type IExecutionView =
    /// Shows a task that has started running. The log of the task goes after the logs of the tasks started earlier.
    abstract StartTask: name: string -> ITaskView
    /// Logs a message of a task that did not run, as if it was started and finished immediately.
    abstract LogInstant: name: string * message: string -> unit
    /// Notifies that the processing of one more resource of the execution has finished, whether its tasks have run or
    /// not. This signal is used by the UI to increment the finished resource counter.
    abstract ResourceFinished: unit -> unit

/// The console UI.
type IExecutionUi =
    /// <summary>Shows an execution while the <paramref name="action"/> is running.</summary>
    /// <param name="header">The line written before the execution starts.</param>
    /// <param name="title">The short title of the execution, shown together with its overall progress.</param>
    /// <param name="totalResources">The number of resources processed by the execution.</param>
    /// <param name="action">The execution itself.</param>
    /// <remarks>
    /// If writing the output fails, the execution is cancelled, and the returned computation fails with the output
    /// error after the execution has finished.
    /// </remarks>
    abstract Run:
        header: string * title: string * totalResources: int * action: (IExecutionView -> Async<'a>) -> Async<'a>
    /// Writes a line out of any task's log order, e.g. a message about the execution as a whole.
    abstract WriteLine: line: string -> unit

/// The visual representation of a running task's status and progress.
type private ITaskRow =
    abstract SetStatus: status: string -> unit
    /// Sets the status to the header and starts showing progress; returns the generation number of the progress to pass
    /// to the other methods.
    abstract StartProgress: header: string * total: int64 option * progressUnit: ProgressUnit -> int
    abstract ReportProgress: generation: int * current: int64 -> unit
    abstract StopProgress: generation: int -> unit

module private TaskRow =
    let Null = {
        new ITaskRow with
            member _.SetStatus _ = ()
            member _.StartProgress(_, _, _) = 0
            member _.ReportProgress(_, _) = ()
            member _.StopProgress _ = ()
    }

let private prefixed (name: string) (message: string) = $"{name}: {message}"

type private TaskReporter(name: string, log: ChannelWriter<string>, row: ITaskRow) =
    interface IReporter with
        member _.Status newStatus = row.SetStatus newStatus
        member _.Log message = log.TryWrite(prefixed name message) |> ignore
        member _.WithProgress(header, total, progressUnit, action) = async {
            let generation = row.StartProgress(header, total, progressUnit)
            let progress = { new IProgressReporter with member _.Report current = row.ReportProgress(generation, current) }
            try
                return! action progress
            finally
                row.StopProgress generation
        }

/// Writes a line to the ordered log, as a separate log.
let private writeInstant (log: OrderedLog) (line: string) =
    let writer = log.Open()
    writer.TryWrite line |> ignore
    writer.TryComplete() |> ignore

let private logInstant (log: OrderedLog) (name: string) (message: string) =
    writeInstant log (prefixed name message)

let private completeTask (name: string) (log: ChannelWriter<string>) (finalMessage: string option) =
    finalMessage |> Option.iter (fun message -> log.TryWrite(prefixed name message) |> ignore)
    log.TryComplete() |> ignore

/// <summary>Runs the action, then waits for the log to be written out, however the action has ended.</summary>
/// <param name="log">
/// The log the action writes to. It is completed after the action has finished, so no new logs can be opened after
/// that.
/// </param>
/// <param name="ct">
/// The cancellation token of the execution. The action runs with a token linked to it, so it is also cancelled when
/// the log fails.
/// </param>
/// <param name="action">
/// The action to run; it should open all its logs in <paramref name="log"/> before finishing.
/// </param>
/// <remarks>
/// <para>
/// If the log fails to be written, the action is cancelled, and, after it has finished, the log error is thrown
/// (unless the execution has been cancelled via <paramref name="ct"/>).
/// </para>
/// <para>
/// This is a task and not an async computation, because the latter would skip waiting for the log after cancellation.
/// </para>
/// </remarks>
let private runAndFlush (log: OrderedLog) (ct: CancellationToken) (action: Async<'a>): Task<'a> = task {
    use cts = CancellationTokenSource.CreateLinkedTokenSource ct
    let work = Async.StartAsTask(action, cancellationToken = cts.Token)
    let! first = Task.WhenAny(work :> Task, log.Completion) // Never fails.
    // The log can only complete before the action by failing.
    if obj.ReferenceEquals(first, log.Completion) then cts.Cancel()

    let! _ = Task.WhenAny work // Never fails.
    let flush = log.CompleteAsync()
    let! _ = Task.WhenAny flush // Never fails.
    if flush.IsFaulted && not ct.IsCancellationRequested then
        do! flush // Throws the log error.
    return! work
}

/// Like <see cref="M:Microsoft.FSharp.Control.FSharpAsync.AwaitTask"/>, but fails with the task's own exception instead
/// of the wrapping <see cref="T:System.AggregateException"/>.
let private awaitTask(t: Task<'a>): Async<'a> = async {
    try
        return! Async.AwaitTask t
    with
    | :? AggregateException as e when e.InnerExceptions.Count = 1 ->
        ExceptionDispatchInfo.Capture(nonNull e.InnerException).Throw()
        return Unchecked.defaultof<'a> // Unreachable.
}

/// The UI writing only the ordered log, as plain text.
type PlainUi(writer: TextWriter) =
    let writer = TextWriter.Synchronized writer

    interface IExecutionUi with
        member _.WriteLine line = writer.WriteLine line
        member _.Run(header, _, _, action) = async {
            let! ct = Async.CancellationToken
            let log = OrderedLog(fun lines -> for line in lines do writer.WriteLine line)
            writeInstant log header
            let view = {
                new IExecutionView with
                    member _.StartTask name =
                        let taskLog = log.Open()
                        let reporter = TaskReporter(name, taskLog, TaskRow.Null)
                        {
                            new ITaskView with
                                member _.Reporter = reporter
                                member _.Complete finalMessage = completeTask name taskLog finalMessage
                        }
                    member _.LogInstant(name, message) = logInstant log name message
                    member _.ResourceFinished() = ()
            }
            return! awaitTask(runAndFlush log ct (action view))
        }

/// A progress shown by a task row.
type ProgressState =
    {
        Header: string
        Total: int64 option
        Unit: ProgressUnit
        mutable Current: int64
    }

/// <summary>
/// The progress operations of a task that are currently running (see
/// <see cref="M:Fabricator.Core.IReporter.WithProgress"/>). Of them, the latest started one is shown.
/// </summary>
/// <remarks>Not thread-safe.</remarks>
type ActiveProgress() =
    let running = ResizeArray<int * ProgressState>()
    let mutable lastGeneration = 0

    /// Starts a new progress, which becomes the shown one. Returns its generation number.
    member _.Start(header: string, total: int64 option, progressUnit: ProgressUnit): int =
        lastGeneration <- lastGeneration + 1
        running.Add(lastGeneration, { Header = header; Total = total; Unit = progressUnit; Current = 0L })
        lastGeneration

    /// Updates the value of the progress, if it is still running.
    member _.Report(generation: int, current: int64): unit =
        match running |> Seq.tryFind (fun (g, _) -> g = generation) with
        | Some(_, state) -> state.Current <- current
        | None -> ()

    /// Stops the progress. If it was the shown one, the latest started progress of the remaining ones is shown.
    member _.Stop(generation: int): unit =
        running.RemoveAll(fun (g, _) -> g = generation) |> ignore

    /// The shown progress: the latest started one still running.
    member _.Shown: ProgressState option =
        if running.Count = 0 then None else Some(snd running[running.Count - 1])

/// What the custom columns show for a row of the progress display.
type private RowInfo() =
    member val ShowBar = false with get, set
    member val ValueText = "" with get, set
    /// Measures the time since the row's task has started, whether it was visible or not.
    member val Elapsed = Stopwatch.StartNew()

type private RowInfos = ConcurrentDictionary<ProgressTask, RowInfo>

/// The task description, never wrapped.
type private DescriptionColumn() =
    inherit ProgressColumn()
    override _.NoWrap = true
    override _.GetColumnWidth options = Nullable(max 20 (options.ConsoleSize.Width / 2))
    override _.Render(_, task, _) = Text(task.Description, Overflow = Overflow.Ellipsis)

/// The progress bar, only shown for the rows with progress.
type private BarColumn(rows: RowInfos) =
    inherit ProgressColumn()
    let inner = ProgressBarColumn(Width = Nullable 30)
    override _.Render(options, task, delta) =
        match rows.TryGetValue task with
        | true, row when row.ShowBar -> inner.Render(options, task, delta)
        | _ -> Text.Empty

/// The textual representation of the progress, e.g. a percentage or a data size.
type private ValueColumn(rows: RowInfos) =
    inherit ProgressColumn()
    override _.NoWrap = true
    override _.Render(_, task, _) =
        match rows.TryGetValue task with
        | true, row -> Text row.ValueText
        | false, _ -> Text.Empty

/// The time since the task has started, which may be earlier than when its row was shown.
type private ElapsedColumn(rows: RowInfos) =
    inherit ProgressColumn()
    let style = ElapsedTimeColumn().Style
    override _.NoWrap = true
    override _.GetColumnWidth _ = Nullable 8
    override _.Render(_, task, _) =
        match rows.TryGetValue task with
        | true, row ->
            let elapsed = row.Elapsed.Elapsed
            if elapsed.TotalHours >= 100.0 then Text "**:**:**"
            else Text(elapsed.ToString @"hh\:mm\:ss", style)
        | false, _ -> Text.Empty

/// The progress bar of a task is scaled to this value, without ever reaching it, so the task never gets finished from
/// Spectre's point of view (which would stop its spinner and timer).
let private BarScale = 100.0

type private SpectreRow(name: string) =
    let mutable status = ""
    let progress = ActiveProgress()

    member val Info = RowInfo()
    member val Task: ProgressTask option = None with get, set

    member _.Description = if String.IsNullOrEmpty status then name else prefixed name status

    member _.Status with set value = status <- value
    member _.StartProgress(header, total, progressUnit) =
        status <- header
        progress.Start(header, total, progressUnit)
    member _.Report(generation, current) = progress.Report(generation, current)
    member _.StopProgress generation =
        let shownBefore = progress.Shown
        progress.Stop generation
        match shownBefore, progress.Shown with
        | Some before, Some after when not(obj.ReferenceEquals(before, after)) ->
            // An earlier started progress is shown again, so is its header.
            status <- after.Header
        | _ -> ()

    /// Copies the state to the display, if the row is visible.
    member this.Refresh() =
        let shown = progress.Shown
        this.Info.ShowBar <- shown.IsSome
        this.Info.ValueText <-
            match shown with
            | None -> ""
            | Some { Current = current; Total = Some total; Unit = Items } ->
                $"{if total > 0L then current * 100L / total else 100L}%%"
            | Some { Current = current; Total = Some total; Unit = Bytes } ->
                $"{formatBytes current} / {formatBytes total}"
            | Some { Current = current; Total = None; Unit = Items } -> string current
            | Some { Current = current; Total = None; Unit = Bytes } -> formatBytes current
        match this.Task with
        | None -> ()
        | Some task ->
            task.Description <- this.Description
            match shown with
            | Some { Current = current; Total = Some total } ->
                task.IsIndeterminate <- false
                let fraction = if total > 0L then float current / float total else 1.0
                task.Value <- min (BarScale * 0.9999) (BarScale * fraction)
            | Some { Total = None } ->
                task.IsIndeterminate <- true
                task.Value <- 0.0
            | None ->
                task.IsIndeterminate <- false
                task.Value <- 0.0

type private SpectreView(
    ctx: ProgressContext,
    rows: RowInfos,
    log: OrderedLog,
    title: string,
    totalResources: int,
    maxRows: unit -> int
) =
    let lockObj = obj()

    let overall = ctx.AddTask(title, ProgressTaskSettings(MaxValue = float(max 1 totalResources)))
    let overallInfo = RowInfo(ShowBar = true, ValueText = $"0/{totalResources}")
    do rows[overall] <- overallInfo
    let mutable finishedResources = 0

    let mutable visibleRows = 0
    let pending = LinkedList<SpectreRow>()
    let mutable moreRow: ProgressTask option = None

    let taskSettings() = ProgressTaskSettings(MaxValue = BarScale)

    let updateMoreRow() =
        match pending.Count, moreRow with
        | 0, None -> ()
        | 0, Some task ->
            ctx.RemoveTask task |> ignore
            moreRow <- None
        | count, Some task -> task.Description <- $"… and {count} more"
        | count, None -> moreRow <- Some(ctx.AddTask($"… and {count} more", taskSettings()))

    let show(row: SpectreRow) =
        let task =
            match moreRow with
            | Some more -> ctx.AddTaskBefore(row.Description, taskSettings(), more)
            | None -> ctx.AddTask(row.Description, taskSettings())
        rows[task] <- row.Info
        row.Task <- Some task
        visibleRows <- visibleRows + 1
        row.Refresh()

    /// Shows the pending rows, in their starting order, while there is room for them.
    let promote() =
        // After the console has shrunk, more rows than allowed might still be visible; they are not hidden, but no
        // more rows are shown until they finish.
        while pending.Count > 0 && visibleRows < maxRows() do
            let next = (nonNull pending.First).Value
            pending.RemoveFirst()
            show next
        updateMoreRow()

    let hide(row: SpectreRow) =
        match row.Task with
        | Some task ->
            ctx.RemoveTask task |> ignore
            rows.TryRemove task |> ignore
            row.Task <- None
            visibleRows <- visibleRows - 1
        | None -> pending.Remove row |> ignore
        promote()

    let update (row: SpectreRow) (action: unit -> 'a) =
        lock lockObj (fun () ->
            let result = action()
            row.Refresh()
            result
        )

    let createRow(row: SpectreRow) = {
        new ITaskRow with
            member _.SetStatus status = update row (fun () -> row.Status <- status)
            member _.StartProgress(header, total, progressUnit) =
                update row (fun () -> row.StartProgress(header, total, progressUnit))
            member _.ReportProgress(generation, current) = update row (fun () -> row.Report(generation, current))
            member _.StopProgress generation = update row (fun () -> row.StopProgress generation)
    }

    interface IExecutionView with
        member _.StartTask name =
            let row = SpectreRow name
            lock lockObj (fun () ->
                // Even if there is room for the row, the rows started earlier go first.
                pending.AddLast row |> ignore
                promote()
            )
            let taskLog = log.Open()
            let reporter = TaskReporter(name, taskLog, createRow row)
            {
                new ITaskView with
                    member _.Reporter = reporter
                    member _.Complete finalMessage =
                        try
                            lock lockObj (fun () -> hide row)
                        finally
                            // Even if the display has failed, the log should not hold the next logs back.
                            completeTask name taskLog finalMessage
            }

        member _.LogInstant(name, message) = logInstant log name message

        member _.ResourceFinished() =
            lock lockObj (fun () ->
                finishedResources <- finishedResources + 1
                overallInfo.ValueText <- $"{finishedResources}/{totalResources}"
                overall.Value <- float finishedResources
            )

/// <summary>Lines of text that erase everything below the cursor before being written.</summary>
/// <remarks>
/// To write lines while the live display is shown, Spectre moves the cursor to the top of the display, writes the lines
/// over it, and renders the display again below them, without erasing the old display. So, the parts of the old
/// display not covered by the lines (e.g. the right part of the overall progress row under the second row of a wrapped
/// line) would stay on the screen.
/// </remarks>
type private ClearedLines(lines: IReadOnlyList<string>) =
    let text = Text(String.Join("\n", lines)) :> IRenderable
    interface IRenderable with
        member _.Measure(options, maxWidth) = text.Measure(options, maxWidth)
        member _.Render(options, maxWidth) = seq {
            Segment.Control "\u001b[0J" // Erase from the cursor to the end of the screen.
            yield! text.Render(options, maxWidth)
            Segment.LineBreak
        }

/// Writes the lines at once, so the live display is only redrawn once.
let private writeLines (console: IAnsiConsole) (lines: IReadOnlyList<string>) =
    console.Write(ClearedLines lines)

/// The number of the live display rows besides the task rows: the top and bottom padding, the overall progress row,
/// and the "… and N more" row.
let private liveDisplayExtraRows = 4

/// The number of the console lines left free above the live display.
let private freeLines = 10

/// The maximum number of task rows in the live display shown on a console with the passed height.
let maxTaskRows(consoleHeight: int): int =
    max 1 (consoleHeight - freeLines - liveDisplayExtraRows)

/// The UI showing the live progress display on an interactive console, with the ordered log above it.
type SpectreUi(console: IAnsiConsole) =
    interface IExecutionUi with
        member _.WriteLine line = writeLines console [| line |]
        member _.Run(header, title, totalResources, action) = async {
            let! ct = Async.CancellationToken
            let log = OrderedLog(writeLines console)
            writeInstant log header

            // Read on every use, to follow the console resizing.
            let maxRows() = maxTaskRows console.Profile.Height
            let rows = RowInfos(HashIdentity.Reference)
            let progress =
                console.Progress()
                    .AutoClear(true)
                    .Columns(
                        SpinnerColumn(),
                        DescriptionColumn(),
                        BarColumn rows,
                        ValueColumn rows,
                        ElapsedColumn rows
                    )
            // The log is waited for inside, so it is written out before the live display is cleared.
            return! awaitTask(progress.StartAsync(fun ctx ->
                let view = SpectreView(ctx, rows, log, title, totalResources, maxRows)
                runAndFlush log ct (action view)
            ))
        }

/// Chooses the UI for the current console: the live display for interactive ANSI terminals, plain text otherwise.
let forCurrentConsole(): IExecutionUi =
    let capabilities = AnsiConsole.Profile.Capabilities
    // The live display redraws itself via ANSI cursor movements.
    if capabilities.Interactive && capabilities.Ansi && not Console.IsOutputRedirected
    then SpectreUi AnsiConsole.Console
    else PlainUi Console.Out
