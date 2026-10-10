// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Console

open System
open System.Collections.Generic
open System.Threading.Channels
open System.Threading.Tasks

/// <summary>
/// The output of the logs of concurrently running operations that never interleaves the lines of different operations.
/// </summary>
/// <remarks>
/// <para>
/// Every operation writes to its own log, and the logs are written to the sink in the order they were opened: all the
/// lines of a log are written before the lines of the next one. So, the lines of the first log that is not completed
/// yet are written as soon as possible, while the lines of the later logs are buffered until all the earlier logs are
/// completed.
/// </para>
/// <para>
/// The sink receives the lines in batches: all the lines of a log available at the moment are passed in a single call.
/// The sink is only called from a single consumer, so it is never called concurrently.
/// </para>
/// </remarks>
type internal OrderedLog(sink: IReadOnlyList<string> -> unit) =
    let logs = Channel.CreateUnbounded<Channel<string>>(UnboundedChannelOptions(SingleReader = true))

    let writeAll(log: Channel<string>): Task = task {
        let reader = log.Reader
        while! reader.WaitToReadAsync() do
            let batch = ResizeArray()
            let mutable reading = true
            while reading do
                match reader.TryRead() with
                | true, line -> batch.Add line
                | false, _ -> reading <- false
            if batch.Count > 0 then sink batch
    }

    let pump: Task = task {
        let reader = logs.Reader
        while! reader.WaitToReadAsync() do
            let mutable reading = true
            while reading do
                match reader.TryRead() with
                | true, log -> do! writeAll log
                | false, _ -> reading <- false
    }

    /// <summary>
    /// Opens a new log, which goes after all the logs opened earlier. Complete the returned writer when the operation is
    /// finished; the lines written after that are discarded.
    /// </summary>
    /// <exception cref="T:System.InvalidOperationException">If called after <see cref="M:CompleteAsync"/>.</exception>
    member _.Open(): ChannelWriter<string> =
        let log = Channel.CreateUnbounded<string>(UnboundedChannelOptions(SingleReader = true))
        if not(logs.Writer.TryWrite log) then
            raise <| InvalidOperationException "The log is already completed, no new logs can be opened."
        log.Writer

    /// <summary>
    /// Completes after <see cref="M:CompleteAsync"/> has been called and all the logs have been written to the sink.
    /// Fails as soon as the sink fails, without waiting for <see cref="M:CompleteAsync"/>; no lines are written after
    /// that.
    /// </summary>
    member _.Completion: Task = pump

    /// <summary>
    /// Forbids opening new logs, and waits for all the opened logs to be completed and written to the sink. Fails if
    /// the sink has failed.
    /// </summary>
    member _.CompleteAsync(): Task =
        logs.Writer.TryComplete() |> ignore
        pump
