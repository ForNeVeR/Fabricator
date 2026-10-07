// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Core.ResourceTests

open System.Threading.Tasks
open Fabricator.Core
open Fabricator.Tests.FakeResource
open Xunit

[<Fact>]
let ``dependsOn keeps the behavior of the original resource``(): Task = task {
    let log = EventLog()
    let inner = FakeResource("Inner", log)
    let copy = inner.Resource |> Resource.dependsOn []

    Assert.Equal("Inner", copy.PresentableName)
    let! applied = copy.AlreadyApplied()
    Assert.False applied
    do! copy.Apply()
    let! applied = copy.AlreadyApplied()
    Assert.True applied
    Assert.Equal<string list>([ "check Inner"; "apply Inner"; "check Inner" ], log.Events)
}

[<Fact>]
let ``dependsOn combines own and additional dependencies``(): unit =
    let log = EventLog()
    let own, additional = FakeResource("Own", log), FakeResource("Additional", log)
    let inner = FakeResource("Inner", log, own)

    let copy = inner.Resource |> Resource.dependsOn [ additional.Resource ]

    Assert.Equal(2, copy.DependsOn.Count)
    Assert.True(copy.DependsOn.Contains own.Resource)
    Assert.True(copy.DependsOn.Contains additional.Resource)

[<Fact>]
let ``dependsOn creates a separate resource``(): unit =
    let inner = FakeResource("Inner", EventLog())
    let copy = inner.Resource |> Resource.dependsOn []
    Assert.NotEqual(inner.Resource, copy)

[<Fact>]
let ``NoDependencies is empty``(): unit =
    Assert.Empty Resource.NoDependencies
