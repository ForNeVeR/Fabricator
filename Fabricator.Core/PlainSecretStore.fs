// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Core

open System
open System.Collections.Generic
open System.Security

/// The scope of an opened <see cref="T:Fabricator.Core.PlainSecretStore"/>, owning the entered secrets.
type private PlainSecretStoreScope() =
    let lockObj = obj()
    let secrets = Dictionary<SecretName, SecureString>()
    let mutable disposed = false

    member _.Add(name: SecretName, secret: SecureString) =
        lock lockObj (fun () ->
            if disposed then
                secret.Dispose()
                raise <| ObjectDisposedException "PlainSecretStore"
            secrets[name] <- secret
        )

    member _.Contains(name: SecretName) = lock lockObj (fun () -> secrets.ContainsKey name)

    interface ISecretStoreScope with
        member _.GetSecret name = async {
            return lock lockObj (fun () ->
                if disposed then raise <| ObjectDisposedException "PlainSecretStore"
                match secrets.TryGetValue name with
                | true, secret -> secret.Copy()
                | false, _ ->
                    raise <| KeyNotFoundException
                        $"Secret \"{name}\" was not requested when opening the PlainSecretStore."
            )
        }

        member _.Dispose() =
            lock lockObj (fun () ->
                if not disposed then
                    disposed <- true
                    for secret in secrets.Values do
                        secret.Dispose()
                    secrets.Clear()
            )

/// <summary>
/// A secret store asking the user to enter every requested secret when opening, and keeping the entered secrets in
/// the process memory until the end of the execution.
/// </summary>
/// <remarks>
/// <para>
/// <b>This store is not secure.</b> The secrets are pasted or typed into the console, and kept in the process memory
/// for the whole Fabricator session (<see cref="T:System.Security.SecureString"/> provides little actual protection).
/// Only use it for simple cases, when you don't want to get your secrets from a dedicated storage, and are fine with
/// entering them manually on every run.
/// </para>
/// <para>
/// The user is asked for every secret name listed in <see cref="P:Fabricator.Core.SecretStoreContext.SecretNames"/>
/// once, in the listed order. Requesting a secret that was not listed fails.
/// </para>
/// </remarks>
type PlainSecretStore() =
    interface ISecretStore with
        member _.Open(ui, context) = async {
            let scope = new PlainSecretStoreScope()
            let opened = ref false
            try
                for name in context.SecretNames do
                    if not(scope.Contains name) then
                        let! secret = ui.ReadSecret $"Enter the secret \"{name}\": "
                        scope.Add(name, secret)
                opened.Value <- true
            finally
                // Also covers cancellation, which skips the exception handlers of async computations.
                if not opened.Value then (scope :> IDisposable).Dispose()
            return scope :> ISecretStoreScope
        }
