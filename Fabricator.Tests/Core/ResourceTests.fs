// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Core.ResourceTests

open Fabricator.Core
open Xunit

[<Fact>]
let ``NoDependencies is empty``(): unit =
    Assert.Empty Resource.NoDependencies
