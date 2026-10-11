// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Core.PlainSecretStoreTests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Collections.Immutable
open System.Security
open System.Threading.Tasks
open Fabricator.Core
open Fabricator.Tests.FakeSecretStore
open Xunit

/// A UI answering every prompt with its own number, e.g. "secret 1", and recording the prompts.
type private ScriptedUi(?failOn: int) =
    let prompts = ConcurrentQueue<string>()
    member val Returned = ConcurrentQueue<SecureString>()
    member _.Prompts: string list = List.ofSeq prompts
    interface ISecretStoreUi with
        member this.ReadSecret prompt = async {
            prompts.Enqueue prompt
            if Some prompts.Count = failOn then failwith "Input failure."
            let secret = secretOf $"secret {prompts.Count}"
            this.Returned.Enqueue secret
            return secret
        }

let private openStore (ui: ISecretStoreUi) (names: string list) =
    let context = { SecretNames = ImmutableArray.CreateRange(names |> Seq.map SecretName) }
    (PlainSecretStore() :> ISecretStore).Open(ui, context) |> Async.StartAsTask

let private getSecret (scope: ISecretStoreScope) (name: string) =
    scope.GetSecret(SecretName name) |> Async.StartAsTask

[<Fact>]
let ``Open asks for every secret once in order``(): Task = task {
    let ui = ScriptedUi()
    use! scope = openStore ui [ "a"; "b"; "a" ]

    Assert.Equal<string list>([ "Enter the secret \"a\": "; "Enter the secret \"b\": " ], ui.Prompts)
    use! a = getSecret scope "a"
    use! b = getSecret scope "b"
    Assert.Equal("secret 1", plain a)
    Assert.Equal("secret 2", plain b)
}

[<Fact>]
let ``Every request returns an independent copy``(): Task = task {
    let ui = ScriptedUi()
    use! scope = openStore ui [ "a" ]

    let! first = getSecret scope "a"
    first.Dispose()
    use! second = getSecret scope "a"

    Assert.False(obj.ReferenceEquals(first, second))
    Assert.Equal("secret 1", plain second)
}

[<Fact>]
let ``Request of a secret not listed on opening fails``(): Task = task {
    use! scope = openStore (ScriptedUi()) [ "a" ]
    let! ex = Assert.ThrowsAsync<KeyNotFoundException>(fun () -> getSecret scope "b" :> Task)
    Assert.Contains("\"b\"", ex.Message)
}

[<Fact>]
let ``Disposed scope wipes the secrets and fails the requests``(): Task = task {
    let ui = ScriptedUi()
    let! scope = openStore ui [ "a" ]

    scope.Dispose()

    let! _ = Assert.ThrowsAsync<ObjectDisposedException>(fun () -> getSecret scope "a" :> Task)
    for secret in ui.Returned do
        Assert.Throws<ObjectDisposedException>(fun () -> secret.Length |> ignore) |> ignore
    scope.Dispose() // Repeated disposal is allowed.
}

[<Fact>]
let ``Input failure disposes the secrets entered before``(): Task = task {
    let ui = ScriptedUi(failOn = 2)

    let! ex = Assert.ThrowsAsync<Exception>(fun () -> openStore ui [ "a"; "b" ] :> Task)

    Assert.Equal("Input failure.", ex.Message)
    let entered = Assert.Single ui.Returned
    Assert.Throws<ObjectDisposedException>(fun () -> entered.Length |> ignore) |> ignore
}
