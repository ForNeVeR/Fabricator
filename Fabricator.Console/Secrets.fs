// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

/// Opening of the secret stores required by an execution, and fetching of the secrets for its operations.
module internal Fabricator.Console.Secrets

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Runtime.ExceptionServices
open System.Security
open Fabricator.Core

/// Disposes all the objects, even if some of them fail. Returns the errors.
let private tryDisposeAll(disposables: IDisposable seq): exn[] = [|
    for disposable in disposables do
        try
            disposable.Dispose()
        with
        | e -> e
|]

/// Disposes all the objects, even if some of them fail; then throws the error if there was one.
let private disposeAll(disposables: IDisposable seq): unit =
    match tryDisposeAll disposables with
    | [| |] -> ()
    | [| e |] -> ExceptionDispatchInfo.Capture(e).Throw()
    | errors -> raise <| AggregateException errors

/// The secrets fetched for a single operation. Disposing this object disposes all the secrets.
type SecretValues(values: IReadOnlyDictionary<SecretRef, SecureString>) =
    member _.Values: IReadOnlyDictionary<SecretRef, SecureString> = values

    interface IDisposable with
        member _.Dispose() = disposeAll(values.Values |> Seq.cast)

/// <summary>The scopes of the opened stores. Disposing this object disposes all the scopes.</summary>
/// <param name="scopes">The opened scopes by their stores, compared by reference.</param>
/// <param name="openingOrder">The scopes in the order of opening.</param>
type OpenScopes(scopes: IReadOnlyDictionary<ISecretStore, ISecretStoreScope>, openingOrder: ISecretStoreScope list) =
    /// <summary>Fetches the secrets from their stores, one by one; each distinct reference is fetched once.</summary>
    /// <remarks>If any secret fails to be fetched, the ones already fetched are disposed.</remarks>
    member _.Fetch(secrets: SecretRef seq): Async<SecretValues> = async {
        let values = Dictionary<SecretRef, SecureString>(HashIdentity.Reference)
        let result = new SecretValues(values)
        let mutable success = false
        try
            for secret in secrets do
                if not(values.ContainsKey secret) then
                    let scope =
                        match scopes.TryGetValue secret.Store with
                        | true, scope -> scope
                        | false, _ -> raise <| InvalidOperationException $"The store of secret {secret} is not opened."
                    let! value = scope.GetSecret secret.Name
                    match value with
                    | Null ->
                        raise <| InvalidOperationException $"The store has returned null for secret {secret}."
                    | NonNull value -> values[secret] <- value
            success <- true
        finally
            // Also covers cancellation.
            if not success then tryDisposeAll [ result ] |> ignore
        return result
    }

    interface IDisposable with
        member _.Dispose() = disposeAll(openingOrder |> Seq.rev |> Seq.cast)

/// <summary>Opens the stores of the passed secrets, one by one, in the order of their first mention.</summary>
/// <param name="ui">The UI passed to the stores.</param>
/// <param name="secrets">The secrets that may be fetched from the opened scopes.</param>
/// <remarks>
/// Stores are compared by reference. Each store receives the distinct names of its secrets, in the order of their first
/// mention. If any store fails to open, the scopes opened so far are disposed.
/// </remarks>
let openStores (ui: ISecretStoreUi) (secrets: SecretRef seq): Async<OpenScopes> = async {
    let names = Dictionary<ISecretStore, ResizeArray<SecretName>>(HashIdentity.Reference)
    let stores = ResizeArray<ISecretStore>()
    for secret in secrets do
        match names.TryGetValue secret.Store with
        | true, storeNames -> if not(storeNames.Contains secret.Name) then storeNames.Add secret.Name
        | false, _ ->
            names[secret.Store] <- ResizeArray [ secret.Name ]
            stores.Add secret.Store

    let scopes = Dictionary<ISecretStore, ISecretStoreScope>(HashIdentity.Reference)
    let opened = ResizeArray<ISecretStoreScope>()
    let succeeded = ref false
    try
        for store in stores do
            let! scope = store.Open(ui, { SecretNames = names[store].ToImmutableArray() })
            if obj.ReferenceEquals(scope, null) then
                raise <| InvalidOperationException $"Secret store \"{store}\" has returned a null scope."
            scopes[store] <- scope
            opened.Add scope
        succeeded.Value <- true
    finally
        // Also covers cancellation, which skips the exception handlers of async computations. The original error is
        // more important than the disposal ones, so the latter are ignored.
        if not succeeded.Value then tryDisposeAll(opened |> Seq.rev |> Seq.cast) |> ignore
    return new OpenScopes(scopes, List.ofSeq opened)
}
