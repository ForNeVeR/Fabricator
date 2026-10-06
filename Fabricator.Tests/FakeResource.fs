// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.FakeResource

open System.Collections.Concurrent
open System.Collections.Generic
open Fabricator.Core

/// A thread-safe log of the events happening to fake resources.
type EventLog() =
    let events = ConcurrentQueue<string>()
    member _.Add(event: string) = events.Enqueue event
    member _.Events: string list = Seq.toList events
    member this.IndexOf(event: string): int = List.findIndex ((=) event) this.Events

/// A resource recording its checks and applications to the log.
type FakeResource(name: string, log: EventLog) =
    let dependencies = HashSet<IResource>()

    member val IsApplied = false with get, set
    member val CheckError: exn option = None with get, set
    member val ApplyError: exn option = None with get, set
    member val OnApply: unit -> Async<unit> = (fun () -> async.Return()) with get, set

    member this.DependOn([<System.ParamArray>] resources: IResource[]): unit =
        for resource in resources do
            dependencies.Add resource |> ignore

    interface IResource with
        member _.PresentableName = name
        member this.AlreadyApplied() = async {
            log.Add $"check {name}"
            match this.CheckError with
            | Some e -> return raise e
            | None -> return this.IsApplied
        }
        member this.Apply() = async {
            log.Add $"apply {name}"
            do! this.OnApply()
            match this.ApplyError with
            | Some e -> raise e
            | None -> this.IsApplied <- true
        }
        member _.DependsOn = dependencies
