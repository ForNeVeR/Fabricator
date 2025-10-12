// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Core.FrameworkUtil

open System.IO
open System.Reflection

let getApplicationDirectoryPath(): string =
    let assemblyPath = Assembly.GetEntryAssembly().Location
    Path.GetDirectoryName assemblyPath
