// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.FakeResource

open System
open System.Collections.Concurrent
open System.Collections.Immutable
open Fabricator.Core

/// A thread-safe log of the events happening to fake resources.
type EventLog() =
    let events = ConcurrentQueue<string>()
    member _.Add(event: string) = events.Enqueue event
    member _.Events: string list = Seq.toList events
    member this.IndexOf(event: string): int = List.findIndex ((=) event) this.Events

/// A resource recording its checks and applications to the log.
type FakeResource(name: string, log: EventLog, lock: ConcurrencyGroup option, dependencies: FakeResource[]) as this =
    let resource = {
        PresentableName = name
        DependsOn = ImmutableHashSet.CreateRange(dependencies |> Seq.map (fun (d: FakeResource) -> d.Resource))
        Lock = lock
        AlreadyApplied = fun ctx -> async {
            log.Add $"check {name}"
            do! this.OnCheck()
            do! this.OnCheckWithContext ctx
            match this.CheckError with
            | Some e -> return raise e
            | None -> return if this.IsApplied then NoChanges else this.Change
        }
        Apply = fun ctx -> async {
            log.Add $"apply {name}"
            do! this.OnApply()
            do! this.OnApplyWithContext ctx
            match this.ApplyError with
            | Some e -> return raise e
            | None ->
                this.IsApplied <- true
                return this.Change
        }
    }

    new(name: string, log: EventLog, [<ParamArray>] dependencies: FakeResource[]) =
        FakeResource(name, log, None, dependencies)

    member val IsApplied = false with get, set
    /// The change reported by the check while not applied, and by the application.
    member val Change = ChangeWithNoDescription with get, set
    member val CheckError: exn option = None with get, set
    member val ApplyError: exn option = None with get, set
    member val OnCheck: unit -> Async<unit> = (fun () -> async.Return()) with get, set
    member val OnApply: unit -> Async<unit> = (fun () -> async.Return()) with get, set
    member val OnCheckWithContext: ResourceContext -> Async<unit> = (fun _ -> async.Return()) with get, set
    member val OnApplyWithContext: ResourceContext -> Async<unit> = (fun _ -> async.Return()) with get, set

    /// The resource itself, always the same object for the same fake.
    member _.Resource: Resource = resource
