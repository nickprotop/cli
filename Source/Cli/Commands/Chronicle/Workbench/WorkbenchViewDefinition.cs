// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Bundles everything that defines a single workbench navigation item in one place:
/// the view type, factory, nav item text/icon/subtitle, and the section it belongs to.
/// The position of this entry in <see cref="WorkbenchViewRegistry.All"/> is its view index.
/// </summary>
/// <param name="ViewType">The concrete view type — used for O(n) index lookup by type without creating instances.</param>
/// <param name="Factory">Creates a fresh instance of the view. Called once during startup.</param>
/// <param name="NavText">The nav item label shown in the sidebar.</param>
/// <param name="NavIcon">The single-character icon shown in compact sidebar mode.</param>
/// <param name="NavSubtitle">The short subtitle shown below the label in expanded mode.</param>
/// <param name="Section">The section header this item lives under. Use the <c>static readonly</c>
/// instances from <see cref="WorkbenchViewRegistry"/> — reference equality is used to detect section boundaries.</param>
public sealed record WorkbenchViewDefinition(
    Type ViewType,
    Func<IWorkbenchView> Factory,
    string NavText,
    string NavIcon,
    string NavSubtitle,
    WorkbenchSection Section);
