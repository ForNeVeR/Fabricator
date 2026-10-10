// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

/// Helpers to describe the changes of text and file contents.
module internal Fabricator.Resources.Diffs

open System
open System.Text
open Fabricator.Core

/// <summary>Describes a change of a text entity as a diff.</summary>
/// <param name="name">The name of the entity, shown in the diff header, e.g. the file path.</param>
/// <param name="oldText">The old text, or <c>None</c> if the entity doesn't exist yet.</param>
/// <param name="newText">The new text.</param>
/// <returns><see cref="F:Fabricator.Core.ResourceChange.NoChanges"/> if the texts are equal, the diff otherwise.</returns>
let textChange (name: string) (oldText: string option) (newText: string): ResourceChange =
    if oldText = Some newText then NoChanges
    else TextDiff { Name = name; OldText = oldText; NewText = newText }

let private strictUtf8 = UTF8Encoding(encoderShouldEmitUTF8Identifier = false, throwOnInvalidBytes = true)

/// Decodes the content as text, or returns None if the content is binary: contains zero bytes or invalid UTF-8.
let private tryDecodeText(content: byte[]): string option =
    if Array.contains 0uy content then None
    else
        try
            Some(strictUtf8.GetString content)
        with
        | :? DecoderFallbackException -> None

/// The maximum size of a file content, in bytes, which is shown as a diff. Larger files are described by their sizes.
let MaxTextDiffSize: int64 = 1024L * 1024L

/// <summary>Describes a change of a file content by its sizes, without the content itself.</summary>
/// <param name="name">The name of the file, e.g. the file path.</param>
/// <param name="oldSize">The old content size in bytes, or <c>None</c> if the file doesn't exist yet.</param>
/// <param name="newSize">The new content size in bytes.</param>
let sizeChange (name: string) (oldSize: int64 option) (newSize: int64): ResourceChange =
    match oldSize with
    | None -> NamedChange $"new file \"{name}\", {newSize} bytes"
    | Some oldSize -> NamedChange $"file update \"{name}\", {oldSize} bytes → {newSize} bytes"

/// <summary>Describes a change of a file content.</summary>
/// <param name="name">The name of the file, shown in the diff header, e.g. the file path.</param>
/// <param name="oldContent">The old content, or <c>None</c> if the file doesn't exist yet.</param>
/// <param name="newContent">The new content.</param>
/// <returns>
/// <see cref="F:Fabricator.Core.ResourceChange.NoChanges"/> if the contents are equal, a diff if both of them are
/// text, or a description with the content sizes if any of them is binary or larger than
/// <see cref="P:Fabricator.Resources.Diffs.MaxTextDiffSize"/>.
/// </returns>
let fileChange (name: string) (oldContent: byte[] option) (newContent: byte[]): ResourceChange =
    let oldSize = oldContent |> Option.map (fun content -> int64 content.Length)
    match oldContent with
    | Some oldContent when ReadOnlySpan(oldContent).SequenceEqual(ReadOnlySpan newContent) -> NoChanges
    | _ when int64 newContent.Length > MaxTextDiffSize || oldSize |> Option.exists (fun s -> s > MaxTextDiffSize) ->
        sizeChange name oldSize newContent.Length
    | None ->
        match tryDecodeText newContent with
        | Some newText -> textChange name None newText
        | None -> NamedChange $"new binary file \"{name}\", {newContent.Length} bytes"
    | Some oldContent ->
        match tryDecodeText oldContent, tryDecodeText newContent with
        | Some oldText, Some newText -> textChange name (Some oldText) newText
        | _ -> NamedChange $"binary file update \"{name}\", {oldContent.Length} bytes → {newContent.Length} bytes"
