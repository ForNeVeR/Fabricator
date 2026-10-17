// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Console.DryRunTests

open Fabricator.Console
open Fabricator.Console.Lowering
open Fabricator.Tests.FakeResource
open Xunit

let private resource name (dependencies: FakeResource list) = FakeResource(name, EventLog(), Array.ofList dependencies)

let private actions(plan: DryRunPlan) =
    plan.Actions |> Array.map (fun t -> t.Kind, t.Resource.PresentableName)

/// The roots of a graph with a shared dependency, and its dependencies of different names.
let private sampleRoots() =
    let shared = resource "Shared" []
    let z, y = resource "Z" [ shared ], resource "Y" []
    let root1 = resource "Root1" [ z; y ]
    let root2 = resource "Root2" [ shared ]
    [ root1.Resource; root2.Resource; z.Resource ]

[<Fact>]
let ``Check plan lists a check of every resource, dependencies first``(): unit =
    let plan = DryRun.create CheckOnly (sampleRoots())
    Assert.Equal<(TaskKind * string) seq>(
        [ Check, "Y"; Check, "Shared"; Check, "Z"; Check, "Root1"; Check, "Root2" ],
        actions plan
    )

[<Fact>]
let ``Apply plan lists a check and an application of every resource, dependencies first``(): unit =
    let plan = DryRun.create CheckAndApply (sampleRoots())
    Assert.Equal<(TaskKind * string) seq>(
        [
            Check, "Y"; Apply, "Y"
            Check, "Shared"; Apply, "Shared"
            Check, "Z"; Apply, "Z"
            Check, "Root1"; Apply, "Root1"
            Check, "Root2"; Apply, "Root2"
        ],
        actions plan
    )

[<Fact>]
let ``Plan of no resources is empty``(): unit =
    Assert.Empty((DryRun.create CheckAndApply []).Actions)

[<Fact>]
let ``Action lines are formatted with emoji``(): unit =
    let r = resource "R" []
    Assert.Equal("⏸️ R: check", DryRun.formatLine true { Kind = Check; Resource = r.Resource })
    Assert.Equal("⏸️ R: apply if not applied", DryRun.formatLine true { Kind = Apply; Resource = r.Resource })

[<Fact>]
let ``Action lines are formatted in ASCII``(): unit =
    let r = resource "R" []
    Assert.Equal("[ ] R: check", DryRun.formatLine false { Kind = Check; Resource = r.Resource })
    Assert.Equal("[ ] R: apply if not applied", DryRun.formatLine false { Kind = Apply; Resource = r.Resource })

[<Theory>]
[<InlineData(0, "0 actions")>]
[<InlineData(1, "1 action")>]
[<InlineData(2, "2 actions")>]
let ``Summary counts the actions``(count: int, expected: string): unit =
    let r = resource "R" []
    let plan = { Actions = Array.replicate count { Kind = Check; Resource = r.Resource } }
    Assert.Equal(expected, DryRun.formatSummary plan)
