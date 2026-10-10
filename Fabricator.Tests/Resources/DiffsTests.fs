// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Resources.DiffsTests

open System.Text
open Fabricator.Core
open Fabricator.Resources
open Xunit

let private utf8(text: string) = Encoding.UTF8.GetBytes text

[<Fact>]
let ``Equal texts have no changes``(): unit =
    Assert.Equal(NoChanges, Diffs.textChange "f" (Some "a\n") "a\n")

[<Fact>]
let ``Different texts produce a diff``(): unit =
    Assert.Equal(
        TextDiff { Name = "f"; OldText = Some "a\n"; NewText = "b\n" },
        Diffs.textChange "f" (Some "a\n") "b\n"
    )

[<Fact>]
let ``Missing text produces a creation diff``(): unit =
    Assert.Equal(TextDiff { Name = "f"; OldText = None; NewText = "" }, Diffs.textChange "f" None "")

[<Fact>]
let ``Equal file contents have no changes``(): unit =
    let content = [| 0uy; 1uy; 2uy |]
    Assert.Equal(NoChanges, Diffs.fileChange "f" (Some content) (Array.copy content))

[<Fact>]
let ``Text file contents produce a diff``(): unit =
    Assert.Equal(
        TextDiff { Name = "f"; OldText = Some "привет\n"; NewText = "мир\n" },
        Diffs.fileChange "f" (Some(utf8 "привет\n")) (utf8 "мир\n")
    )

[<Fact>]
let ``New text file produces a creation diff``(): unit =
    Assert.Equal(
        TextDiff { Name = "f"; OldText = None; NewText = "text" },
        Diffs.fileChange "f" None (utf8 "text")
    )

[<Fact>]
let ``New binary file is described with its size``(): unit =
    Assert.Equal(NamedChange "new binary file \"f\", 3 bytes", Diffs.fileChange "f" None [| 1uy; 0uy; 2uy |])

[<Fact>]
let ``Content with zero bytes is binary``(): unit =
    Assert.Equal(
        NamedChange "binary file update \"f\", 4 bytes → 3 bytes",
        Diffs.fileChange "f" (Some(utf8 "text")) [| 1uy; 0uy; 2uy |]
    )
    Assert.Equal(
        NamedChange "binary file update \"f\", 3 bytes → 4 bytes",
        Diffs.fileChange "f" (Some [| 1uy; 0uy; 2uy |]) (utf8 "text")
    )

[<Fact>]
let ``Content with invalid UTF-8 is binary``(): unit =
    let invalid = [| 0x61uy; 0xFFuy; 0xFEuy |]
    Assert.Equal(
        NamedChange "binary file update \"f\", 3 bytes → 4 bytes",
        Diffs.fileChange "f" (Some invalid) (utf8 "text")
    )
    Assert.Equal(
        NamedChange "binary file update \"f\", 4 bytes → 3 bytes",
        Diffs.fileChange "f" (Some(utf8 "text")) invalid
    )
