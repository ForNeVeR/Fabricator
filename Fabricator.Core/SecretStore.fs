// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Core

open System
open System.Collections.Immutable
open System.Security

/// <summary>
/// The name of a secret in a secret store, e.g. <c>SecretName "dns-provider-token"</c>. This is a label used to look
/// the secret up.
/// </summary>
/// <remarks>Names are compared by value: equal names in the same store denote the same secret.</remarks>
type SecretName =
    | SecretName of name: string
    override this.ToString() = let (SecretName name) = this in name

/// <summary>
/// The user interface available to a secret store while it is being opened (see
/// <see cref="M:Fabricator.Core.ISecretStore.Open(Fabricator.Core.ISecretStoreUi,Fabricator.Core.SecretStoreContext)"/>).
/// </summary>
[<Interface>]
type ISecretStoreUi =
    /// <summary>
    /// Shows the <paramref name="prompt"/> to the user and reads a secret (e.g. a password or a token) they enter. The
    /// entered text is not echoed.
    /// </summary>
    /// <param name="prompt">The prompt, e.g. <c>Enter the password:</c>.</param>
    /// <returns>
    /// The entered secret, as a read-only string. The caller owns it, and should dispose it as soon as it is not
    /// needed.
    /// </returns>
    abstract ReadSecret: prompt: string -> Async<SecureString>

/// <summary>
/// Optional information about the upcoming use of a secret store, passed to
/// <see cref="M:Fabricator.Core.ISecretStore.Open(Fabricator.Core.ISecretStoreUi,Fabricator.Core.SecretStoreContext)"/>.
/// Most stores may ignore it, but some might use it, e.g. to prefetch the secrets while opening.
/// </summary>
type SecretStoreContext =
    {
        /// <summary>
        /// The distinct names of the secrets that may be requested from the opened scope. Some of them may end up not
        /// requested, e.g. if the resource requiring them doesn't need to be applied.
        /// </summary>
        SecretNames: ImmutableArray<SecretName>
    }

/// <summary>
/// An opened secret store, see
/// <see cref="M:Fabricator.Core.ISecretStore.Open(Fabricator.Core.ISecretStoreUi,Fabricator.Core.SecretStoreContext)"/>.
/// Disposing the scope closes it.
/// </summary>
[<Interface>]
type ISecretStoreScope =
    inherit IDisposable

    /// <summary>Gets the secret with the passed name.</summary>
    /// <param name="name">The name of the secret.</param>
    /// <returns>
    /// A new string instance on every call. The caller owns it and disposes it as soon as it is not needed, so it
    /// should not be shared with the store or with other callers. <c>null</c> if the secret was not found in the store.
    /// </returns>
    /// <remarks>
    /// May be called concurrently, so the implementations should be thread-safe. Only called between the scope opening
    /// and its disposal.
    /// </remarks>
    abstract GetSecret: name: SecretName -> Async<SecureString | null>

/// <summary>
/// A secret store: a storage of the secrets (passwords, tokens, etc.) required by the resources, see
/// <see cref="P:Fabricator.Core.Resource.Secrets"/>.
/// </summary>
/// <remarks>
/// <para>
/// Before running any resource checks or applications, Fabricator opens every store required by them once, and
/// disposes the opened scopes after all the checks and applications are finished. The stores are opened one by one
/// (never concurrently), so they are free to interact with the user while opening.
/// </para>
/// <para>
/// Stores are compared by reference: every store object is a separate store, no matter how its <c>Equals</c> is
/// defined.
/// </para>
/// </remarks>
[<Interface>]
type ISecretStore =
    /// <summary>Opens the store.</summary>
    /// <param name="ui">
    /// The user interface the store may use while opening, e.g. to ask for the password of the store itself.
    /// </param>
    /// <param name="context">Optional information about the upcoming use of the store.</param>
    /// <returns>The opened scope, which is disposed by Fabricator after the execution.</returns>
    abstract Open: ui: ISecretStoreUi * context: SecretStoreContext -> Async<ISecretStoreScope>

/// <summary>
/// Defines which operations of a resource require a secret, see <see cref="T:Fabricator.Core.SecretRef"/>.
/// </summary>
[<RequireQualifiedAccess>]
type SecretRequestMode =
    /// <summary>Only the check (<see cref="P:Fabricator.Core.Resource.AlreadyApplied"/>) requires the secret.</summary>
    | CheckOnly
    /// <summary>Only the application (<see cref="P:Fabricator.Core.Resource.Apply"/>) requires the secret.</summary>
    | ApplyOnly
    /// Both the check and the application require the secret.
    | CheckAndApply

/// <summary>
/// A reference to a secret required by a resource: the store, the name of the secret in the store, and the operations
/// requiring it. See <see cref="P:Fabricator.Core.Resource.Secrets"/>.
/// </summary>
/// <remarks>
/// References are compared by reference: every reference object is a separate key of
/// <see cref="P:Fabricator.Core.ResourceContext.Secrets"/>, even if its contents are equal to some other reference's.
/// </remarks>
[<ReferenceEquality>]
type SecretRef =
    {
        /// The store to get the secret from.
        Store: ISecretStore
        /// The name of the secret in the store.
        Name: SecretName
        /// The operations of the resource requiring the secret.
        RequestMode: SecretRequestMode
    }
    override this.ToString() = $"{this.Name} ({this.RequestMode})"
