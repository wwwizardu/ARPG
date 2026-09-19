#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ARPG.Data
{
    /// <summary>
    /// Copies the persisted shape, not runtime objects. Leaves are immutable scalars; all
    /// objects, arrays, dictionaries and sets become exclusively owned DTO containers.
    /// Contract getters (including EquipmentData.WeaponStats) run only during capture.
    /// No JSON, JToken, table reference or Unity object is handed to the writer.
    /// </summary>
    public sealed class SaveDtoSnapshot
    {
        private readonly DefaultContractResolver _contracts = new();

        public object? Capture(object? source)
        {
            return Copy(source, new HashSet<object>(ReferenceIdentity.Instance), 0);
        }

        private object? Copy(object? source, HashSet<object> ancestors, int depth)
        {
            if (source == null) return null;
            Type type = source.GetType();
            if (type.IsEnum == true || type.IsPrimitive == true || source is string || source is decimal)
                return source;
            if (depth > 128 || ancestors.Add(source) == false)
                throw new InvalidOperationException("Cyclic or excessively deep save data: " + type.FullName);

            try
            {
                if (source is IDictionary dictionary)
                {
                    var result = new Dictionary<string, object?>(dictionary.Count, StringComparer.Ordinal);
                    IDictionaryEnumerator entries = dictionary.GetEnumerator();
                    try
                    {
                        for (; entries.MoveNext();)
                        {
                            object key = entries.Key;
                            Type keyType = key.GetType();
                            if (keyType.IsEnum == false && keyType.IsPrimitive == false && key is string == false)
                                throw new InvalidOperationException("Unsupported save dictionary key: " + keyType.FullName);
                            result.Add(Convert.ToString(key, CultureInfo.InvariantCulture)!, Copy(entries.Value, ancestors, depth + 1));
                        }
                    }
                    finally { (entries as IDisposable)?.Dispose(); }
                    return result;
                }

                if (source is IEnumerable sequence)
                {
                    var result = new List<object?>(source is ICollection collection ? collection.Count : 0);
                    IEnumerator entries = sequence.GetEnumerator();
                    try
                    {
                        for (; entries.MoveNext();) result.Add(Copy(entries.Current, ancestors, depth + 1));
                    }
                    finally { (entries as IDisposable)?.Dispose(); }
                    return result;
                }

                // Fail closed on new runtime/reference types instead of accidentally sharing them.
                if (type.Namespace == null || type.Namespace.StartsWith("ARPG", StringComparison.Ordinal) == false ||
                    _contracts.ResolveContract(type) is not JsonObjectContract contract || contract.Converter != null)
                    throw new InvalidOperationException("Unsupported save DTO: " + type.FullName);

                var fields = new Dictionary<string, object?>(contract.Properties.Count, StringComparer.Ordinal);
                for (int i = 0; i < contract.Properties.Count; i++)
                {
                    JsonProperty property = contract.Properties[i];
                    if (property.Ignored == true || property.Readable == false) continue;
                    if (property.Converter != null || property.ItemConverter != null)
                        throw new InvalidOperationException("Unsupported save member converter: " + property.PropertyName);
                    if (property.ShouldSerialize != null && property.ShouldSerialize(source) == false) continue;
                    fields.Add(property.PropertyName!, Copy(property.ValueProvider!.GetValue(source), ancestors, depth + 1));
                }
                return fields;
            }
            finally { ancestors.Remove(source); }
        }

        private sealed class ReferenceIdentity : IEqualityComparer<object>
        {
            public static readonly ReferenceIdentity Instance = new();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
    }
}
