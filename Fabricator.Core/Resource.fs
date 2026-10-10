// SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Core

open System.Collections.Immutable

/// <summary>
/// A group of resources sharing some part of the environment that is not safe to check or change concurrently, e.g.
/// the same file or the same package manager. Fabricator never runs checks or applications of resources from the same
/// group concurrently.
/// </summary>
/// <remarks>Groups with equal names are the same group.</remarks>
type ConcurrencyGroup = ConcurrencyGroup of name: string

/// <summary>
/// A resource is a part of the desired environment state that Fabricator is able to check and apply.
/// </summary>
/// <remarks>
/// <para>
/// Resources are compared by reference: every resource object is a separate resource, even if its contents are equal
/// to some other resource's. The same resource object is checked and applied at most once per execution, no matter how
/// many times it is passed to Fabricator or mentioned in dependencies of other resources.
/// </para>
/// <para>
/// Since the dependencies of a resource have to exist before the resource itself is created, the dependency graph
/// cannot contain cycles.
/// </para>
/// </remarks>
[<ReferenceEquality>]
type Resource =
    {
        /// <summary>A human-readable name of the resource, used for logging and status reporting.</summary>
        PresentableName: string

        /// <summary>
        /// The resources this resource depends on. They are not required to be passed to Fabricator explicitly: they
        /// are discovered from the resources passed to Fabricator transitively.
        /// </summary>
        /// <remarks>
        /// <para>
        /// While applying, the dependencies that are not applied yet get applied before this resource, and this
        /// resource is only checked after all its dependencies are in their desired state. If any of the dependencies
        /// fails to be checked or applied, this resource is skipped.
        /// </para>
        /// <para>
        /// While only checking the environment, all the dependencies are checked together with this resource,
        /// independently of each other.
        /// </para>
        /// <para>
        /// Use <see cref="P:Fabricator.Core.ResourceModule.NoDependencies"/> for resources without dependencies.
        /// </para>
        /// </remarks>
        DependsOn: ImmutableHashSet<Resource>

        /// <summary>
        /// Checks whether the resource is already in its desired state in the current environment. Should return
        /// <see cref="F:Fabricator.Core.ResourceChange.NoChanges"/> if the resource is already applied and requires no
        /// further action, or the change required to apply it otherwise.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The returned change is shown to the user as the change about to be made. Return
        /// <see cref="F:Fabricator.Core.ResourceChange.ChangeWithNoDescription"/> if the resource is not applied, but
        /// the change cannot be described.
        /// </para>
        /// <para>
        /// This function should not change the environment. It may be called concurrently with checks and
        /// applications of other resources. While applying, it is only called after all the dependencies from
        /// <see cref="P:Fabricator.Core.Resource.DependsOn"/> are applied.
        /// </para>
        /// <para>
        /// The passed context is pinned to this check: use its reporter to report the status, progress, and log of the
        /// check.
        /// </para>
        /// </remarks>
        AlreadyApplied: ResourceContext -> Async<ResourceChange>

        /// <summary>
        /// Brings the resource to its desired state in the current environment. Should return the change that has been
        /// made, which is shown to the user.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only called after <see cref="P:Fabricator.Core.Resource.AlreadyApplied"/> returned a change other than
        /// <see cref="F:Fabricator.Core.ResourceChange.NoChanges"/>, and after
        /// all the dependencies from <see cref="P:Fabricator.Core.Resource.DependsOn"/> are applied (the ones not
        /// applied yet are applied first). May be called concurrently with checks and applications of other resources
        /// that are not connected to this one via dependencies.
        /// </para>
        /// <para>
        /// The passed context is pinned to this application: use its reporter to report the status, progress, and log
        /// of the application.
        /// </para>
        /// </remarks>
        Apply: ResourceContext -> Async<ResourceChange>

        /// <summary>
        /// The concurrency group of the resource, or <c>None</c> if the resource can be checked and applied
        /// concurrently with any other resources it's not connected to via dependencies.
        /// </summary>
        /// <remarks>
        /// Checks and applications of resources from the same group never run concurrently. Fabricator doesn't keep
        /// the group locked between the check and the application of the same resource, so the application should
        /// not rely on the environment staying unchanged since the check.
        /// </remarks>
        Lock: ConcurrencyGroup option
    }
    override this.ToString() = this.PresentableName

/// Helper functions to work with <see cref="T:Fabricator.Core.Resource"/>.
module Resource =

    /// An empty dependency set, to be used for resources without dependencies.
    let NoDependencies: ImmutableHashSet<Resource> = ImmutableHashSet<Resource>.Empty
