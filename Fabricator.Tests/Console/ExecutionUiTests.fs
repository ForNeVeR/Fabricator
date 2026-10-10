// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Console.ExecutionUiTests

open System.IO
open System.Text.RegularExpressions
open System.Threading.Tasks
open Fabricator.Console
open Fabricator.Core
open Fabricator.Tests.FakeResource
open Spectre.Console
open Xunit

/// Leaves room for 3 task rows.
[<Literal>]
let private ConsoleHeight = 17

let private createInteractiveConsole(writer: TextWriter): IAnsiConsole =
    let console = AnsiConsole.Create(
        AnsiConsoleSettings(
            Out = AnsiConsoleOutput writer,
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes,
            // Otherwise, the CI enrichers (e.g. the one for GitHub Actions) make the console non-interactive.
            Enrichment = ProfileEnrichment(UseDefaultEnrichers = false)
        )
    )
    console.Profile.Width <- 100
    console.Profile.Height <- ConsoleHeight
    console

[<Fact>]
let ``maxTaskRows leaves 10 lines above the live display``(): unit =
    Assert.Equal(16, ExecutionUi.maxTaskRows 30)
    Assert.Equal(3, ExecutionUi.maxTaskRows 17)
    Assert.Equal(1, ExecutionUi.maxTaskRows 10)

[<Fact>]
let ``formatBytes chooses the unit``(): unit =
    Assert.Equal("512 bytes", ExecutionUi.formatBytes 512L)
    Assert.Equal("1.50 KiB", ExecutionUi.formatBytes 1536L)
    Assert.Equal("2.00 MiB", ExecutionUi.formatBytes(2L * 1024L * 1024L))
    Assert.Equal("3.00 GiB", ExecutionUi.formatBytes(3L * 1024L * 1024L * 1024L))

[<Fact>]
let ``Live display handles more running tasks than it has rows for``(): Task = task {
    let log = EventLog()
    let resources = [ for i in 1 .. 10 -> FakeResource($"R{i}", log) ]
    let allStarted = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let started = ref 0
    for resource in resources do
        resource.OnApplyWithContext <- fun ctx -> async {
            ctx.Reporter.Status "Starting"
            if System.Threading.Interlocked.Increment &started.contents = resources.Length then allStarted.SetResult()
            do! Async.AwaitTask(allStarted.Task.WaitAsync(System.TimeSpan.FromSeconds 10.0))
            do! ctx.Reporter.WithProgress("Items", Some 5L, Items, fun progress -> async {
                for i in 1L .. 5L do
                    progress.Report i
                    do! Async.Sleep 5
            })
            do! ctx.Reporter.WithProgress("Bytes", None, Bytes, fun progress -> async {
                progress.Report 4096L
                do! Async.Sleep 5
            })
            do! Async.Sleep 300 // Let the display refresh with all the tasks running.
            ctx.Reporter.Log "done"
        }

    use output = new StringWriter()
    let ui = ExecutionUi.SpectreUi(createInteractiveConsole output)
    let! report = Commands.apply ui (resources |> Seq.map _.Resource) |> Async.StartAsTask

    let text = output.ToString()
    Assert.True(Report.isSuccessful report, text)
    Assert.Contains(" more", text) // The tasks not fitting the screen are summarized.
    // The overall progress counts the resources, not their check and apply tasks.
    Assert.Contains("0/10", text)
    Assert.DoesNotContain("/20", text)

    // To redraw the display, Spectre moves the cursor from its last line up to its first one.
    let displayHeights = [ for m in Regex.Matches(text, "\u001b" + @"\[(\d+)A") -> int m.Groups[1].Value + 1 ]
    Assert.NotEmpty displayHeights
    for height in displayHeights do
        Assert.True(height <= ConsoleHeight - 10, $"The live display is {height} lines high.")

    for i in 1 .. 10 do
        // Every written batch of lines erases the previous state of the display before being written over it. The
        // first line of a task's log always starts a batch.
        Assert.Contains($"\u001b[0JR{i}: applying…", text)
        Assert.Contains($"R{i}: done", text)
        Assert.Contains($"R{i}: applied.", text)
}

[<Fact>]
let ``ActiveProgress shows the latest started progress still running``(): unit =
    let progress = ExecutionUi.ActiveProgress()
    Assert.True(progress.Shown.IsNone)

    let alpha = progress.Start("Alpha", Some 10L, Items)
    let beta = progress.Start("Beta", None, Bytes)
    progress.Report(alpha, 3L)
    progress.Report(beta, 100L)
    let shown = Option.get progress.Shown
    Assert.Equal("Beta", shown.Header)
    Assert.Equal(100L, shown.Current)

    progress.Stop beta
    progress.Report(beta, 200L) // Ignored after the progress has stopped.
    progress.Report(alpha, 7L)
    let shown = Option.get progress.Shown
    Assert.Equal("Alpha", shown.Header)
    Assert.Equal(7L, shown.Current)

    progress.Stop alpha
    Assert.True(progress.Shown.IsNone)

[<Fact>]
let ``ActiveProgress keeps showing the latest progress when an earlier one stops``(): unit =
    let progress = ExecutionUi.ActiveProgress()
    let alpha = progress.Start("Alpha", Some 10L, Items)
    let _beta = progress.Start("Beta", Some 20L, Items)
    progress.Stop alpha
    Assert.Equal("Beta", (Option.get progress.Shown).Header)

let private sampleReport = {
    Items = [
        { ResourceName = "Copy file a.txt to b.txt"; State = ReportItemState.AlreadyApplied }
        { ResourceName = "Deploy [service]"; State = ReportItemState.Applied }
        { ResourceName = "Install tool"; State = ReportItemState.ApplyFailed }
    ]
}

let private sampleReportLines(useEmoji: bool) =
    if useEmoji then [
        "➖ Copy file a.txt to b.txt (already applied)"
        "✅ Deploy [service] (applied)"
        "❌ Install tool (failed to apply)"
    ] else [
        "[=] Copy file a.txt to b.txt (already applied)"
        "[x] Deploy [service] (applied)"
        "[x] Install tool (failed to apply)"
    ]

let private lines(text: string) =
    text.Split('\n') |> Seq.map _.TrimEnd('\r') |> Seq.filter (fun line -> line <> "") |> Seq.toList

/// A writer with an encoding unable to represent emoji.
type private AsciiWriter() =
    inherit StringWriter()
    override _.Encoding = System.Text.Encoding.ASCII

[<Fact>]
let ``Plain UI writes the report with emoji to a Unicode writer``(): unit =
    use output = new StringWriter()
    (ExecutionUi.PlainUi output :> ExecutionUi.IExecutionUi).WriteReport sampleReport
    Assert.Equal<string list>(sampleReportLines true, lines(output.ToString()))

[<Fact>]
let ``Plain UI writes the report in ASCII to a non-Unicode writer``(): unit =
    use output = new AsciiWriter()
    (ExecutionUi.PlainUi output :> ExecutionUi.IExecutionUi).WriteReport sampleReport
    Assert.Equal<string list>(sampleReportLines false, lines(output.ToString()))

[<Fact>]
let ``Plain UI writes nothing for an empty report``(): unit =
    use output = new StringWriter()
    (ExecutionUi.PlainUi output :> ExecutionUi.IExecutionUi).WriteReport { Items = [] }
    Assert.Equal("", output.ToString())

[<Theory>]
[<InlineData true>]
[<InlineData false>]
let ``Spectre UI writes the report according to the console Unicode support``(unicode: bool): unit =
    use output = new StringWriter()
    let console = createInteractiveConsole output
    console.Profile.Capabilities.Unicode <- unicode
    (ExecutionUi.SpectreUi console :> ExecutionUi.IExecutionUi).WriteReport sampleReport
    Assert.Equal<string list>(sampleReportLines unicode, lines(output.ToString()))