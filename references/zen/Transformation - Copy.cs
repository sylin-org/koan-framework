using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geis.Domain.Person.Model;
using Geis.UKB.Service.Model.Transformation;
using Newtonsoft.Json.Linq;
using Zen.Base.Module.Data;
using Geis.UKB.Model;
using Zen.Base.Module.Log;
using Microsoft.AspNetCore.Mvc;
using Geis.UKB.Service.Model;
using Zen.Base.Extension;

namespace Geis.UKB.Service.Module
{
    public static class Transformation
    {

        private class ProcessedHealingEntry
        {
            public string Parser { get; set; }
            public string Value { get; set; } // The target heal value
            public HashSet<string> CommonTermsSet { get; set; }
        }

        // Tunables are loaded from DB-backed configuration with optional env overrides
        static int BatchSize => EtlConfig.Current().BatchSize;
        static int MaxDop => EtlConfig.Current().MaxDegreeOfParallelism;

        // Semaphore to ensure that only one thread can access the Digest method at a time
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        // Method to process and aggregate normalized data
        public static async Task Digest()
        {
            await _semaphore.WaitAsync(); // Ensure single execution

            // Local variables for data refreshed on each call
            List<NormalizedDataElement> aggregationElements;

            var healingEntries = HealingMap.All().ToList();

            Dictionary<string, ProcessedHealingEntry> healingMapLookup;

            try
            {
                // --- Fetch Fresh Data for This Run ---
                Zen.Base.Log.KeyValuePair("Transformation:Digest", "Refreshing lookup data.");

                // 1. Fetch aggregation elements
                aggregationElements = NormalizedDataElement
                    .Where(i => i.IsAggregationIdentifier)
                    .OrderBy(i => i.AggregationPriority)
                    .Reverse()
                    .ToList();

                // 3. Build Optimized Healing Lookup (runs ONCE per Digest call)
                healingMapLookup = healingEntries
                   .GroupBy(he => he.NamespaceKey) // Handle potential duplicate NamespaceKeys if necessary
                   .ToDictionary(
                        group => group.Key, // Key: NamespaceKey
                        group =>
                        {
                            var firstEntry = group.First(); // Prioritize first if duplicates exist
                            return new ProcessedHealingEntry
                            {
                                Parser = firstEntry.Parser,
                                Value = firstEntry.Value,
                                // Convert CommonTerms List to HashSet for O(1) lookup within the parallel loop
                                CommonTermsSet = firstEntry.CommonTerms?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                                                 ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            };
                        },
                        StringComparer.Ordinal // Comparer for NamespaceKey lookup. Assume case-sensitive.
                    );

                Zen.Base.Log.KeyValuePair("Transformation:Digest", $"Data Refresh Complete: {aggregationElements.Count} Aggregation Elements, {healingEntries.Count} Healing Entries fetched ({healingMapLookup.Count} mapped).");

                // --- Process Data in Batches ---
                do
                {
                    // Fetch next batch
                    var queuedDataPayloads = NormalizedData.Query(new Mutator { Transform = new QueryTransform { Pagination = new Pagination { Index = 0, Size = BatchSize } } }).ToList();

                    if (queuedDataPayloads.Count == 0)
                    {
                        Zen.Base.Log.KeyValuePair("Transformation:Digest", "Queue empty, final aggregation triggered.");
                        await Aggregate();
                        return; // Exit loop and method
                    }

                    Zen.Base.Log.KeyValuePair("Transformation:Digest", $"START Batch Processing: {queuedDataPayloads.Count} items", Message.EContentType.Maintenance);
                    var tagClicker = new TagClicker("Transformation:Digest:");
                    var click = new Clicker("Items", queuedDataPayloads.Count);

                    // Process the batch in parallel using the lookups created for *this* Digest call
                    Parallel.ForEach(queuedDataPayloads, new ParallelOptions { MaxDegreeOfParallelism = MaxDop }, dataPayload =>
                    {
                        var taggedModel = new TaggedNormalizedDataElement
                        {
                            Id = dataPayload.Id,
                            DataPayload = dataPayload,
                            AggregationTags = new List<string>(),
                            Changes = new List<TaggedNormalizedDataElement.Change>()
                        };

                        // --- Healing Step (Using Per-Call Optimized Lookups) ---
                        var keysToProcess = taggedModel.DataPayload.Data.Keys.ToList();
                        foreach (var entryKey in keysToProcess)
                        {
                            var originalValue = taggedModel.DataPayload.Data[entryKey];
                            string candidateValue = null;

                            // Use the local healingMapLookup (O(1) average)
                            if (healingMapLookup.TryGetValue(entryKey, out var healingInfo))
                            {
                                if (!healingInfo.Parser.IsNullOrEmpty())
                                {
                                    candidateValue = Domain.Person.Constants.WellKnown.Parser.Parse(healingInfo.Parser, originalValue);
                                }

                                if (candidateValue.IsNullOrEmpty())
                                {
                                    // Use the HashSet within healingInfo (O(1) average)
                                    if (healingInfo.CommonTermsSet.Contains(originalValue))
                                    {
                                        candidateValue = healingInfo.Value;
                                    }
                                }
                            }

                            if (!candidateValue.IsNullOrEmpty() && candidateValue != originalValue)
                            {
                                taggedModel.DataPayload.Data[entryKey] = candidateValue;
                                taggedModel.Changes.Add(new TaggedNormalizedDataElement.Change { NamespaceKey = entryKey, OriginalValue = originalValue, FinalValue = candidateValue });
                            }
                        } // End healing loop

                        // --- Aggregation Tagging Step (Using Per-Call List) ---
                        // Use the local aggregationElements list fetched for this run
                        foreach (var aggregationElement in aggregationElements)
                        {
                            if (taggedModel.DataPayload.Data.TryGetValue(aggregationElement.Id, out var aggValue))
                            {
                                taggedModel.AggregationTags.Add($"{aggregationElement.Id}:{aggValue}");
                            }
                        } // End aggregation loop

                        // --- Save/Update Status ---
                        if (taggedModel.AggregationTags.Any())
                        {
                            taggedModel.Save();
                            dataPayload.Save(new Mutator { SetCode = "Processed" });
                            tagClicker.Click("Processed");
                        }
                        else
                        {
                            dataPayload.Save(new Mutator { SetCode = "Not-processable" });
                            tagClicker.Click("Not Processable");
                        }
                        dataPayload.Remove();

                        click.Click();
                    }); // End Parallel.ForEach

                    click.End();
                    tagClicker.ToLog();
                    Zen.Base.Log.KeyValuePair("Transformation:Digest", $"END Batch: {queuedDataPayloads.Count} entries processed.", Message.EContentType.Maintenance);

                    await Aggregate(); // Aggregate after each batch

                } while (true); // Continue processing batches
            }
            catch (Exception e)
            {
                Zen.Base.Log.Add("Error during Transformation:Digest processing loop.", e);
                throw; // Re-throw after logging
            }
            finally
            {
                _semaphore.Release(); // Release semaphore reliably
            }
        }

        // Method to aggregate tagged normalized data elements
        public static async Task Aggregate()
        {
            var _topic = "Transformation:Aggregate";

            Zen.Base.Log.KeyValuePair(_topic, "START", Message.EContentType.Maintenance);
            Zen.Base.Log.KeyValuePair(_topic, "Fetch all tagged data elements and aggregate by their keys");

            var tagClicker = new TagClicker(_topic + ":");

            var totalProcessed = 0;
            var pageIndex = 0;
            var pageSize = BatchSize;
            while (true)
            {
                // Page through TaggedNormalizedDataElement to avoid full scans
                var page = TaggedNormalizedDataElement.Query(new Mutator { Transform = new QueryTransform { Pagination = new Pagination { Index = pageIndex, Size = pageSize } } }).ToList();
                if (page.Count == 0) break;

                var click = new Clicker("Items", page.Count);

                // Process each tagged normalized data element in parallel
                Parallel.ForEach(page, new ParallelOptions { MaxDegreeOfParallelism = MaxDop }, taggedNormalizedDataElement =>
                {
                    AggregatedUser targetAggregatedUserModel = null;
                    var invalidEntry = false;

                    // For each present Aggregation tag, try to locate an aggregated user that has it
                    foreach (var aggregationTag in taggedNormalizedDataElement.AggregationTags)
                    {
                        var targetValue = aggregationTag;
                        var target = AggregatedUser.Where(i => i.AggregationTags.Contains(targetValue)).ToList();

                        if (target.Any())
                        {
                            if (target.Count > 1)
                            {
                                Zen.Base.Log.KeyValuePair(_topic, targetValue + " is not unique (" + target.Count + " entries found).", Message.EContentType.Warning);
                                invalidEntry = true;
                                break;
                            }

                            // Assign the target model for changes
                            targetAggregatedUserModel = target.First();
                            break;
                        }
                    }

                    if (invalidEntry)
                    {
                        taggedNormalizedDataElement.Status = "Duplicated matches";
                        taggedNormalizedDataElement.Save(new Mutator { SetCode = "Invalid" });
                        taggedNormalizedDataElement.Remove();
                        return;
                    }

                    // If none is found, create a new aggregated user
                    if (targetAggregatedUserModel == null)
                    {
                        tagClicker.Click("New");

                        targetAggregatedUserModel = new AggregatedUser
                        {
                            AggregationTags = taggedNormalizedDataElement.AggregationTags,
                            SourceNormalizedDataElementId = taggedNormalizedDataElement.Id,
                            Timestamps = new AggregatedUser.TimeStamp()
                        };
                    }
                    // Process the entries present on the original Tagged Normalized Data Element
                    targetAggregatedUserModel.Entries ??= new List<AggregatedUser.AggregateEntry>();

                    // Remove all entries from the data source and add the ones contained in the data payload




                    targetAggregatedUserModel.Entries = targetAggregatedUserModel.Entries
                        .Where(i => i.Source != taggedNormalizedDataElement.DataPayload.Source)
                        .ToList();

                    foreach (var entry in taggedNormalizedDataElement.DataPayload.Data)
                    {
                        targetAggregatedUserModel.Entries.Add(new AggregatedUser.AggregateEntry
                        {
                            DataElement = entry.Key,
                            Source = taggedNormalizedDataElement.DataPayload.Source,
                            Value = entry.Value
                        });
                    }

                    targetAggregatedUserModel.Timestamps.Modified = DateTime.Now;

                    // Mark this entry for synthesis
                    targetAggregatedUserModel.Synthetized = false;
                    targetAggregatedUserModel.Save();

                    // Move to the Processed stack
                    taggedNormalizedDataElement.Save(new Mutator { SetCode = "Processed" });
                    taggedNormalizedDataElement.Remove();

                    click.Click();
                });

                click.End();
                totalProcessed += page.Count;
                pageIndex++;
            }
            tagClicker.ToLog();
            Zen.Base.Log.KeyValuePair(_topic, $"END ({totalProcessed} entries)", Message.EContentType.Maintenance);

            await Synthetize();

        }

        // Method to synthesize aggregated user data
        public static Task Synthetize()
        {
            var _topic = "Transformation:Synthesis";

            Zen.Base.Log.KeyValuePair(_topic, "START", Message.EContentType.Maintenance);

            Zen.Base.Log.KeyValuePair(_topic, "Fetch all Aggregated Users waiting for synthesis");

            var pendingAggregatedUsers = AggregatedUser.Where(i => i.Synthetized == false).ToList();

            var click = new Clicker("Synthesis: Processed users", pendingAggregatedUsers.Count);

            // Process each pending aggregated user in parallel
            Parallel.ForEach(pendingAggregatedUsers, new ParallelOptions { MaxDegreeOfParallelism = MaxDop }, pendingAggregatedUser =>
            {
                var personSourceDataElement = new Dictionary<string, Dictionary<string, List<string>>>();
                var personDataElement = new Dictionary<string, List<string>>();

                foreach (var aggregateEntry in pendingAggregatedUser.Entries)
                {
                    if (!personSourceDataElement.ContainsKey(aggregateEntry.DataElement))
                        personSourceDataElement[aggregateEntry.DataElement] = new Dictionary<string, List<string>>();

                    if (!personSourceDataElement[aggregateEntry.DataElement].ContainsKey(aggregateEntry.Value))
                        personSourceDataElement[aggregateEntry.DataElement][aggregateEntry.Value] = new List<string>();

                    if (!personSourceDataElement[aggregateEntry.DataElement][aggregateEntry.Value].Contains(aggregateEntry.Source))
                        personSourceDataElement[aggregateEntry.DataElement][aggregateEntry.Value].Add(aggregateEntry.Source);


                    if (!personDataElement.ContainsKey(aggregateEntry.DataElement))
                        personDataElement[aggregateEntry.DataElement] = new List<string>();

                    if (!personDataElement[aggregateEntry.DataElement].Contains(aggregateEntry.Value))
                        personDataElement[aggregateEntry.DataElement].Add(aggregateEntry.Value);
                }

                var personSourceAggregate = personSourceDataElement.ToDictionary(i => i.Key, i => (object)i.Value);
                var personSourceData = CreateJObjectFromDictionary(personSourceAggregate);

                new PersonSourceMap
                {
                    Id = pendingAggregatedUser.Id,
                    data = personSourceData
                }.Save();

                var personAggregate = personDataElement.ToDictionary(i => i.Key, i => (object)i.Value);
                var personData = CreateJObjectFromDictionary(personAggregate);

                new Person
                {
                    Id = pendingAggregatedUser.Id,
                    data = personData
                }.Save();

                pendingAggregatedUser.Synthetized = true;
                pendingAggregatedUser.Timestamps.Modified = DateTime.Now;
                pendingAggregatedUser.Save();

                click.Click();
            });

            click.End();
            Zen.Base.Log.KeyValuePair(_topic, $"END: {pendingAggregatedUsers.Count} entries processed.", Message.EContentType.Maintenance);
            return Task.CompletedTask;
        }

    /// <summary>
    /// Reconstructs a JSON object from a dictionary of JSON-path-like keys to values.
    /// </summary>
    /// <param name="jsonPathValues">
    /// Dictionary where each key is a dotted path and each value is the leaf value to assign.
    /// - Use "." to separate path segments.
    /// - Use "\\." to escape literal dots in segment names.
    /// - Numeric segments (e.g., "addresses.0.city") are treated as array indices.
    /// </param>
    /// <remarks>
    /// Behavior and conflict policy:
    /// - Keys are applied in order of increasing depth so parents are created before children.
    /// - When the next segment is numeric, an array container is created (or converted) as needed; otherwise, an object.
    /// - If an intermediate token is a scalar or an array but an object is required, it is replaced with an object that preserves the original under "_value".
    /// - Empty arrays at intermediate positions are converted to empty objects when an object is needed.
    /// - Leaf string values that look like JSON arrays (e.g., "['a','b']" or "[1,2]") are parsed into JArray when parseable.
    /// - For duplicate keys, last write wins (later entries overwrite earlier ones at the same leaf).
    /// </remarks>
    /// <returns>
    /// A <see cref="JObject"/> with the reconstructed structure. Returns an empty object when the input is null or empty.
    /// </returns>
        public static JObject CreateJObjectFromDictionary(Dictionary<string, object> jsonPathValues)
        {
            var root = new JObject();

            if (jsonPathValues == null || jsonPathValues.Count == 0) return root;

            // Order by path depth so parent containers are created before children (handles unordered keys more ergonomically)
            var ordered = jsonPathValues
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                .Select(kvp => new { kvp.Key, kvp.Value, Parts = SplitJsonPath(kvp.Key) })
                .OrderBy(i => i.Parts.Length)
                .ToList();

            foreach (var item in ordered)
            {
                var parts = item.Parts;
                if (parts.Length == 0) continue;

                JToken current = root;
                JObject parentObj = null;
                string parentProp = null;

                for (int i = 0; i < parts.Length; i++)
                {
            var part = parts[i];
                    var isLeaf = i == parts.Length - 1;
                    var nextIsIndex = !isLeaf && int.TryParse(parts[i + 1], out _);

                    if (current is JObject obj)
                    {
                        if (isLeaf)
                        {
                            // Leaf assignment under object: parse JSON-like array strings when appropriate
                            obj[part] = ToLeafToken(item.Value);
                            break;
                        }

                        var next = obj[part];

                        if (next == null || next.Type == JTokenType.Null)
                        {
                            // Create container based on whether next segment is an array index
                            var child = nextIsIndex ? (JToken)new JArray() : new JObject();
                            obj[part] = child;
                            parentObj = obj;
                            parentProp = part;
                            current = child;
                        }
                        else if (next is JObject)
                        {
                            if (nextIsIndex)
                            {
                                // Conflict: need an array next but found object. Convert object to array, keep object at index 0.
                                var arr = new JArray();
                                if (next.HasValues) arr.Add(next.DeepClone());
                                obj[part] = arr;
                                parentObj = obj;
                                parentProp = part;
                                current = arr;
                            }
                            else
                            {
                                parentObj = obj;
                                parentProp = part;
                                current = next;
                            }
                        }
                        else
                        {
                            if (next is JArray nextArr)
                            {
                                if (!nextIsIndex)
                                {
                                    // Expecting object but found array: convert empty to object, otherwise wrap under _value
                                    var replacement = nextArr.Count == 0 ? new JObject() : new JObject { ["_value"] = nextArr.DeepClone() };
                                    obj[part] = replacement;
                                    parentObj = obj;
                                    parentProp = part;
                                    current = replacement;
                                }
                                else
                                {
                                    // Already an array and next is index, continue
                                    parentObj = obj;
                                    parentProp = part;
                                    current = nextArr;
                                }
                            }
                            else
                            {
                                // Conflict: value at intermediate path. If next is index, convert to array with value at [0], else wrap in object
                                if (nextIsIndex)
                                {
                                    var arr = new JArray();
                                    arr.Add(next.DeepClone());
                                    obj[part] = arr;
                                    parentObj = obj;
                                    parentProp = part;
                                    current = arr;
                                }
                                else
                                {
                                    var replacement = new JObject { ["_value"] = next.DeepClone() };
                                    obj[part] = replacement;
                                    parentObj = obj;
                                    parentProp = part;
                                    current = replacement;
                                }
                            }
                        }
                    }
                    else if (current is JArray arr)
                    {
                        // In arrays, we expect numeric indices.
                        if (int.TryParse(part, out var idx))
                        {
                            // Ensure capacity
                            while (arr.Count <= idx) arr.Add(JValue.CreateNull());

                            if (isLeaf)
                            {
                                arr[idx] = ToLeafToken(item.Value);
                                break;
                            }
                            else
                            {
                                var child = arr[idx];
                                var childNextIsIndex = int.TryParse(parts[i + 1], out _);

                                if (child == null || child.Type == JTokenType.Null)
                                {
                                    arr[idx] = childNextIsIndex ? (JToken)new JArray() : new JObject();
                                    current = arr[idx];
                                }
                                else if (child is JObject childObj)
                                {
                                    if (childNextIsIndex)
                                    {
                                        // Convert object to array with object at [0]
                                        var childArr = new JArray { childObj.DeepClone() };
                                        arr[idx] = childArr;
                                        current = childArr;
                                    }
                                    else
                                    {
                                        current = childObj;
                                    }
                                }
                                else if (child is JArray childArr)
                                {
                                    current = childArr;
                                }
                                else
                                {
                                    // JValue at intermediate: convert based on next segment
                                    if (childNextIsIndex)
                                    {
                                        var childArr2 = new JArray { child.DeepClone() };
                                        arr[idx] = childArr2;
                                        current = childArr2;
                                    }
                                    else
                                    {
                                        arr[idx] = new JObject { ["_value"] = child.DeepClone() };
                                        current = arr[idx];
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Non-numeric segment under an array: convert the array to object and retry this segment
                            if (parentObj != null && parentProp != null)
                            {
                                var replacement = arr.Count == 0 ? new JObject() : new JObject { ["_value"] = arr.DeepClone() };
                                parentObj[parentProp] = replacement;
                                current = replacement;
                                i--; // retry same part with an object
                            }
                            else
                            {
                                var replacement = arr.Count == 0 ? new JObject() : new JObject { ["_value"] = arr.DeepClone() };
                                root = replacement;
                                current = replacement;
                                i--;
                            }
                        }
                    }
                    else // JValue or other token types
                    {
                        if (parentObj != null && parentProp != null)
                        {
                            var replacement = new JObject { ["_value"] = current.DeepClone() };
                            parentObj[parentProp] = replacement;
                            current = replacement;
                            i--;
                        }
                        else
                        {
                            // Replace root with an object and retry
                            root = new JObject();
                            current = root;
                            i--;
                        }
                    }
                }
            }

            return root;
        }

        // Split a JSON path by '.' while supporting escaped dots (e.g., "contact.email\.work")
        private static string[] SplitJsonPath(string path)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(path)) return parts.ToArray();

            var current = new System.Text.StringBuilder();
            bool escape = false;
            foreach (var ch in path)
            {
                if (escape)
                {
                    current.Append(ch);
                    escape = false;
                    continue;
                }

                if (ch == '\\') { escape = true; continue; }
                if (ch == '.') { parts.Add(current.ToString()); current.Clear(); continue; }
                current.Append(ch);
            }
            parts.Add(current.ToString());

            return parts.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        }

        // Convert leaf values to tokens, parsing JSON-like array strings when applicable.
        // Examples:
        //   "['one','two']" -> JArray(["one","two"]) if parseable; otherwise remain as string.
        private static JToken ToLeafToken(object value)
        {
            if (value is string s)
            {
                var t = s.Trim();
                if (t.Length >= 2 && t[0] == '[' && t[^1] == ']')
                {
                    try { return JArray.Parse(t); } catch { /* fall through to string */ }
                }
                return JValue.FromObject(s);
            }
            return value != null ? JToken.FromObject(value) : JValue.CreateNull();
        }

        // Method to heal non-processable data
        public static ActionResult Heal()
        {
            const int batchSize = 1000;
            int currentIndex = 0;
            int totalHealedRecords = 0;
            var _topic = "Transformation:Heal";

            Zen.Base.Log.KeyValuePair(_topic, "START", Message.EContentType.Maintenance);

            while (true)
            {
                Zen.Base.Log.KeyValuePair(_topic, $"Fetching batch starting at index {currentIndex}", Message.EContentType.Maintenance);

                var notProcessableCollection = new Mutator
                {
                    SetCode = "Not-processable",
                    Transform = new QueryTransform
                    {
                        Pagination = new Pagination
                        {
                            Index = currentIndex,
                            Size = batchSize
                        }
                    }
                };

                var batch = NormalizedData.Query(notProcessableCollection).ToList();

                Zen.Base.Log.KeyValuePair(_topic, $"Fetched {batch.Count} records", Message.EContentType.Maintenance);

                if (batch.Count == 0)
                {
                    Zen.Base.Log.KeyValuePair(_topic, "No more records to process, exiting loop", Message.EContentType.Maintenance);
                    break;
                }

                Zen.Base.Log.KeyValuePair(_topic, "Saving batch", Message.EContentType.Maintenance);
                batch.Save();

                Zen.Base.Log.KeyValuePair(_topic, "Removing records from not-processable collection", Message.EContentType.Maintenance);
                foreach (var dataPayload in batch) dataPayload.Remove(notProcessableCollection);

                totalHealedRecords += batch.Count;

                Zen.Base.Log.KeyValuePair(_topic, $"Batch starting at index {currentIndex} processed successfully", Message.EContentType.Maintenance);
                currentIndex++;
            }

            Zen.Base.Log.KeyValuePair(_topic, "END", Message.EContentType.Maintenance);

            Task.Run(() => { _ = Digest(); });

            var response = new Dictionary<string, string>
            {
                ["recordsHealed"] = totalHealedRecords.ToString()
            };

            return new OkObjectResult(new ServiceActionResponse { Success = true, Message = $"{totalHealedRecords} healed records queued for reprocessing.", Metadata = response });
        }
    }
}