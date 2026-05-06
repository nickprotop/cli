// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Nodes;

namespace Cratis.Cli.Commands.Chronicle.ReadModels;

/// <summary>
/// Cleans read model JSON documents by removing internal Chronicle fields and renaming internal ones.
/// </summary>
static class ReadModelJsonCleaner
{
    /// <summary>
    /// Parses a raw read model JSON string into a <see cref="JsonObject"/>, removes the
    /// <c>__initialized</c> field, and renames <c>__lastHandledEventSequenceNumber</c> to
    /// <c>LastHandledEvent</c>.
    /// </summary>
    /// <param name="json">The raw JSON string from the Chronicle server.</param>
    /// <returns>A cleaned <see cref="JsonObject"/>, or <see langword="null"/> if parsing fails.</returns>
    internal static JsonObject? CleanInstance(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject source)
                return null;

            var result = new JsonObject();
            foreach (var property in source)
            {
                if (property.Key == "__initialized")
                    continue;

                if (property.Key == "__lastHandledEventSequenceNumber")
                {
                    result.Add("LastHandledEvent", UnwrapConceptValue(property.Value));
                    continue;
                }

                result.Add(property.Key, property.Value?.DeepClone());
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Unwraps a <see cref="JsonNode"/> that may be a <c>ConceptAs&lt;T&gt;</c> wrapper object
    /// serialized without converters (e.g. <c>{"Value":42}</c> or empty <c>{}</c>).
    /// Returns the inner <c>Value</c> node when present, <see langword="null"/> when the object
    /// is empty (unset), or the node itself when it is already a primitive.
    /// </summary>
    /// <param name="node">The JSON node to unwrap.</param>
    static JsonNode? UnwrapConceptValue(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            // ConceptAs<T> serializes as {"Value": <primitive>} — unwrap it.
            if (obj.TryGetPropertyValue("Value", out var inner))
                return inner?.DeepClone();

            // Empty object {} means the concept was never set.
            return null;
        }

        return node?.DeepClone();
    }
}
