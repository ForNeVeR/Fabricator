// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Core

/// A change of a text entity, e.g. a file, presented as a patch.
type Diff =
    {
        /// The name of the entity shown in the patch header, e.g. the file path.
        Name: string
        /// The old text, or <c>None</c> if the entity doesn't exist yet and is going to be created.
        OldText: string option
        /// The new text.
        NewText: string
    }

/// <summary>
/// A change of the environment required to apply a resource (as returned from
/// <see cref="P:Fabricator.Core.Resource.AlreadyApplied"/>), or made by applying it (as returned from
/// <see cref="P:Fabricator.Core.Resource.Apply"/>).
/// </summary>
type ResourceChange =
    /// No changes are required, or none were made.
    | NoChanges
    /// A change of a text entity, e.g. a file. Shown as a patch.
    | TextDiff of Diff
    /// <summary>
    /// A human-readable description of the change, e.g. <c>new service "name"</c>. May contain several lines.
    /// </summary>
    | NamedChange of description: string
    /// A change the resource is not able to describe.
    | ChangeWithNoDescription
