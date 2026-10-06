// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Core.ResourceTests

open System.Threading.Tasks
open Fabricator.Core
open Fabricator.Tests.FakeResource
open Xunit

[<Fact>]
let ``dependsOn delegates to the wrapped resource``(): Task = task {
    let log = EventLog()
    let inner = FakeResource("Inner", log)
    let wrapped = inner |> Resource.dependsOn []

    Assert.Equal("Inner", wrapped.PresentableName)
    let! applied = wrapped.AlreadyApplied()
    Assert.False applied
    do! wrapped.Apply()
    let! applied = wrapped.AlreadyApplied()
    Assert.True applied
    Assert.Equal<string list>([ "check Inner"; "apply Inner"; "check Inner" ], log.Events)
}

[<Fact>]
let ``dependsOn combines own and additional dependencies``(): unit =
    let log = EventLog()
    let inner, own, additional = FakeResource("Inner", log), FakeResource("Own", log), FakeResource("Additional", log)
    inner.DependOn own

    let wrapped = inner |> Resource.dependsOn [ additional ]

    Assert.Equal(2, wrapped.DependsOn.Count)
    Assert.True(wrapped.DependsOn.Contains own)
    Assert.True(wrapped.DependsOn.Contains additional)

[<Fact>]
let ``dependsOn reflects later changes of the wrapped resource dependencies``(): unit =
    let log = EventLog()
    let inner, own = FakeResource("Inner", log), FakeResource("Own", log)
    let wrapped = inner |> Resource.dependsOn []

    inner.DependOn own

    Assert.True(wrapped.DependsOn.Contains own)

[<Fact>]
let ``NoDependencies is empty``(): unit =
    Assert.Empty Resource.NoDependencies
