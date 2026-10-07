// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Console.LoweringTests

open Fabricator.Console
open Fabricator.Console.Lowering
open Fabricator.Core
open Fabricator.Tests.FakeResource
open Xunit

let private resource name (dependencies: FakeResource list) = FakeResource(name, EventLog(), Array.ofList dependencies)
let private check(r: FakeResource) = { Kind = Check; Resource = r.Resource }
let private apply(r: FakeResource) = { Kind = Apply; Resource = r.Resource }

let private lowerFakes mode (roots: FakeResource list) =
    lower mode (roots |> Seq.map _.Resource)

let private assertTasks (expected: LoweredTask list) (graph: TaskExecutor.TaskGraph<LoweredTask>) =
    Assert.Equal<Set<string>>(
        expected |> Seq.map string |> Set.ofSeq,
        graph.Keys |> Seq.map string |> Set.ofSeq
    )
    Assert.Equal(expected.Length, graph.Count)

let private assertPrerequisites
    (task: LoweredTask)
    (expected: LoweredTask list)
    (graph: TaskExecutor.TaskGraph<LoweredTask>) =
    Assert.Equal<Set<string>>(
        expected |> Seq.map string |> Set.ofSeq,
        graph[task] |> Seq.map string |> Set.ofSeq
    )
    Assert.Equal(expected.Length, graph[task].Count)

[<Fact>]
let ``Check-only mode produces a check task for a single root``(): unit =
    let r = resource "R" []
    let graph = lowerFakes CheckOnly [ r ]
    assertTasks [ check r ] graph
    assertPrerequisites (check r) [] graph

[<Fact>]
let ``Check-only mode has independent check tasks for all reachable resources``(): unit =
    let e = resource "E" []
    let d = resource "D" [ e ]
    let r = resource "R" [ d ]

    let graph = lowerFakes CheckOnly [ r ]

    assertTasks [ check r; check d; check e ] graph
    assertPrerequisites (check r) [] graph
    assertPrerequisites (check d) [] graph
    assertPrerequisites (check e) [] graph

[<Fact>]
let ``Apply mode checks resources after their dependencies are applied``(): unit =
    let e = resource "E" []
    let d = resource "D" [ e ]
    let r = resource "R" [ d ]

    let graph = lowerFakes CheckAndApply [ r ]

    assertTasks [ check r; check d; check e; apply r; apply d; apply e ] graph
    assertPrerequisites (check r) [ apply d ] graph
    assertPrerequisites (check d) [ apply e ] graph
    assertPrerequisites (check e) [] graph
    assertPrerequisites (apply r) [ check r ] graph
    assertPrerequisites (apply d) [ check d ] graph
    assertPrerequisites (apply e) [ check e ] graph

[<Fact>]
let ``Shared dependency is lowered once``(): unit =
    let d = resource "D" []
    let r1 = resource "R1" [ d ]
    let r2 = resource "R2" [ d ]

    let graph = lowerFakes CheckAndApply [ r1; r2 ]

    assertTasks [ check r1; check r2; check d; apply r1; apply r2; apply d ] graph
    assertPrerequisites (check d) [] graph
    assertPrerequisites (apply d) [ check d ] graph
    assertPrerequisites (check r1) [ apply d ] graph
    assertPrerequisites (check r2) [ apply d ] graph

[<Fact>]
let ``Root that is also a dependency of another root is lowered once``(): unit =
    let d = resource "D" []
    let r = resource "R" [ d ]

    let graph = lowerFakes CheckAndApply [ r; d ]

    assertTasks [ check r; check d; apply r; apply d ] graph
    assertPrerequisites (check r) [ apply d ] graph
    assertPrerequisites (apply d) [ check d ] graph

[<Fact>]
let ``Resources not reachable from roots are not lowered``(): unit =
    let d = resource "D" []
    let r = resource "R" [ d ]
    let _unrelated = resource "Unrelated" [ r ]

    let graph = lowerFakes CheckOnly [ r ]

    assertTasks [ check r; check d ] graph

[<Fact>]
let ``No roots produce an empty graph``(): unit =
    let graph = lowerFakes CheckAndApply []
    Assert.Empty graph

[<Fact>]
let ``Same resource passed twice is lowered once``(): unit =
    let r = resource "R" []
    let graph = lowerFakes CheckAndApply [ r; r ]
    assertTasks [ check r; apply r ] graph

[<Fact>]
let ``Resources with equal contents are lowered separately``(): unit =
    let alreadyApplied () = async.Return true
    let apply () = async.Return()
    let create() = {
        PresentableName = "R"
        DependsOn = Resource.NoDependencies
        Lock = None
        AlreadyApplied = alreadyApplied
        Apply = apply
    }

    let graph = lower CheckAndApply [ create(); create() ]

    Assert.Equal(4, graph.Count)

[<Fact>]
let ``Diamond dependency is lowered once per resource``(): unit =
    let d = resource "D" []
    let b = resource "B" [ d ]
    let c = resource "C" [ d ]
    let a = resource "A" [ b; c ]
    let graph = lowerFakes CheckAndApply [ a ]
    Assert.Equal(8, graph.Count)
    assertPrerequisites (check a) [ apply b; apply c ] graph
    assertPrerequisites (check b) [ apply d ] graph
    assertPrerequisites (check c) [ apply d ] graph

[<Fact>]
let ``Apply mode graph size is linear in the resource graph size``(): unit =
    let chainLength = 100
    let chain = Seq.fold (fun dependencies i -> [ resource $"R{i}" dependencies ]) [] [ 1 .. chainLength ]

    let graph = lowerFakes CheckAndApply chain

    Assert.Equal(2 * chainLength, graph.Count)
    // One prerequisite for each apply task, and one for each check task except the one with no dependencies.
    Assert.Equal(2 * chainLength - 1, graph.Values |> Seq.sumBy _.Count)
