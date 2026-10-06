// SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Core

open System.Collections.Generic

/// <summary>
/// A resource is a part of the desired environment state that Fabricator is able to check and apply.
/// </summary>
/// <remarks>
/// Resources are compared using their <see cref="M:System.Object.Equals(System.Object)"/> and
/// <see cref="M:System.Object.GetHashCode"/> implementations: equal resources are considered to be the same resource,
/// and are checked and applied at most once per execution.
/// </remarks>
type IResource =
    /// <summary>A human-readable name of the resource, used for logging and status reporting.</summary>
    abstract member PresentableName: string

    /// <summary>
    /// Checks whether the resource is already in its desired state in the current environment.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the resource is already applied and requires no further action, <c>false</c> otherwise.
    /// </returns>
    /// <remarks>
    /// This method should not change the environment. It may be called concurrently with checks and applications of
    /// other resources.
    /// </remarks>
    abstract member AlreadyApplied: unit -> Async<bool>

    /// <summary>
    /// Brings the resource to its desired state in the current environment.
    /// </summary>
    /// <remarks>
    /// Only called after <see cref="M:Fabricator.Core.IResource.AlreadyApplied"/> returned <c>false</c>, and after
    /// all the dependencies from <see cref="P:Fabricator.Core.IResource.DependsOn"/> that required it have been
    /// successfully applied. May be called concurrently with checks and applications of other resources that are not
    /// connected to this one via dependencies.
    /// </remarks>
    abstract member Apply: unit -> Async<unit>

    /// <summary>
    /// The resources this resource depends on. They are not required to be passed to Fabricator explicitly: they
    /// are discovered from the resources passed to Fabricator transitively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dependencies are only processed when this resource is not already applied, i.e., when
    /// <see cref="M:Fabricator.Core.IResource.AlreadyApplied"/> returned <c>false</c>. In that case, while checking,
    /// all the dependencies are checked as well (recursively following the same rule); while applying, the
    /// dependencies that are not already applied get applied before this resource.
    /// </para>
    /// <para>
    /// The dependency graph should not contain cycles; Fabricator verifies this before starting the execution and
    /// reports an error in case a cycle is detected.
    /// </para>
    /// <para>
    /// The set might be changed while the resources are prepared, but should not be changed after the resources have
    /// been passed to Fabricator for execution.
    /// </para>
    /// <para>
    /// Use <see cref="P:Fabricator.Core.Resource.NoDependencies"/> for resources without dependencies, and
    /// <see cref="M:Fabricator.Core.Resource.dependsOn(System.Collections.Generic.IEnumerable{Fabricator.Core.IResource},Fabricator.Core.IResource)"/>
    /// to add dependencies to an existing resource.
    /// </para>
    /// </remarks>
    abstract member DependsOn: IReadOnlySet<IResource>
