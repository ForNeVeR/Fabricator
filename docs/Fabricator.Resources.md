<!--
SPDX-FileCopyrightText: 2025 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

---
uid: Fabricator.Resources
summary: *content
---

Fabricator's main concept is [`IResource`][iresource]: this is an entity that can be checked for its presence (via `AlreadyApplied` check) or can be applied to the target environment (via `Apply`).

This assembly contains various resources Fabricator supports out of the box. The user can define new resources by implementing the [`IResource`][iresource] interface.

[iresource]: xref:Fabricator.Core.IResource
