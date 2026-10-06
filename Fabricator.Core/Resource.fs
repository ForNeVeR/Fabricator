// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

/// Helper functions to work with <see cref="T:Fabricator.Core.IResource"/>.
module Fabricator.Core.Resource

open System.Collections.Generic
open System.Collections.Immutable

/// An empty dependency set, to be used for resources without dependencies.
let NoDependencies: IReadOnlySet<IResource> = ImmutableHashSet<IResource>.Empty

/// <summary>
/// Creates a resource that behaves like <paramref name="resource"/>, but additionally depends on
/// <paramref name="dependencies"/> (on top of the resource's own <see cref="P:Fabricator.Core.IResource.DependsOn"/>).
/// </summary>
/// <remarks>
/// <para>
/// The result is a separate resource that is not equal to <paramref name="resource"/>: if both the original and the
/// wrapped resources are used in the same dependency graph, they will be checked and applied independently.
/// </para>
/// <para>
/// The <paramref name="dependencies"/> sequence is enumerated once, when this function is called. The original
/// resource's dependencies are read every time the result's <see cref="P:Fabricator.Core.IResource.DependsOn"/> is
/// accessed.
/// </para>
/// </remarks>
/// <param name="dependencies">Additional dependencies of the resource.</param>
/// <param name="resource">The resource to wrap.</param>
let dependsOn (dependencies: IResource seq) (resource: IResource): IResource =
    let dependencies = Seq.toArray dependencies
    { new IResource with
        member _.PresentableName = resource.PresentableName
        member _.AlreadyApplied() = resource.AlreadyApplied()
        member _.Apply() = resource.Apply()
        member _.DependsOn = ImmutableHashSet.CreateRange(Seq.append resource.DependsOn dependencies)
    }
