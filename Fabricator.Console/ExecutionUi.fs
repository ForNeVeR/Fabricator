// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

/// Presentation of the execution: the ordered log of the tasks and, for interactive consoles, the live progress
/// display.
module internal Fabricator.Console.ExecutionUi

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
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
    /// Notifies that one more task of the execution has finished, whether it has run or not.
    /// This signal is used by the UI used to increment the finished task counter.
    abstract TaskFinished: unit -> unit

/// The console UI.
type IExecutionUi =
    /// <summary>Shows an execution while the <paramref name="action"/> is running.</summary>
    /// <param name="header">The line written before the execution starts.</param>
    /// <param name="title">The short title of the execution, shown together with its overall progress.</param>
    /// <param name="totalTasks">The number of tasks in the execution.</param>
    /// <param name="action">The execution itself.</param>
    abstract Run: header: string * title: string * totalTasks: int * action: (IExecutionView -> Async<'a>) -> Async<'a>
    /// Writes a line out of any task's log order, e.g. a message about the execution as a whole.
    abstract WriteLine: line: string -> unit

/// The visual representation of a running task's status and progress.
type private ITaskRow =
    abstract SetStatus: status: string -> unit
    /// Starts showing progress; returns the generation number of the progress to pass to the other methods.
    abstract StartProgress: total: int64 option * progressUnit: ProgressUnit -> int
    abstract ReportProgress: generation: int * current: int64 -> unit
    abstract StopProgress: generation: int -> unit

module private TaskRow =
    let Null = {
        new ITaskRow with
            member _.SetStatus _ = ()
            member _.StartProgress(_, _) = 0
            member _.ReportProgress(_, _) = ()
            member _.StopProgress _ = ()
    }

let private prefixed (name: string) (message: string) = $"{name}: {message}"

type private TaskReporter(name: string, log: ChannelWriter<string>, row: ITaskRow) =
    interface IReporter with
        member _.Status newStatus = row.SetStatus newStatus
        member _.Log message = log.TryWrite(prefixed name message) |> ignore
        member _.WithProgress(header, total, progressUnit, action) = async {
            row.SetStatus header
            let generation = row.StartProgress(total, progressUnit)
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
/// <remarks>
/// This is a task and not an async computation, because the latter would skip waiting for the log after cancellation.
/// </remarks>
let private runAndFlush (log: OrderedLog) (ct: CancellationToken) (action: Async<'a>): Task<'a> = task {
    let work = Async.StartAsTask(action, cancellationToken = ct)
    let! _ = Task.WhenAny work // Never fails.
    do! log.CompleteAsync()
    return! work
}

/// The UI writing only the ordered log, as plain text.
type PlainUi(writer: TextWriter) =
    let writer = TextWriter.Synchronized writer

    interface IExecutionUi with
        member _.WriteLine line = writer.WriteLine line
        member _.Run(header, _, _, action) = async {
            let! ct = Async.CancellationToken
            let log = OrderedLog writer.WriteLine
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
                    member _.TaskFinished() = ()
            }
            return! Async.AwaitTask(runAndFlush log ct (action view))
        }

/// What the custom columns show for a row of the progress display.
type private RowInfo() =
    member val ShowBar = false with get, set
    member val ValueText = "" with get, set

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

/// The progress bar of a task is scaled to this value, without ever reaching it, so the task never gets finished from
/// Spectre's point of view (which would stop its spinner and timer).
let private BarScale = 100.0

type private SpectreRow(name: string) =
    let mutable status = ""
    let mutable progress: (int64 * int64 option * ProgressUnit) option = None
    let mutable generation = 0

    member val Info = RowInfo()
    member val Task: ProgressTask option = None with get, set

    member _.Description = if String.IsNullOrEmpty status then name else prefixed name status

    member _.Status with set value = status <- value
    member _.Generation = generation
    member _.StartProgress(total, progressUnit) =
        generation <- generation + 1
        progress <- Some(0L, total, progressUnit)
        generation
    member _.Report(current: int64) =
        progress <- progress |> Option.map (fun (_, total, progressUnit) -> current, total, progressUnit)
    member _.StopProgress() =
        progress <- None

    /// Copies the state to the display, if the row is visible.
    member this.Refresh() =
        this.Info.ShowBar <- progress.IsSome
        this.Info.ValueText <-
            match progress with
            | None -> ""
            | Some(current, Some total, Items) -> $"{if total > 0L then current * 100L / total else 100L}%%"
            | Some(current, Some total, Bytes) -> $"{formatBytes current} / {formatBytes total}"
            | Some(current, None, Items) -> string current
            | Some(current, None, Bytes) -> formatBytes current
        match this.Task with
        | None -> ()
        | Some task ->
            task.Description <- this.Description
            match progress with
            | Some(current, Some total, _) ->
                task.IsIndeterminate <- false
                let fraction = if total > 0L then float current / float total else 1.0
                task.Value <- min (BarScale * 0.9999) (BarScale * fraction)
            | Some(_, None, _) ->
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
    totalTasks: int,
    maxRows: unit -> int
) =
    let lockObj = obj()

    let overall = ctx.AddTask(title, ProgressTaskSettings(MaxValue = float(max 1 totalTasks)))
    let overallInfo = RowInfo(ShowBar = true, ValueText = $"0/{totalTasks}")
    do rows[overall] <- overallInfo
    let mutable finishedTasks = 0

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

    let hide(row: SpectreRow) =
        match row.Task with
        | Some task ->
            ctx.RemoveTask task |> ignore
            rows.TryRemove task |> ignore
            row.Task <- None
            visibleRows <- visibleRows - 1
            // After the console has shrunk, more rows than allowed might still be visible.
            if pending.Count > 0 && visibleRows < maxRows() then
                let next = (nonNull pending.First).Value
                pending.RemoveFirst()
                show next
        | None -> pending.Remove row |> ignore
        updateMoreRow()

    let update (row: SpectreRow) (action: unit -> 'a) =
        lock lockObj (fun () ->
            let result = action()
            row.Refresh()
            result
        )

    let createRow(row: SpectreRow) = {
        new ITaskRow with
            member _.SetStatus status = update row (fun () -> row.Status <- status)
            member _.StartProgress(total, progressUnit) = update row (fun () -> row.StartProgress(total, progressUnit))
            member _.ReportProgress(generation, current) =
                update row (fun () -> if row.Generation = generation then row.Report current)
            member _.StopProgress generation =
                update row (fun () -> if row.Generation = generation then row.StopProgress())
    }

    interface IExecutionView with
        member _.StartTask name =
            let row = SpectreRow name
            lock lockObj (fun () ->
                if visibleRows < maxRows() then show row
                else
                    pending.AddLast row |> ignore
                    updateMoreRow()
            )
            let taskLog = log.Open()
            let reporter = TaskReporter(name, taskLog, createRow row)
            {
                new ITaskView with
                    member _.Reporter = reporter
                    member _.Complete finalMessage =
                        lock lockObj (fun () -> hide row)
                        completeTask name taskLog finalMessage
            }

        member _.LogInstant(name, message) = logInstant log name message

        member _.TaskFinished() =
            lock lockObj (fun () ->
                finishedTasks <- finishedTasks + 1
                overallInfo.ValueText <- $"{finishedTasks}/{totalTasks}"
                overall.Value <- float finishedTasks
            )

/// <summary>A line of text that erases everything below the cursor before being written.</summary>
/// <remarks>
/// To write a line while the live display is shown, Spectre moves the cursor to the top of the display, writes the line
/// over it, and renders the display again below the line, without erasing the old display. So, the parts of the old
/// display not covered by the line (e.g. the right part of the overall progress row under the second row of a wrapped
/// line) would stay on the screen.
/// </remarks>
type private ClearedLine(text: string) =
    let text = Text text :> IRenderable
    interface IRenderable with
        member _.Measure(options, maxWidth) = text.Measure(options, maxWidth)
        member _.Render(options, maxWidth) = seq {
            Segment.Control "\u001b[0J" // Erase from the cursor to the end of the screen.
            yield! text.Render(options, maxWidth)
            Segment.LineBreak
        }

let private writeLine (console: IAnsiConsole) (line: string) =
    console.Write(ClearedLine line)

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
        member _.WriteLine line = writeLine console line
        member _.Run(header, title, totalTasks, action) = async {
            let! ct = Async.CancellationToken
            let log = OrderedLog(writeLine console)
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
                        ElapsedTimeColumn()
                    )
            // The log is waited for inside, so it is written out before the live display is cleared.
            return! Async.AwaitTask(progress.StartAsync(fun ctx ->
                let view = SpectreView(ctx, rows, log, title, totalTasks, maxRows)
                runAndFlush log ct (action view)
            ))
        }

/// Chooses the UI for the current console: the live display for interactive terminals, plain text otherwise.
let forCurrentConsole(): IExecutionUi =
    if AnsiConsole.Profile.Capabilities.Interactive && not Console.IsOutputRedirected
    then SpectreUi AnsiConsole.Console
    else PlainUi Console.Out
