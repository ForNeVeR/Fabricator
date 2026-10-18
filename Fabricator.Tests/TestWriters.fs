// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.TestWriters

open System.IO
open System.Text

/// A writer with an encoding unable to represent emoji, so the ASCII markers are written to it.
type AsciiWriter() =
    inherit StringWriter()
    override _.Encoding = Encoding.ASCII
