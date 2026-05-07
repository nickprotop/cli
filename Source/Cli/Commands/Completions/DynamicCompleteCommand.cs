// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cratis.Chronicle.Contracts.Jobs;
using Cratis.Chronicle.Contracts.Observation.EventStoreSubscriptions;
using Cratis.Cli.Commands.Chronicle;

namespace Cratis.Cli.Commands.Completions;

/// <summary>
/// Hidden internal command that outputs resource identifiers for dynamic shell completion.
/// Not shown in help or listed commands.
/// Returns empty output on any error to avoid breaking shell completion.
/// </summary>
[CliCommand("_complete", "(internal) dynamic completion helper", IsHidden = true, ExcludeFromLlm = true)]
public class DynamicCompleteCommand : ChronicleCommand<DynamicCompleteSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteCommandAsync(IServices services, DynamicCompleteSettings settings, string format)
    {
        try
        {
            var eventStore = settings.ResolveEventStore();
            var ns = settings.ResolveNamespace();

            switch (settings.Context.ToLowerInvariant())
            {
                case "observers":
                    var observers = await services.Observers.GetObservers(new AllObserversRequest
                    {
                        EventStore = eventStore,
                        Namespace = ns
                    });
                    foreach (var obs in observers ?? [])
                    {
                        Console.WriteLine(obs.Id);
                    }

                    break;

                case "jobs":
                    var jobs = await services.Jobs.GetJobs(new GetJobsRequest
                    {
                        EventStore = eventStore,
                        Namespace = ns
                    });
                    foreach (var job in jobs ?? [])
                    {
                        Console.WriteLine(job.Id.ToString());
                    }

                    break;

                case "read-models":
                    var response = await services.ReadModels.GetDefinitions(new GetDefinitionsRequest
                    {
                        EventStore = eventStore
                    });
                    foreach (var rm in response.ReadModels ?? [])
                    {
                        Console.WriteLine(rm.Type?.Identifier ?? rm.DisplayName ?? string.Empty);
                    }

                    break;

                case "output-formats":
                    // Static list — no server call needed.
                    Console.WriteLine(OutputFormats.Table);
                    Console.WriteLine(OutputFormats.Plain);
                    Console.WriteLine(OutputFormats.Json);
                    Console.WriteLine(OutputFormats.JsonCompact);
                    Console.WriteLine(OutputFormats.Auto);
                    break;

                case "event-types":
                    // When no specific event store is configured (resolved to the default),
                    // query all available stores and aggregate types for maximum completion coverage.
                    IEnumerable<string> storeNames;
                    if (eventStore == CliDefaults.DefaultEventStoreName)
                    {
                        var allStores = await services.EventStores.GetEventStores();
                        storeNames = allStores ?? [];
                    }
                    else
                    {
                        storeNames = [eventStore];
                    }

                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var store in storeNames)
                    {
                        var types = await services.EventTypes.GetAll(new GetAllEventTypesRequest
                        {
                            EventStore = store
                        });
                        foreach (var et in types ?? [])
                        {
                            if (seen.Add(et.Id))
                            {
                                Console.WriteLine(et.Id);
                            }
                        }
                    }

                    break;

                case "event-stores":
                    var stores = await services.EventStores.GetEventStores();
                    foreach (var store in stores ?? [])
                    {
                        Console.WriteLine(store);
                    }

                    break;

                case "projections":
                    var declarations = await services.Projections.GetAllDeclarations(new GetAllDeclarationsRequest
                    {
                        EventStore = eventStore
                    });
                    foreach (var decl in declarations ?? [])
                    {
                        Console.WriteLine(decl.Identifier);
                    }

                    break;

                case "recommendations":
                    var recs = await services.Recommendations.GetRecommendations(new GetRecommendationsRequest
                    {
                        EventStore = eventStore,
                        Namespace = ns
                    });
                    foreach (var rec in recs ?? [])
                    {
                        Console.WriteLine(rec.Id);
                    }

                    break;

                case "users":
                    var users = await services.Users.GetAll();
                    foreach (var user in users ?? [])
                    {
                        Console.WriteLine(user.Id);
                    }

                    break;

                case "applications":
                    var apps = await services.Applications.GetAll();
                    foreach (var app in apps ?? [])
                    {
                        Console.WriteLine(app.Id);
                    }

                    break;

                case "event-store-subscriptions":
                    var subscriptions = await services.EventStoreSubscriptions.GetSubscriptions(new GetEventStoreSubscriptionsRequest
                    {
                        TargetEventStore = eventStore
                    });
                    foreach (var subscription in subscriptions ?? [])
                    {
                        Console.WriteLine(subscription.Identifier);
                    }

                    break;

                case "contexts":
                    var config = CliConfiguration.Load();
                    foreach (var name in config.Contexts.Keys)
                    {
                        Console.WriteLine(name);
                    }

                    break;
            }
        }
        catch
        {
            // Silently ignore all errors — shell completion must not break on server failure.
        }

        return ExitCodes.Success;
    }
}
