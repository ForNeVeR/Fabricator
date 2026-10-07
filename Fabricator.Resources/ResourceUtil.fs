// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module internal Fabricator.Resources.ResourceUtil

open System.Collections.Immutable
open Fabricator.Core

/// Converts an optional dependency sequence passed to a resource factory to a dependency set.
let dependencies(dependsOn: Resource seq option): ImmutableHashSet<Resource> =
    match dependsOn with
    | None -> Resource.NoDependencies
    | Some dependencies -> ImmutableHashSet.CreateRange dependencies
