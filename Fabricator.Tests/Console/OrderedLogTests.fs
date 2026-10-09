// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Console.OrderedLogTests

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Fabricator.Console
open Xunit

let private createLog() =
    let lines = ConcurrentQueue<string>()
    OrderedLog lines.Enqueue, lines

let private assertLines (expected: string list) (lines: ConcurrentQueue<string>) =
    Assert.Equal<string list>(expected, Seq.toList lines)

let private timeout = TimeSpan.FromSeconds 10.0

[<Fact>]
let ``The first log is written before it is completed``(): Task = task {
    let written = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let log = OrderedLog(fun line -> if line = "a1" then written.SetResult())
    let first = log.Open()

    first.TryWrite "a1" |> ignore
    do! written.Task.WaitAsync timeout

    first.Complete()
    do! log.CompleteAsync()
}

[<Fact>]
let ``A later log is written after the earlier ones``(): Task = task {
    let log, lines = createLog()
    let first = log.Open()
    let second = log.Open()

    second.TryWrite "b1" |> ignore
    first.TryWrite "a1" |> ignore
    first.TryWrite "a2" |> ignore
    second.Complete()
    first.Complete()

    do! log.CompleteAsync()
    assertLines [ "a1"; "a2"; "b1" ] lines
}

[<Fact>]
let ``Logs completed while an earlier one is open are written in their opening order``(): Task = task {
    let log, lines = createLog()
    let first = log.Open()
    let second = log.Open()
    let third = log.Open()
    let fourth = log.Open()

    third.TryWrite "c1" |> ignore
    third.Complete()
    second.TryWrite "b1" |> ignore
    second.Complete()
    fourth.TryWrite "d1" |> ignore
    first.TryWrite "a1" |> ignore
    first.Complete()
    fourth.Complete()

    do! log.CompleteAsync()
    assertLines [ "a1"; "b1"; "c1"; "d1" ] lines
}

[<Fact>]
let ``Lines written after completing a log are discarded``(): Task = task {
    let log, lines = createLog()
    let first = log.Open()
    first.TryWrite "a1" |> ignore
    first.Complete()
    Assert.False(first.TryWrite "a2")
    Assert.False(first.TryComplete())

    do! log.CompleteAsync()
    assertLines [ "a1" ] lines
}

[<Fact>]
let ``Completion waits for the open logs``(): Task = task {
    let log, lines = createLog()
    let first = log.Open()

    let completion = log.CompleteAsync()
    Assert.False completion.IsCompleted

    first.TryWrite "a1" |> ignore
    first.Complete()
    do! completion.WaitAsync timeout
    assertLines [ "a1" ] lines
}

[<Fact>]
let ``No logs can be opened after completion``(): Task = task {
    let log, _ = createLog()
    do! log.CompleteAsync()
    Assert.Throws<InvalidOperationException>(fun () -> log.Open() |> ignore) |> ignore
}

[<Fact>]
let ``Sink failure is reported on completion``(): Task = task {
    let log = OrderedLog(fun _ -> failwith "Sink failure.")
    let first = log.Open()
    first.TryWrite "a1" |> ignore
    first.Complete()

    let! ex = Assert.ThrowsAsync<Exception>(fun () -> log.CompleteAsync())
    Assert.Equal("Sink failure.", ex.Message)
}

[<Fact>]
let ``Lines of concurrent writers are never interleaved``(): Task = task {
    let log, lines = createLog()
    let logCount, lineCount = 16, 200
    let logs = Array.init logCount (fun _ -> log.Open())

    Parallel.For(0, logCount, fun i ->
        for j in 1 .. lineCount do
            logs[i].TryWrite $"{i}:{j}" |> ignore
        logs[i].Complete()
    ) |> ignore

    do! log.CompleteAsync()
    let expected = [ for i in 0 .. logCount - 1 do for j in 1 .. lineCount -> $"{i}:{j}" ]
    assertLines expected lines
}
