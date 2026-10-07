// SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Core

open System.Collections.Immutable

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
        /// <c>true</c> if the resource is already applied and requires no further action, <c>false</c> otherwise.
        /// </summary>
        /// <remarks>
        /// This function should not change the environment. It may be called concurrently with checks and
        /// applications of other resources. While applying, it is only called after all the dependencies from
        /// <see cref="P:Fabricator.Core.Resource.DependsOn"/> are applied.
        /// </remarks>
        AlreadyApplied: unit -> Async<bool>

        /// <summary>
        /// Brings the resource to its desired state in the current environment.
        /// </summary>
        /// <remarks>
        /// Only called after <see cref="P:Fabricator.Core.Resource.AlreadyApplied"/> returned <c>false</c>, and after
        /// all the dependencies from <see cref="P:Fabricator.Core.Resource.DependsOn"/> are applied (the ones not
        /// applied yet are applied first). May be called concurrently with checks and applications of other resources
        /// that are not connected to this one via dependencies.
        /// </remarks>
        Apply: unit -> Async<unit>
    }
    override this.ToString() = this.PresentableName

/// Helper functions to work with <see cref="T:Fabricator.Core.Resource"/>.
module Resource =

    /// An empty dependency set, to be used for resources without dependencies.
    let NoDependencies: ImmutableHashSet<Resource> = ImmutableHashSet<Resource>.Empty
