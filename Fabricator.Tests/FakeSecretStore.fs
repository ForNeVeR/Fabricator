// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.FakeSecretStore

open System.Collections.Concurrent
open System.Security
open Fabricator.Core
open Fabricator.Tests.FakeResource

/// <summary>
/// A secret store recording its events to the log: <c>open S [names]</c>, <c>opened S</c>, <c>get S name</c>, and
/// <c>dispose S</c>. Returns the secret named <c>name</c> as the string <c>S:name</c>.
/// </summary>
type FakeSecretStore(name: string, log: EventLog) =
    member val OpenError: exn option = None with get, set
    member val GetError: exn option = None with get, set
    member val OnOpen: unit -> Async<unit> = (fun () -> async.Return()) with get, set
    /// All the secrets returned by the store's scope.
    member val Returned = ConcurrentQueue<SecureString>()

    /// Creates a new reference to a secret of this store.
    member this.Ref(secretName: string, mode: SecretRequestMode): SecretRef =
        { Store = this; Name = SecretName secretName; RequestMode = mode }

    override _.ToString() = name

    interface ISecretStore with
        member this.Open(_, context) = async {
            log.Add $"""open {name} [{context.SecretNames |> Seq.map string |> String.concat ", "}]"""
            do! this.OnOpen()
            match this.OpenError with
            | Some e -> return raise e
            | None ->
                log.Add $"opened {name}"
                return {
                    new ISecretStoreScope with
                        member _.GetSecret secretName = async {
                            log.Add $"get {name} {secretName}"
                            match this.GetError with
                            | Some e -> return raise e
                            | None ->
                                let secret = Secrets.OfString $"{name}:{secretName}"
                                this.Returned.Enqueue secret
                                return secret
                        }
                        member _.Dispose() = log.Add $"dispose {name}"
                }
        }

/// A store equal to any other object, to check that the stores are compared by reference.
type EqualToEverythingStore(name: string, log: EventLog) =
    inherit FakeSecretStore(name, log)
    override _.Equals _ = true
    override _.GetHashCode() = 0
