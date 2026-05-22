// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.Commands.Chronicle.Workbench;

/// <summary>
/// Single source of truth for all workbench views.
/// <para>
/// Every view, its navigation sidebar entry, and its position index are defined here.
/// Nothing else in the codebase hardcodes view positions, nav labels, or icons.
/// </para>
/// <para>
/// To add a new view: create the view class, then add ONE <see cref="Entry{TView}"/> to the
/// master list below in the desired position. Navigation and all index constants update automatically.
/// </para>
/// <para>
/// <see cref="WorkbenchNavigation"/> asserts at startup when nav item count and view count drift apart.
/// </para>
/// </summary>
public static class WorkbenchViewRegistry
{
    // ── Section definitions ────────────────────────────────────────────────────
    // Static reference objects — WorkbenchNavigation uses ReferenceEquals to detect
    // section boundaries when building the nav tree.

    /// <summary>Overview section header.</summary>
    public static readonly WorkbenchSection SectionOverview = new("OVERVIEW", WorkbenchColors.Accent);

    /// <summary>Observation (observers, failures, jobs, recommendations) section header.</summary>
    public static readonly WorkbenchSection SectionObservation = new("OBSERVATION", WorkbenchColors.Warning);

    /// <summary>Events (sequences, types) section header.</summary>
    public static readonly WorkbenchSection SectionEvents = new("EVENTS", WorkbenchColors.Teal);

    /// <summary>Projections (projections, read models) section header.</summary>
    public static readonly WorkbenchSection SectionProjections = new("PROJECTIONS", WorkbenchColors.Mauve);

    /// <summary>Server (event stores, namespaces, applications, users, identities, subscriptions) section header.</summary>
    public static readonly WorkbenchSection SectionServer = new("SERVER", WorkbenchColors.Muted);

    static readonly WorkbenchViewDefinition[] _all =
    [
        Entry<OverviewView>(static () => new OverviewView(), "Overview", "◈", "Health & status", SectionOverview),
        Entry<ObserversView>(static () => new ObserversView(), "Observers", "◉", "Monitor & replay", SectionObservation),
        Entry<FailedPartitionsView>(static () => new FailedPartitionsView(), "Failures", "✕", "Retry failed partitions", SectionObservation),
        Entry<JobsView>(static () => new JobsView(), "Jobs", "≋", "Background operations", SectionObservation),
        Entry<RecommendationsView>(static () => new RecommendationsView(), "Recommendations", "★", "Suggested actions", SectionObservation),
        Entry<EventSequencesView>(static () => new EventSequencesView(), "Event Sequences", "≡", "Appended events", SectionEvents),
        Entry<EventTypesView>(static () => new EventTypesView(), "Event Types", "◇", "Registered schemas", SectionEvents),
        Entry<ProjectionsView>(static () => new ProjectionsView(), "Projections", "▷", "Running projections", SectionProjections),
        Entry<ReadModelsView>(static () => new ReadModelsView(), "Read Models", "▦", "Derived state", SectionProjections),
        Entry<EventStoresView>(static () => new EventStoresView(), "Event Stores", "⊞", "Store management", SectionServer),
        Entry<NamespacesView>(static () => new NamespacesView(), "Namespaces", "⊙", "Namespace context", SectionServer),
        Entry<ApplicationsView>(static () => new ApplicationsView(), "Applications", "⊕", "App registrations", SectionServer),
        Entry<UsersView>(static () => new UsersView(), "Users", "♟", "User management", SectionServer),
        Entry<IdentitiesView>(static () => new IdentitiesView(), "Identities", "◎", "Identity records", SectionServer),
        Entry<SubscriptionsView>(static () => new SubscriptionsView(), "Subscriptions", "⊗", "Active subscriptions", SectionServer),
    ];

    /// <summary>The ordered list of all registered view definitions. Position = view index.</summary>
    public static IReadOnlyList<WorkbenchViewDefinition> All => _all;

    /// <summary>
    /// Returns the zero-based view index for <typeparamref name="TView"/>.
    /// Throws if the type is not registered — this is a programming error.
    /// </summary>
    /// <typeparam name="TView">The view type to look up.</typeparam>
    /// <returns>The view index matching the corresponding <c>IndexXxx</c> constant.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="TView"/> is not in the registry.</exception>
    public static int IndexOf<TView>()
        where TView : IWorkbenchView
    {
        var target = typeof(TView);
        for (var i = 0; i < _all.Length; i++)
        {
            if (_all[i].ViewType == target)
            {
                return i;
            }
        }

        throw new InvalidOperationException(
            $"View type {typeof(TView).Name} is not registered in {nameof(WorkbenchViewRegistry)}. " +
            $"Add an Entry<{typeof(TView).Name}>(...) to the master list.");
    }

    /// <summary>
    /// Creates one fresh instance of every registered view, in registry order.
    /// The returned array is the shared <c>_views[]</c> for <c>MainWindow</c> and <c>WorkbenchNavigation</c>.
    /// </summary>
    /// <returns>New view instances in registry order.</returns>
    public static IWorkbenchView[] CreateViews() =>
        [.. _all.Select(d => d.Factory())];

    static WorkbenchViewDefinition Entry<TView>(
        Func<IWorkbenchView> factory,
        string navText,
        string navIcon,
        string navSubtitle,
        WorkbenchSection section)
        where TView : IWorkbenchView =>
        new(typeof(TView), factory, navText, navIcon, navSubtitle, section);
}
