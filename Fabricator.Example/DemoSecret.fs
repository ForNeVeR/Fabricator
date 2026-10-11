// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

/// <summary>
/// Resources demonstrating the secrets passed to the resources. None of them changes anything.
/// </summary>
/// <remarks>
/// <b>Note that the <see cref="T:Fabricator.Core.PlainSecretStore"/> used here is insecure:</b> the secrets are entered
/// into the console, and kept in the process memory for the whole session. It is only used to demonstrate the API; use
/// a dedicated secret storage for real credentials.
/// </remarks>
module Fabricator.DemoSecret

open System.Collections.Immutable
open Fabricator.Core

let private insecureNote = "Note: the demo uses PlainSecretStore, which is insecure; don't use it for real credentials."

let private store = PlainSecretStore()

/// Uses the secret, disposing it right after, and then continues with the steps not requiring it.
let private useSecret (ctx: ResourceContext) (secret: SecretRef) (operation: string) = async {
    ctx.Reporter.Log insecureNote
    do
        use value = ctx.Secrets[secret]
        ctx.Reporter.Log $"{operation}: received secret \"{secret.Name}\" of length {value.Length}."
    ctx.Reporter.Status "Working without the secret…"
    do! Async.Sleep 500
}

let private demo (name: string) (secrets: SecretRef list) (check: ResourceContext -> Async<unit>)
                 (apply: ResourceContext -> Async<unit>): Resource = {
    PresentableName = $"Demo secret: {name}"
    DependsOn = Resource.NoDependencies
    Lock = None
    Secrets = ImmutableArray.CreateRange secrets
    AlreadyApplied = fun ctx -> async {
        do! check ctx
        return ChangeWithNoDescription
    }
    Apply = fun ctx -> async {
        do! apply ctx
        return ChangeWithNoDescription
    }
}

let private noAction (_: ResourceContext) = async.Return()

let private checkAndApply =
    let secret = { Store = store; Name = SecretName "check-and-apply"; RequestMode = SecretRequestMode.CheckAndApply }
    demo "check and apply" [ secret ] (fun ctx -> useSecret ctx secret "check") (fun ctx -> useSecret ctx secret "apply")

/// Two references to the same secret: the store asks for it once.
let private applyOnly =
    let reference() = { Store = store; Name = SecretName "apply-only"; RequestMode = SecretRequestMode.ApplyOnly }
    let first, second = reference(), reference()
    demo "apply only" [ first; second ] noAction (fun ctx -> async {
        do! useSecret ctx first "apply (first reference)"
        do! useSecret ctx second "apply (second reference)"
    })

let private noSecrets = demo "no secrets" [] noAction noAction

/// Creates the demo resources.
let resources(): Resource list = [
    checkAndApply
    applyOnly
    noSecrets
]
