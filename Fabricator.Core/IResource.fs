// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Core

type IResource =
    abstract member PresentableName: string
    abstract member AlreadyApplied: unit -> Async<bool>
    abstract member Apply: unit -> Async<unit>
